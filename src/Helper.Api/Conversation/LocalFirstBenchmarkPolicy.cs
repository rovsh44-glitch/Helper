using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public enum LocalFirstBenchmarkMode
{
    None,
    LocalOnly,
    WebRecommended,
    WebRequired
}

public sealed record LocalFirstBenchmarkDecision(
    bool IsBenchmark,
    LocalFirstBenchmarkMode Mode,
    bool RequireRussianOutput,
    bool RequireExplicitUncertainty,
    string ReasonCode);

public interface ILocalFirstBenchmarkPolicy
{
    LocalFirstBenchmarkDecision Evaluate(ChatRequestDto request, string? resolvedLanguage);
}

public sealed class LocalFirstBenchmarkPolicy : ILocalFirstBenchmarkPolicy
{
    public LocalFirstBenchmarkDecision Evaluate(ChatRequestDto request, string? resolvedLanguage)
    {
        var systemInstruction = request.SystemInstruction ?? string.Empty;
        if (!LooksLikeLocalFirstBenchmark(systemInstruction))
        {
            return new LocalFirstBenchmarkDecision(
                false,
                LocalFirstBenchmarkMode.None,
                RequireRussianOutput: false,
                RequireExplicitUncertainty: false,
                "none");
        }

        var requireRussian = string.Equals(resolvedLanguage, "ru", StringComparison.OrdinalIgnoreCase) ||
                             systemInstruction.Contains("Answer in Russian.", StringComparison.OrdinalIgnoreCase) ||
                             systemInstruction.Contains("Отвечай по-русски.", StringComparison.OrdinalIgnoreCase);
        var requireExplicitUncertainty =
            systemInstruction.Contains("state uncertainty and evidence limits explicitly", StringComparison.OrdinalIgnoreCase) ||
            systemInstruction.Contains("evidence is weak, conflicting, or incomplete", StringComparison.OrdinalIgnoreCase) ||
            systemInstruction.Contains("say so clearly instead of smoothing it over", StringComparison.OrdinalIgnoreCase) ||
            systemInstruction.Contains("неопредел", StringComparison.OrdinalIgnoreCase) ||
            systemInstruction.Contains("огранич", StringComparison.OrdinalIgnoreCase);

        if (systemInstruction.Contains("must use live web", StringComparison.OrdinalIgnoreCase) ||
            systemInstruction.Contains("supplement or verify it with live web evidence before concluding", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalFirstBenchmarkDecision(
                true,
                LocalFirstBenchmarkMode.WebRequired,
                requireRussian,
                requireExplicitUncertainty,
                "benchmark_mandatory_web");
        }

        if (systemInstruction.Contains("use live web cautiously when needed", StringComparison.OrdinalIgnoreCase) ||
            systemInstruction.Contains("Aim for at least 2 distinct web sources if they exist.", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalFirstBenchmarkDecision(
                true,
                LocalFirstBenchmarkMode.WebRecommended,
                requireRussian,
                requireExplicitUncertainty,
                "benchmark_recommended_web");
        }

        return new LocalFirstBenchmarkDecision(
            true,
            LocalFirstBenchmarkMode.LocalOnly,
            requireRussian,
            requireExplicitUncertainty,
            "benchmark_local_only");
    }

    private static bool LooksLikeLocalFirstBenchmark(string systemInstruction)
    {
        if (string.IsNullOrWhiteSpace(systemInstruction))
        {
            return false;
        }

        return systemInstruction.Contains("local-first librarian-research assistant", StringComparison.OrdinalIgnoreCase) &&
               systemInstruction.Contains("## Local Findings", StringComparison.Ordinal) &&
               systemInstruction.Contains("## Web Findings", StringComparison.Ordinal) &&
               systemInstruction.Contains("## Sources", StringComparison.Ordinal) &&
               systemInstruction.Contains("## Analysis", StringComparison.Ordinal) &&
               systemInstruction.Contains("## Conclusion", StringComparison.Ordinal) &&
               systemInstruction.Contains("## Opinion", StringComparison.Ordinal);
    }
}

