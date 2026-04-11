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
            var settings = GetRuntimeSettingsSnapshot();
            var modelToUse = overrideModel ?? GetCurrentModel();
            using var response = await PostJsonWithRetryAsync(
                settings,
                settings.TransportKind == AiTransportKind.OpenAiCompatible ? "/chat/completions" : "/api/generate",
                BuildAskPayload(settings, modelToUse, prompt, base64Image, keepAliveSeconds, systemInstruction),
                ct);

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
            return SanitizeAssistantResponse(ExtractAssistantResponse(settings, json));
        }

        private async IAsyncEnumerable<string> StreamInternalAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken ct,
            string? overrideModel = null,
            string? base64Image = null,
            int keepAliveSeconds = 300,
            string? systemInstruction = null)
        {
            var settings = GetRuntimeSettingsSnapshot();
            var modelToUse = overrideModel ?? GetCurrentModel();
            using var request = CreateRequest(
                HttpMethod.Post,
                settings,
                settings.TransportKind == AiTransportKind.OpenAiCompatible ? "/chat/completions" : "/api/generate",
                BuildStreamPayload(settings, modelToUse, prompt, base64Image, keepAliveSeconds, systemInstruction));
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

                if (!TryParseStreamChunk(settings, line, out var rawToken, out var isDone))
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

        private static bool TryParseStreamChunk(AiProviderRuntimeSettings settings, string line, out string token, out bool done)
        {
            if (settings.TransportKind == AiTransportKind.OpenAiCompatible)
            {
                return TryParseOpenAiStreamChunk(line, out token, out done);
            }

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

        private static bool TryParseOpenAiStreamChunk(string line, out string token, out bool done)
        {
            token = string.Empty;
            done = false;
            var payload = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? line["data:".Length..].Trim()
                : line.Trim();
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
            {
                done = true;
                return true;
            }

            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                {
                    return false;
                }

                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("delta", out var delta))
                {
                    token = ReadMessageContent(delta, "content");
                }
                else if (firstChoice.TryGetProperty("message", out var message))
                {
                    token = ReadMessageContent(message, "content");
                }

                if (firstChoice.TryGetProperty("finish_reason", out var finishReason) &&
                    finishReason.ValueKind != JsonValueKind.Null &&
                    !string.IsNullOrWhiteSpace(finishReason.GetString()))
                {
                    done = true;
                }

                return !string.IsNullOrEmpty(token) || done;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string BuildAskPayload(
            AiProviderRuntimeSettings settings,
            string model,
            string prompt,
            string? base64Image,
            int keepAliveSeconds,
            string? systemInstruction)
        {
            return settings.TransportKind == AiTransportKind.OpenAiCompatible
                ? BuildOpenAiCompatiblePayload(model, prompt, base64Image, systemInstruction, stream: false)
                : BuildOllamaPayload(model, prompt, base64Image, keepAliveSeconds, systemInstruction, stream: false);
        }

        private static string BuildStreamPayload(
            AiProviderRuntimeSettings settings,
            string model,
            string prompt,
            string? base64Image,
            int keepAliveSeconds,
            string? systemInstruction)
        {
            return settings.TransportKind == AiTransportKind.OpenAiCompatible
                ? BuildOpenAiCompatiblePayload(model, prompt, base64Image, systemInstruction, stream: true)
                : BuildOllamaPayload(model, prompt, base64Image, keepAliveSeconds, systemInstruction, stream: true);
        }

        private static string BuildOllamaPayload(string model, string prompt, string? base64Image, int keepAliveSeconds, string? systemInstruction, bool stream)
        {
            var contextSize = (model.Contains("32b", StringComparison.OrdinalIgnoreCase) || model.Contains("Fusion", StringComparison.OrdinalIgnoreCase)) ? 32768 : 8192;
            var temperature = string.IsNullOrWhiteSpace(base64Image) ? 0.6 : 0.1;
            var requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "prompt", prompt },
                { "stream", stream },
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

            return JsonSerializer.Serialize(requestBody);
        }

        private static string BuildOpenAiCompatiblePayload(string model, string prompt, string? base64Image, string? systemInstruction, bool stream)
        {
            var messages = new List<object>();
            if (!string.IsNullOrWhiteSpace(systemInstruction))
            {
                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "system",
                    ["content"] = systemInstruction
                });
            }

            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "user",
                ["content"] = string.IsNullOrWhiteSpace(base64Image)
                    ? prompt
                    : new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "text",
                            ["text"] = prompt
                        },
                        new Dictionary<string, object?>
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new Dictionary<string, object?>
                            {
                                ["url"] = $"data:image/png;base64,{base64Image}"
                            }
                        }
                    }
            });

            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["model"] = model,
                ["stream"] = stream,
                ["temperature"] = string.IsNullOrWhiteSpace(base64Image) ? 0.6 : 0.1,
                ["messages"] = messages
            });
        }

        private static string ExtractAssistantResponse(AiProviderRuntimeSettings settings, string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (settings.TransportKind != AiTransportKind.OpenAiCompatible)
            {
                return doc.RootElement.TryGetProperty("response", out var responseNode)
                    ? responseNode.GetString() ?? string.Empty
                    : string.Empty;
            }

            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                return string.Empty;
            }

            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message))
            {
                return ReadMessageContent(message, "content");
            }

            return string.Empty;
        }

        private static string ReadMessageContent(JsonElement parent, string propertyName)
        {
            if (!parent.TryGetProperty(propertyName, out var contentNode))
            {
                return string.Empty;
            }

            return contentNode.ValueKind switch
            {
                JsonValueKind.String => contentNode.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Concat(contentNode.EnumerateArray().Select(ReadContentPart)),
                _ => string.Empty
            };
        }

        private static string ReadContentPart(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? string.Empty;
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("text", out var textNode))
                {
                    return textNode.GetString() ?? string.Empty;
                }

                if (element.TryGetProperty("content", out var contentNode))
                {
                    return contentNode.GetString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static string SanitizeAssistantResponse(string rawResponse)
        {
            if (!rawResponse.Contains("<think>", StringComparison.Ordinal))
            {
                return rawResponse;
            }

            var endThink = rawResponse.IndexOf("</think>", StringComparison.Ordinal);
            if (endThink == -1)
            {
                return rawResponse;
            }

            return rawResponse[(endThink + 8)..].Trim();
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
            var settings = GetRuntimeSettingsSnapshot();
            if (settings.TransportKind == AiTransportKind.OpenAiCompatible)
            {
                return;
            }

            var request = new { model = GetCurrentModel(), prompt = string.Empty, keep_alive = 0 };
            using var httpRequest = CreateRequest(HttpMethod.Post, settings, "/api/generate", JsonSerializer.Serialize(request));
            await _httpClient.SendAsync(httpRequest, ct);
        }
    }
}

