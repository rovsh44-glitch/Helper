using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Infrastructure
{
    public partial class AILink
    {
        private async Task<HttpResponseMessage> PostJsonWithRetryAsync(AiProviderRuntimeSettings settings, string endpoint, string payload, CancellationToken ct, int maxAttempts = 6)
        {
            HttpResponseMessage? lastResponse = null;
            for (var attempt = 1; attempt <= Math.Max(maxAttempts, 1); attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var request = CreateRequest(HttpMethod.Post, settings, endpoint, payload);
                    var response = await _httpClient.SendAsync(request, ct);
                    if (response.IsSuccessStatusCode || attempt == maxAttempts)
                    {
                        return response;
                    }

                    if (!await ShouldRetryResponseAsync(endpoint, response, ct))
                    {
                        return response;
                    }

                    lastResponse = response;
                    Console.WriteLine($"[AILink] Transient upstream failure on {endpoint}. Attempt {attempt}/{maxAttempts} returned {(int)response.StatusCode}. Retrying...");
                }
                catch (HttpRequestException ex) when (attempt < maxAttempts)
                {
                    Console.WriteLine($"[AILink] HttpRequestException on {endpoint}. Attempt {attempt}/{maxAttempts}: {ex.Message}");
                }

                if (lastResponse is not null)
                {
                    lastResponse.Dispose();
                    lastResponse = null;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt * attempt), ct);
            }

            throw new HttpRequestException($"Failed to reach upstream endpoint '{endpoint}' after {maxAttempts} attempts.");
        }

        private static bool IsTransientStatusCode(System.Net.HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return code == 408 || code == 429 || code == 500 || code == 502 || code == 503 || code == 504;
        }

        private static async Task<bool> ShouldRetryResponseAsync(string endpoint, HttpResponseMessage response, CancellationToken ct)
        {
            if (!IsTransientStatusCode(response.StatusCode))
            {
                return false;
            }

            if (endpoint.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase))
            {
                var body = await SafeReadBodyAsync(response, ct);
                if (body.Contains("input length exceeds the context length", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
        {
            try
            {
                return await response.Content.ReadAsStringAsync(ct);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string TruncateForLog(string? text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
            return normalized.Length <= maxChars ? normalized : normalized[..maxChars];
        }
    }
}

