using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public partial class AILink
    {
        private static readonly int HttpTimeoutSeconds = ReadBoundedIntEnvironment("HELPER_AI_HTTP_TIMEOUT_SEC", 900, 30, 3600);
        private static readonly int EmbeddingTimeoutSeconds = ReadBoundedIntEnvironment("HELPER_AI_EMBED_TIMEOUT_SEC", 90, 10, 600);
        private static readonly int HttpPooledConnectionLifetimeSeconds = ReadBoundedIntEnvironment("HELPER_AI_HTTP_POOLED_CONNECTION_LIFETIME_SEC", 180, 15, 1800);
        private static readonly int HttpPooledConnectionIdleTimeoutSeconds = ReadBoundedIntEnvironment("HELPER_AI_HTTP_POOLED_CONNECTION_IDLE_TIMEOUT_SEC", 30, 5, 600);
        private readonly HttpClient _httpClient;
        private string _currentModel;
        private List<string> _availableModels = new();
        private readonly object _modelCatalogLock = new();
        private readonly SemaphoreSlim _vramLock = new(1, 1);

        public AILink(string baseUrl = "http://localhost:11434", string defaultModel = "qwen2.5-coder:14b")
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromSeconds(HttpPooledConnectionLifetimeSeconds),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(HttpPooledConnectionIdleTimeoutSeconds),
                MaxConnectionsPerServer = 8
            };

            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds)
            };
            _currentModel = defaultModel;
        }

        public async Task DiscoverModelsAsync(CancellationToken ct = default)
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/tags", ct);
                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var discovered = doc.RootElement.GetProperty("models")
                    .EnumerateArray()
                    .Select(m => m.GetProperty("name").GetString() ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (discovered.Count == 0)
                {
                    return;
                }

                lock (_modelCatalogLock)
                {
                    _availableModels = discovered;
                    if (!_availableModels.Contains(_currentModel, StringComparer.OrdinalIgnoreCase))
                    {
                        _currentModel = SelectPreferredDefaultModel(_availableModels, _currentModel);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AILink] Model discovery failed: {ex.Message}");
            }
        }

        public IReadOnlyList<string> GetAvailableModelsSnapshot()
        {
            lock (_modelCatalogLock)
            {
                return _availableModels.ToList();
            }
        }

        public string GetCurrentModel()
        {
            lock (_modelCatalogLock)
            {
                return _currentModel;
            }
        }

        public string GetBestModel(string category)
        {
            List<string> catalog;
            string current;
            lock (_modelCatalogLock)
            {
                catalog = _availableModels.ToList();
                current = _currentModel;
            }

            var overrideModel = ResolveCategoryOverride(category);
            if (!string.IsNullOrWhiteSpace(overrideModel))
            {
                return overrideModel;
            }

            if (catalog.Count == 0)
            {
                return current;
            }

            return category.ToLowerInvariant() switch
            {
                "fast" => catalog.FirstOrDefault(m => m.Contains("7b", StringComparison.OrdinalIgnoreCase))
                    ?? catalog.FirstOrDefault(m => m.Contains("8b", StringComparison.OrdinalIgnoreCase))
                    ?? current,
                "coder" => catalog.FirstOrDefault(m => m.Contains("coder:14b", StringComparison.OrdinalIgnoreCase))
                    ?? catalog.FirstOrDefault(m => m.Contains("coder", StringComparison.OrdinalIgnoreCase))
                    ?? current,
                "reasoning" => catalog.FirstOrDefault(m => m.Contains("r1:14b", StringComparison.OrdinalIgnoreCase))
                    ?? catalog.FirstOrDefault(m => m.Contains("r1", StringComparison.OrdinalIgnoreCase))
                    ?? current,
                "vision" => catalog.FirstOrDefault(m => m.Contains("vl", StringComparison.OrdinalIgnoreCase))
                    ?? current,
                _ => current
            };
        }

        public void SwitchModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                return;
            }

            lock (_modelCatalogLock)
            {
                _currentModel = modelName.Trim();
            }
        }

        public async Task PreloadModelAsync(string category, CancellationToken ct)
        {
            var targetModel = GetBestModel(category);
            var requestBody = new { model = targetModel, prompt = string.Empty, keep_alive = 300 };
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            await _httpClient.PostAsync("/api/generate", content, ct);
        }

        private static string SelectPreferredDefaultModel(IReadOnlyList<string> models, string fallback)
        {
            if (models.Count == 0)
            {
                return fallback;
            }

            return models.FirstOrDefault(m => m.Equals("qwen2.5-coder:14b", StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault(m => m.Contains("qwen2.5-coder", StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault(m => m.Contains("coder:14b", StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault(m => m.Contains("14b", StringComparison.OrdinalIgnoreCase))
                ?? models.First();
        }

        private string ResolveCoderFallbackModel()
        {
            var bestCoder = GetBestModel("coder");
            return string.IsNullOrWhiteSpace(bestCoder) ? "qwen2.5-coder:14b" : bestCoder;
        }

        private string? ResolveVisionFallbackModel()
        {
            var overrideModel = ResolveCategoryOverride("vision");
            if (!string.IsNullOrWhiteSpace(overrideModel))
            {
                return overrideModel;
            }

            List<string> catalog;
            string current;
            lock (_modelCatalogLock)
            {
                catalog = _availableModels.ToList();
                current = _currentModel;
            }

            if (catalog.Count == 0)
            {
                return current.Contains("vl", StringComparison.OrdinalIgnoreCase) ? current : null;
            }

            return catalog.FirstOrDefault(m => m.Contains("vl", StringComparison.OrdinalIgnoreCase));
        }

        private string? ResolveRequestFallbackModel(string? base64Image)
            => string.IsNullOrWhiteSpace(base64Image) ? ResolveCoderFallbackModel() : ResolveVisionFallbackModel();

        private static string? ResolveCategoryOverride(string category)
        {
            var envName = category.ToLowerInvariant() switch
            {
                "fast" => "HELPER_MODEL_FAST",
                "coder" => "HELPER_MODEL_CODER",
                "reasoning" => "HELPER_MODEL_REASONING",
                "vision" => "HELPER_MODEL_VISION",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(envName))
            {
                return null;
            }

            var raw = Environment.GetEnvironmentVariable(envName);
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }

        private static int ReadBoundedIntEnvironment(string name, int defaultValue, int minValue, int maxValue)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            if (!int.TryParse(raw, out var parsed))
            {
                return defaultValue;
            }

            return Math.Clamp(parsed, minValue, maxValue);
        }
    }
}

