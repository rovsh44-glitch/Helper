namespace Helper.Runtime.Generation;

public static class GenerationWorkloadClassifier
{
    public const string Parity = "parity";
    public const string Smoke = "smoke";
    public const string General = "general";
    public const string Legacy = "legacy";

    public static string Resolve(string? prompt)
    {
        var overrideClass = Normalize(Environment.GetEnvironmentVariable("HELPER_GENERATION_WORKLOAD_CLASS"));
        if (!string.IsNullOrWhiteSpace(overrideClass))
        {
            return overrideClass;
        }

        return GoldenTemplateIntentClassifier.HasExplicitGoldenTemplateRequest(prompt)
            ? Parity
            : General;
    }

    public static string NormalizeOrDefault(string? rawClass, string fallback = Legacy)
        => Normalize(rawClass) ?? fallback;

    public static string? Normalize(string? rawClass)
    {
        if (string.IsNullOrWhiteSpace(rawClass))
        {
            return null;
        }

        var normalized = rawClass.Trim().ToLowerInvariant();
        return normalized switch
        {
            "parity" => Parity,
            "golden" => Parity,
            "golden_parity" => Parity,
            "smoke" => Smoke,
            "smoke_compile" => Smoke,
            "general" => General,
            "default" => General,
            "legacy" => Legacy,
            _ => normalized
        };
    }
}

