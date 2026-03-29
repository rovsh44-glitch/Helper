using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public sealed record TemplateRoutingTelemetryCandidate(
    string TemplateId,
    double Score,
    int TokenMatches,
    bool Certified,
    bool HasCriticalAlerts,
    IReadOnlyList<string> DecisiveFeatures);

public static class TemplateRoutingTelemetry
{
    private static readonly object Sync = new();

    public static void Append(
        string prompt,
        TemplateRoutingDecision decision,
        IReadOnlyList<TemplateRoutingTelemetryCandidate> topCandidates)
    {
        try
        {
            var overridePath = Environment.GetEnvironmentVariable("HELPER_TEMPLATE_ROUTING_TELEMETRY_PATH");
            var path = string.IsNullOrWhiteSpace(overridePath)
                ? HelperWorkspacePathResolver.ResolveLogsPath("template_routing_decisions.jsonl")
                : Path.GetFullPath(overridePath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var payload = new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                promptHash = HashPrompt(prompt),
                matched = decision.Matched,
                templateId = decision.TemplateId,
                confidence = decision.Confidence,
                candidates = decision.Candidates,
                reason = decision.Reason,
                topK = topCandidates.Select(x => new
                {
                    templateId = x.TemplateId,
                    score = Math.Round(x.Score, 4),
                    tokenMatches = x.TokenMatches,
                    certified = x.Certified,
                    hasCriticalAlerts = x.HasCriticalAlerts,
                    decisiveFeatures = x.DecisiveFeatures
                }).ToArray()
            };

            var json = JsonSerializer.Serialize(payload);
            lock (Sync)
            {
                File.AppendAllText(path, json + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TemplateRoutingTelemetry] Failed to append telemetry: {ex.Message}");
        }
    }

    private static string HashPrompt(string prompt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prompt ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

