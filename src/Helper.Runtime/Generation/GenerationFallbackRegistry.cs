using System.Text.RegularExpressions;

namespace Helper.Runtime.Generation;

internal static class GenerationFallbackRegistry
{
    public const string Marker = "GENERATION_FALLBACK";
    public const string EmptyMethodBodyDiagnostic = "Method body was empty. Deterministic fallback injected.";
    public const string SemanticGuardInjectedDiagnostic = "semantic guard fallback injected.";
    public const string SemanticGuardParseFailureDiagnostic = "Unable to parse method signature for semantic guard.";
    public const string GeneratedPlaceholderPhrase = "generated placeholder";
    public const string ValidationFailurePhrase = "Fallback generated due to validation failure";
    public const string FallbackBodyInjectedPhrase = "Fallback body injected";
    public const string EmptyMethodFallbackPhrase = "Method body is empty";
    public const string TodoToken = "TODO";
    public const string LogicHereToken = "logic here";
    public const string NotImplementedExceptionToken = "NotImplementedException";

    public static IReadOnlyList<GenerationPlaceholderRuleSpec> PlaceholderLineRules { get; } =
        new GenerationPlaceholderRuleSpec[]
        {
            new("generation-fallback", $@"\b{Regex.Escape(Marker)}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new("generated-placeholder", Regex.Escape(GeneratedPlaceholderPhrase), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new("todo-marker", $@"\b{Regex.Escape(TodoToken)}\b", RegexOptions.Compiled),
            new("logic-here-marker", Regex.Escape(LogicHereToken), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new("not-implemented-marker", $@"\b{Regex.Escape(NotImplementedExceptionToken)}\b", RegexOptions.Compiled),
            new("fallback-validation-failure", Regex.Escape(ValidationFailurePhrase), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new("fallback-body-injected", Regex.Escape(FallbackBodyInjectedPhrase), RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new("empty-method-fallback", Regex.Escape(EmptyMethodFallbackPhrase), RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

    public static string BuildFallbackMessage(string detail) => $"{Marker}: {detail}";

    public static string BuildEmptyMethodBodyFallbackMessage() => BuildFallbackMessage("method body is empty.");

    public static string BuildMethodSynthesisFallbackMessage(string methodName) =>
        BuildFallbackMessage($"unable to synthesize method body for {methodName}.");

    public static string BuildFileValidationFallbackMessage() =>
        BuildFallbackMessage("file failed validation and requires regeneration.");

    public static string BuildSemanticGuardInjectedDiagnostic(string relativePath, string methodName) =>
        $"{relativePath}/{methodName}: {SemanticGuardInjectedDiagnostic}";

    public static string BuildEmptySignatureFallbackWarning() =>
        "Method signature was empty. Fallback signature injected.";

    public static string BuildPropertyLikeFallbackWarning(string signature) =>
        $"Property-like declaration '{signature}' converted to method fallback.";

    public static string BuildConstructorFallbackWarning(string signature) =>
        $"Constructor-like signature '{signature}' remapped to Initialize().";

    public static string BuildMalformedSignatureFallbackWarning(string signature) =>
        $"Malformed signature '{signature}' converted to deterministic fallback.";

    public static string BuildSignatureValidationFallbackWarning(string signature) =>
        $"Signature '{signature}' failed validation and was replaced by fallback.";

    public static string BuildInterfaceFallbackWarning(string signature) =>
        $"Signature '{signature}' replaced by interface fallback.";
}

internal sealed record GenerationPlaceholderRuleSpec(string RuleId, string Pattern, RegexOptions Options);

