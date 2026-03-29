using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public partial class AILink
    {
        public virtual async Task<string> AskAsync(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null)
        {
            await _vramLock.WaitAsync(ct);
            try
            {
                return await AskInternalAsync(prompt, ct, overrideModel, base64Image, keepAliveSeconds, systemInstruction);
            }
            finally
            {
                _vramLock.Release();
            }
        }

        public virtual async IAsyncEnumerable<string> StreamAsync(
            string prompt,
            string? overrideModel = null,
            string? base64Image = null,
            int keepAliveSeconds = 300,
            string? systemInstruction = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await _vramLock.WaitAsync(ct);
            try
            {
                await foreach (var token in StreamInternalAsync(prompt, ct, overrideModel, base64Image, keepAliveSeconds, systemInstruction))
                {
                    if (!string.IsNullOrEmpty(token))
                    {
                        yield return token;
                    }
                }
            }
            finally
            {
                _vramLock.Release();
            }
        }

        private async Task<string> AskInternalAsync(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null)
        {
            var modelToUse = overrideModel ?? GetCurrentModel();
            var contextSize = (modelToUse.Contains("32b") || modelToUse.Contains("Fusion")) ? 32768 : 8192;
            var temperature = string.IsNullOrWhiteSpace(base64Image) ? 0.6 : 0.1;

            var requestBody = new Dictionary<string, object>
            {
                { "model", modelToUse },
                { "prompt", prompt },
                { "stream", false },
                { "keep_alive", keepAliveSeconds },
                { "options", new { num_ctx = contextSize, temperature } }
            };

            if (!string.IsNullOrEmpty(systemInstruction))
            {
                requestBody["system"] = systemInstruction;
            }

            if (!string.IsNullOrEmpty(base64Image))
            {
                requestBody["images"] = new[] { base64Image };
            }

            var payload = JsonSerializer.Serialize(requestBody);
            using var response = await PostJsonWithRetryAsync("/api/generate", payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                var fallbackModel = ResolveRequestFallbackModel(base64Image);
                if (!string.IsNullOrWhiteSpace(fallbackModel) && !string.Equals(modelToUse, fallbackModel, StringComparison.OrdinalIgnoreCase))
                {
                    return await AskInternalAsync(prompt, ct, fallbackModel, base64Image, keepAliveSeconds, systemInstruction);
                }

                response.EnsureSuccessStatusCode();
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var rawResponse = doc.RootElement.GetProperty("response").GetString() ?? string.Empty;

            if (rawResponse.Contains("<think>"))
            {
                var endThink = rawResponse.IndexOf("</think>", StringComparison.Ordinal);
                if (endThink != -1)
                {
                    rawResponse = rawResponse[(endThink + 8)..].Trim();
                }
            }

            return rawResponse;
        }

        private async IAsyncEnumerable<string> StreamInternalAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken ct,
            string? overrideModel = null,
            string? base64Image = null,
            int keepAliveSeconds = 300,
            string? systemInstruction = null)
        {
            var modelToUse = overrideModel ?? GetCurrentModel();
            var contextSize = (modelToUse.Contains("32b") || modelToUse.Contains("Fusion")) ? 32768 : 8192;
            var temperature = string.IsNullOrWhiteSpace(base64Image) ? 0.6 : 0.1;

            var requestBody = new Dictionary<string, object>
            {
                { "model", modelToUse },
                { "prompt", prompt },
                { "stream", true },
                { "keep_alive", keepAliveSeconds },
                { "options", new { num_ctx = contextSize, temperature } }
            };

            if (!string.IsNullOrEmpty(systemInstruction))
            {
                requestBody["system"] = systemInstruction;
            }

            if (!string.IsNullOrEmpty(base64Image))
            {
                requestBody["images"] = new[] { base64Image };
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var fallbackModel = ResolveRequestFallbackModel(base64Image);
                if (!string.IsNullOrWhiteSpace(fallbackModel) && !string.Equals(modelToUse, fallbackModel, StringComparison.OrdinalIgnoreCase))
                {
                    await foreach (var token in StreamInternalAsync(prompt, ct, fallbackModel, base64Image, keepAliveSeconds, systemInstruction))
                    {
                        yield return token;
                    }

                    yield break;
                }

                response.EnsureSuccessStatusCode();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var inThink = false;
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!TryParseStreamChunk(line, out var rawToken, out var isDone))
                {
                    continue;
                }

                if (TryFilterThinkToken(rawToken, ref inThink, out var cleaned) && !string.IsNullOrEmpty(cleaned))
                {
                    yield return cleaned;
                }

                if (isDone)
                {
                    break;
                }
            }
        }

        private static bool TryFilterThinkToken(string token, ref bool inThink, out string cleaned)
        {
            cleaned = token;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            if (!inThink && token.Contains("<think>", StringComparison.OrdinalIgnoreCase))
            {
                inThink = true;
                var start = token.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
                cleaned = start > 0 ? token[..start] : string.Empty;
                return !string.IsNullOrWhiteSpace(cleaned);
            }

            if (inThink)
            {
                if (token.Contains("</think>", StringComparison.OrdinalIgnoreCase))
                {
                    inThink = false;
                    var end = token.IndexOf("</think>", StringComparison.OrdinalIgnoreCase) + "</think>".Length;
                    cleaned = end < token.Length ? token[end..] : string.Empty;
                    return !string.IsNullOrWhiteSpace(cleaned);
                }

                cleaned = string.Empty;
                return false;
            }

            return true;
        }

        private static bool TryParseStreamChunk(string line, out string token, out bool done)
        {
            token = string.Empty;
            done = false;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("response", out var tokenNode))
                {
                    token = tokenNode.GetString() ?? string.Empty;
                }

                if (doc.RootElement.TryGetProperty("done", out var doneNode))
                {
                    done = doneNode.GetBoolean();
                }

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public async Task<T> AskJsonAsync<T>(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null)
        {
            var response = await AskAsync(prompt, ct, overrideModel, base64Image);
            var json = response.Trim();
            if (json.Contains("```json", StringComparison.Ordinal))
            {
                json = json.Split("```json", StringSplitOptions.None)[1].Split("```", StringSplitOptions.None)[0].Trim();
            }
            else if (json.Contains("```", StringComparison.Ordinal))
            {
                json = json.Split("```", StringSplitOptions.None)[1].Split("```", StringSplitOptions.None)[0].Trim();
            }

            return JsonSerializer.Deserialize<T>(json, JsonDefaults.Options)
                ?? throw new Exception("Deserialized object is null");
        }

        public async Task ReleaseVramAsync(CancellationToken ct)
        {
            var request = new { model = GetCurrentModel(), prompt = string.Empty, keep_alive = 0 };
            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            await _httpClient.PostAsync("/api/generate", content, ct);
        }
    }
}

