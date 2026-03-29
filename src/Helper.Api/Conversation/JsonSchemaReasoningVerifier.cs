using System.Text.Json;
using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

public sealed class JsonSchemaReasoningVerifier : IReasoningOutputVerifier
{
    private static readonly Regex JsonObjectPattern = new(@"\{[\s\S]*\}", RegexOptions.Compiled);

    public int Priority => 10;

    public ValueTask<ReasoningVerifierResult> VerifyAsync(ChatTurnContext context, CancellationToken ct)
    {
        var prompt = context.Request.Message?.Trim() ?? string.Empty;
        var output = context.ExecutionOutput?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt) ||
            string.IsNullOrWhiteSpace(output) ||
            !prompt.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(new ReasoningVerifierResult(nameof(JsonSchemaReasoningVerifier), ReasoningVerificationStatus.NotApplicable, "Prompt does not require JSON."));
        }

        if (!TryParseJson(output, out var outputJson))
        {
            return ValueTask.FromResult(new ReasoningVerifierResult(
                nameof(JsonSchemaReasoningVerifier),
                ReasoningVerificationStatus.Rejected,
                "Output is not valid JSON.",
                StructuredOutputVerifier.BuildRejectedResponse(output, "Output is not valid JSON."),
                new[] { "local_verifier:json_invalid" }));
        }

        if (TryExtractExpectedSchema(prompt, out var expectedJson) &&
            expectedJson.HasValue &&
            outputJson.HasValue &&
            !SchemaMatches(expectedJson.Value, outputJson.Value))
        {
            return ValueTask.FromResult(new ReasoningVerifierResult(
                nameof(JsonSchemaReasoningVerifier),
                ReasoningVerificationStatus.Rejected,
                "Output JSON does not match the requested schema/example.",
                StructuredOutputVerifier.BuildRejectedResponse(output, "Output JSON does not match the requested schema/example."),
                new[] { "local_verifier:json_schema_mismatch" }));
        }

        return ValueTask.FromResult(new ReasoningVerifierResult(
            nameof(JsonSchemaReasoningVerifier),
            ReasoningVerificationStatus.Approved,
            "Output passed JSON structure verification.",
            null,
            new[] { "local_verifier:json_pass" }));
    }

    private static bool TryExtractExpectedSchema(string prompt, out JsonElement? expectedJson)
    {
        expectedJson = null;
        var match = JsonObjectPattern.Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        return TryParseJson(match.Value, out expectedJson);
    }

    private static bool TryParseJson(string candidate, out JsonElement? json)
    {
        json = null;
        try
        {
            using var document = JsonDocument.Parse(candidate);
            json = document.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool SchemaMatches(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return false;
        }

        if (expected.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in expected.EnumerateObject())
            {
                if (!actual.TryGetProperty(property.Name, out var actualValue))
                {
                    return false;
                }

                if (!SchemaMatches(property.Value, actualValue))
                {
                    return false;
                }
            }

            return true;
        }

        if (expected.ValueKind == JsonValueKind.Array)
        {
            var expectedItems = expected.EnumerateArray().ToArray();
            var actualItems = actual.EnumerateArray().ToArray();
            if (expectedItems.Length == 0)
            {
                return true;
            }

            if (actualItems.Length == 0)
            {
                return false;
            }

            return SchemaMatches(expectedItems[0], actualItems[0]);
        }

        return expected.ValueKind == actual.ValueKind;
    }
}

