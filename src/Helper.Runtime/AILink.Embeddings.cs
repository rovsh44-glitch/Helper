using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Infrastructure
{
    public partial class AILink
    {
        public virtual async Task<float[]> EmbedAsync(string text, CancellationToken ct)
        {
            await _vramLock.WaitAsync(ct);
            try
            {
                return await EmbedInternalAsync(text, ct);
            }
            finally
            {
                _vramLock.Release();
            }
        }

        private async Task<float[]> EmbedInternalAsync(string text, CancellationToken ct)
        {
            var candidates = BuildEmbeddingCandidates(text).ToList();
            string? lastFailure = null;
            Exception? lastException = null;

            foreach (var candidate in candidates)
            {
                foreach (var attempt in Enumerable.Range(1, 4))
                {
                    var embedWatch = Stopwatch.StartNew();
                    using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    requestCts.CancelAfter(TimeSpan.FromSeconds(EmbeddingTimeoutSeconds));

                    try
                    {
                        using var response = await _httpClient.PostAsync(
                            "/api/embeddings",
                            new StringContent(JsonSerializer.Serialize(new { model = "nomic-embed-text:latest", prompt = candidate }), Encoding.UTF8, "application/json"),
                            requestCts.Token);

                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync(requestCts.Token);
                            using var doc = JsonDocument.Parse(json);
                            var embedding = doc.RootElement.GetProperty("embedding").EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
                            if (embedWatch.ElapsedMilliseconds >= 5_000)
                            {
                                Console.WriteLine($"[AILink] Slow embedding completed. Attempt={attempt};chars={candidate.Length};elapsedMs={embedWatch.ElapsedMilliseconds}");
                            }

                            return embedding;
                        }

                        var body = await SafeReadBodyAsync(response, requestCts.Token);
                        lastFailure = $"status={(int)response.StatusCode};attempt={attempt};chars={candidate.Length};body={TruncateForLog(body, 240)}";
                        lastException = new HttpRequestException($"Embedding endpoint returned {(int)response.StatusCode}. {lastFailure}");
                        Console.WriteLine($"[AILink] Embedding candidate failed. {lastFailure}");

                        if (!IsTransientStatusCode(response.StatusCode))
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                    {
                        lastFailure = $"timeout={EmbeddingTimeoutSeconds}s;attempt={attempt};chars={candidate.Length}";
                        lastException = ex;
                        Console.WriteLine($"[AILink] Embedding candidate timed out. {lastFailure}");
                    }
                    catch (HttpRequestException ex)
                    {
                        lastFailure = $"http_error;attempt={attempt};chars={candidate.Length};message={TruncateForLog(ex.Message, 240)}";
                        lastException = ex;
                        Console.WriteLine($"[AILink] Embedding candidate transport failure. {lastFailure}");
                    }

                    if (attempt < 4)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt * attempt), ct);
                    }
                }
            }

            throw lastException ?? new HttpRequestException("Embedding endpoint failed for all candidate payloads.");
        }

        private static IEnumerable<string> BuildEmbeddingCandidates(string text)
        {
            var sanitized = PrepareEmbeddingInput(text);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                yield return "empty";
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var maxChars in new[] { 4000, 3000, 2000, 1200 })
            {
                var candidate = sanitized.Length <= maxChars
                    ? sanitized
                    : TruncateBalanced(sanitized, maxChars);
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static string PrepareEmbeddingInput(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var sanitized = new string(text
                .Where(static ch => ch == '\r' || ch == '\n' || ch == '\t' || !char.IsControl(ch))
                .ToArray());

            if (sanitized.StartsWith("[Source:", StringComparison.OrdinalIgnoreCase))
            {
                var newlineIndex = sanitized.IndexOf('\n');
                if (newlineIndex >= 0 && newlineIndex + 1 < sanitized.Length)
                {
                    sanitized = sanitized[(newlineIndex + 1)..];
                }
            }

            sanitized = sanitized.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            sanitized = string.Join("\n", sanitized
                .Split('\n')
                .Select(static line => line.Trim())
                .Where(static line => !string.IsNullOrWhiteSpace(line)));

            return sanitized.Trim();
        }

        private static string TruncateBalanced(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            {
                return text;
            }

            if (maxChars < 64)
            {
                return text[..maxChars];
            }

            var headLength = Math.Max(32, (maxChars - 16) / 2);
            var tailLength = Math.Max(16, maxChars - headLength - 16);
            if (headLength + tailLength + 16 > text.Length)
            {
                return text[..maxChars];
            }

            return string.Concat(
                text.AsSpan(0, headLength),
                "\n...\n",
                text.AsSpan(text.Length - tailLength, tailLength));
        }
    }
}

