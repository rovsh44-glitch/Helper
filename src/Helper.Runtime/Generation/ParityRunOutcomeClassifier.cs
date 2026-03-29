namespace Helper.Runtime.Generation;

internal enum ParityRunDisposition
{
    CleanSuccess,
    DegradedSuccess,
    Failure
}

internal enum ParityFailureCategory
{
    RoutingMismatch,
    BlueprintWeakness,
    CompileGateFail,
    SemanticFallbackOveruse,
    ReportPersistenceMismatch,
    Unknown
}

internal sealed record ParityRunOutcome(
    ParityRunDisposition Disposition,
    bool SemanticallyDegraded,
    bool DegradesParityMetrics,
    ParityFailureCategory? FailureCategory,
    string? PrimaryFailureEvidence)
{
    internal bool Success => Disposition == ParityRunDisposition.CleanSuccess;
}

internal static class ParityRunOutcomeClassifier
{
    internal static ParityRunOutcome Evaluate(
        bool compileGatePassed,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> placeholderFindings,
        bool? artifactValidationPassed,
        bool? smokePassed)
    {
        var semanticDegradation = DetectSemanticDegradation(errors, warnings, placeholderFindings, artifactValidationPassed, smokePassed);
        var structurallySuccessful = compileGatePassed &&
                                     errors.Count == 0 &&
                                     artifactValidationPassed != false &&
                                     smokePassed != false;

        if (structurallySuccessful && !semanticDegradation)
        {
            return new ParityRunOutcome(
                Disposition: ParityRunDisposition.CleanSuccess,
                SemanticallyDegraded: false,
                DegradesParityMetrics: false,
                FailureCategory: null,
                PrimaryFailureEvidence: null);
        }

        var evidence = SelectPrimaryEvidence(errors, warnings, placeholderFindings, artifactValidationPassed, smokePassed);
        var category = ClassifyFailure(compileGatePassed, errors, warnings, placeholderFindings, artifactValidationPassed, smokePassed, evidence);
        var disposition = structurallySuccessful && semanticDegradation
            ? ParityRunDisposition.DegradedSuccess
            : ParityRunDisposition.Failure;
        return new ParityRunOutcome(
            Disposition: disposition,
            SemanticallyDegraded: semanticDegradation,
            DegradesParityMetrics: disposition != ParityRunDisposition.CleanSuccess,
            FailureCategory: category,
            PrimaryFailureEvidence: evidence);
    }

    private static bool DetectSemanticDegradation(
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> placeholderFindings,
        bool? artifactValidationPassed,
        bool? smokePassed)
    {
        if (placeholderFindings.Count > 0 || artifactValidationPassed == false || smokePassed == false)
        {
            return true;
        }

        return errors.Any(LooksLikeSemanticFallback) || warnings.Any(LooksLikeSemanticFallback);
    }

    private static ParityFailureCategory ClassifyFailure(
        bool compileGatePassed,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> placeholderFindings,
        bool? artifactValidationPassed,
        bool? smokePassed,
        string? evidence)
    {
        var normalizedEvidence = evidence ?? string.Empty;
        if (LooksLikeRoutingMismatch(normalizedEvidence))
        {
            return ParityFailureCategory.RoutingMismatch;
        }

        if (LooksLikeBlueprintWeakness(normalizedEvidence))
        {
            return ParityFailureCategory.BlueprintWeakness;
        }

        if (placeholderFindings.Count > 0 ||
            artifactValidationPassed == false ||
            smokePassed == false ||
            errors.Any(LooksLikeSemanticFallback) ||
            warnings.Any(LooksLikeSemanticFallback))
        {
            return ParityFailureCategory.SemanticFallbackOveruse;
        }

        if (!compileGatePassed || errors.Any(LooksLikeCompileGateFailure))
        {
            return ParityFailureCategory.CompileGateFail;
        }

        if (compileGatePassed && errors.Count == 0 && warnings.Count == 0 && placeholderFindings.Count == 0)
        {
            return ParityFailureCategory.ReportPersistenceMismatch;
        }

        return ParityFailureCategory.Unknown;
    }

    private static string? SelectPrimaryEvidence(
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> placeholderFindings,
        bool? artifactValidationPassed,
        bool? smokePassed)
    {
        var evidence = errors.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            return evidence.Trim();
        }

        evidence = warnings.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            return evidence.Trim();
        }

        evidence = placeholderFindings.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            return evidence.Trim();
        }

        if (artifactValidationPassed == false)
        {
            return "ArtifactValidationPassed=false";
        }

        if (smokePassed == false)
        {
            return "SmokePassed=false";
        }

        return null;
    }

    private static bool LooksLikeRoutingMismatch(string evidence)
        => ContainsAny(evidence,
            "GOLDEN_ROUTE_MISMATCH",
            "TEMPLATE_NOT_FOUND",
            "TEMPLATE_BLOCKED_BY_CERTIFICATION_STATUS",
            "route mismatch",
            "failed to procure template");

    private static bool LooksLikeBlueprintWeakness(string evidence)
        => ContainsAny(evidence,
            "BLUEPRINT",
            "SCHEMA",
            "CONTRACT",
            "Project type unsupported",
            "PROJECT_TYPE_UNSUPPORTED",
            "validation:");

    private static bool LooksLikeCompileGateFailure(string evidence)
        => ContainsAny(evidence,
            "CS",
            "MSB",
            "DUPLICATE_SIGNATURE",
            "compile gate",
            "compile failure",
            "build failed",
            "FORMAT");

    private static bool LooksLikeSemanticFallback(string evidence)
        => ContainsAny(evidence,
            "PlaceholderScan",
            "placeholder",
            "TODO",
            "NotImplementedException",
            "fallback",
            "Smoke[",
            "ArtifactValidation:");

    private static bool ContainsAny(string source, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        foreach (var needle in needles)
        {
            if (source.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

