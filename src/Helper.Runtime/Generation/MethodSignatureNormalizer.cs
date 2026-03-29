using System.Text.RegularExpressions;
using Helper.Runtime.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.Runtime.Generation;

public sealed class MethodSignatureNormalizer : IMethodSignatureNormalizer
{
    private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);
    private readonly IMethodSignatureValidator _validator;

    public MethodSignatureNormalizer(IMethodSignatureValidator validator)
    {
        _validator = validator;
    }

    public MethodSignatureNormalizationResult Normalize(string? signature, FileRole role, string? preferredMethodName = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var normalizedMethodName = NormalizeMethodName(preferredMethodName, role == FileRole.Interface);

        if (string.IsNullOrWhiteSpace(signature))
        {
            warnings.Add(GenerationFallbackRegistry.BuildEmptySignatureFallbackWarning());
            return BuildFallback(role, normalizedMethodName, warnings, errors);
        }

        var candidate = StripBodyAndNormalizeWhitespace(signature);
        if (IsPropertyLike(candidate))
        {
            warnings.Add(GenerationFallbackRegistry.BuildPropertyLikeFallbackWarning(signature));
            return BuildFallback(role, normalizedMethodName, warnings, errors);
        }

        if (ContainsCtorToken(candidate))
        {
            warnings.Add(GenerationFallbackRegistry.BuildConstructorFallbackWarning(signature));
            normalizedMethodName = "Initialize";
            return BuildFallback(role, normalizedMethodName, warnings, errors);
        }

        if (!candidate.Contains('(') || !candidate.Contains(')'))
        {
            warnings.Add(GenerationFallbackRegistry.BuildMalformedSignatureFallbackWarning(signature));
            return BuildFallback(role, normalizedMethodName, warnings, errors);
        }

        var validated = _validator.Validate(candidate);
        if (!validated.IsValid || string.IsNullOrWhiteSpace(validated.NormalizedSignature))
        {
            errors.AddRange(validated.Errors);
            warnings.Add(GenerationFallbackRegistry.BuildSignatureValidationFallbackWarning(signature));
            return BuildFallback(role, normalizedMethodName, warnings, errors);
        }

        var normalizedSignature = validated.NormalizedSignature!;
        if (role == FileRole.Interface)
        {
            if (!TryNormalizeForInterface(normalizedSignature, out var interfaceSignature, out var interfaceError))
            {
                errors.Add(interfaceError ?? "Unable to normalize interface signature.");
                warnings.Add(GenerationFallbackRegistry.BuildInterfaceFallbackWarning(signature));
                return BuildFallback(role, normalizedMethodName, warnings, errors);
            }

            normalizedSignature = interfaceSignature!;
        }

        return new MethodSignatureNormalizationResult(true, normalizedSignature, errors, warnings);
    }

    private MethodSignatureNormalizationResult BuildFallback(
        FileRole role,
        string methodName,
        List<string> warnings,
        List<string> errors)
    {
        var fallback = role == FileRole.Interface
            ? $"void {methodName}()"
            : $"public void {methodName}()";

        var validated = _validator.Validate(fallback);
        if (!validated.IsValid || string.IsNullOrWhiteSpace(validated.NormalizedSignature))
        {
            errors.AddRange(validated.Errors);
            return new MethodSignatureNormalizationResult(false, null, errors, warnings);
        }

        return new MethodSignatureNormalizationResult(true, validated.NormalizedSignature!, errors, warnings);
    }

    private static string StripBodyAndNormalizeWhitespace(string signature)
    {
        var trimmed = signature.Trim();
        var expressionBodyIndex = trimmed.IndexOf("=>", StringComparison.Ordinal);
        if (expressionBodyIndex >= 0)
        {
            trimmed = trimmed[..expressionBodyIndex].Trim();
        }

        var bodyIndex = trimmed.IndexOf('{');
        if (bodyIndex >= 0)
        {
            trimmed = trimmed[..bodyIndex].Trim();
        }

        if (trimmed.EndsWith(';'))
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        return MultiSpace.Replace(trimmed, " ");
    }

    private static bool IsPropertyLike(string signature)
    {
        if (signature.Contains('(') || signature.Contains(')'))
        {
            return false;
        }

        return signature.Contains(" get;", StringComparison.Ordinal) ||
               signature.Contains(" set;", StringComparison.Ordinal) ||
               signature.Contains(" init;", StringComparison.Ordinal) ||
               signature.Contains('{');
    }

    private static bool ContainsCtorToken(string signature)
    {
        return signature.Contains(".ctor", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(signature, "ctor", StringComparison.OrdinalIgnoreCase) ||
               signature.StartsWith("ctor(", StringComparison.OrdinalIgnoreCase) ||
               signature.Contains(" ctor(", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMethodName(string? preferredMethodName, bool interfaceStyle)
    {
        var raw = string.IsNullOrWhiteSpace(preferredMethodName) ? "Execute" : preferredMethodName.Trim();
        if (string.Equals(raw, "_ctor", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "ctor", StringComparison.OrdinalIgnoreCase))
        {
            raw = "Initialize";
        }

        var chars = raw.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray();
        var candidate = chars.Length == 0 ? "Execute" : new string(chars);
        if (!SyntaxFacts.IsValidIdentifier(candidate))
        {
            candidate = interfaceStyle ? "Execute" : "Execute";
        }

        return candidate;
    }

    private static bool TryNormalizeForInterface(string signature, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        var wrapped = GenerationSyntaxProbeBuilder.BuildMethodProbeWrapper("SignatureGuard", signature);
        var tree = CSharpSyntaxTree.ParseText(wrapped);
        var parseErrors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (parseErrors.Count > 0)
        {
            error = parseErrors[0].GetMessage();
            return false;
        }

        var method = tree.GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null)
        {
            error = "Unable to parse interface method declaration.";
            return false;
        }

        // Interface members must not carry "async" and body indicators in this pipeline.
        var cleanedModifiers = method.Modifiers.Where(m => !m.IsKind(SyntaxKind.AsyncKeyword)).ToArray();
        var cleaned = method
            .WithAttributeLists(default)
            .WithBody(null)
            .WithExpressionBody(null)
            .WithModifiers(new SyntaxTokenList(cleanedModifiers))
            .WithSemicolonToken(default);

        var cleanedText = cleaned.NormalizeWhitespace().ToFullString();
        cleanedText = Regex.Replace(cleanedText, @"\b(public|private|protected|internal|static|virtual|override|abstract|sealed|extern|partial|new|unsafe)\b", string.Empty);
        cleanedText = MultiSpace.Replace(cleanedText, " ").Trim();
        normalized = cleanedText;
        return true;
    }
}

