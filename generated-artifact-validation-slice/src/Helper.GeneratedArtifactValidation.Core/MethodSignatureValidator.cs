using System.Text.RegularExpressions;
using Helper.GeneratedArtifactValidation.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.GeneratedArtifactValidation.Core;

public sealed class MethodSignatureValidator
{
    private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);

    public MethodSignatureValidationResult Validate(string? signature)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(signature))
        {
            errors.Add("Method signature is empty.");
            return new MethodSignatureValidationResult(false, null, errors);
        }

        var normalized = Normalize(signature);
        var candidate = GenerationSyntaxProbeBuilder.BuildMethodProbeWrapper("SignatureGuard", normalized);
        var tree = CSharpSyntaxTree.ParseText(candidate);
        var parseErrors = tree
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();

        if (parseErrors.Count > 0)
        {
            return new MethodSignatureValidationResult(false, normalized, parseErrors);
        }

        var root = tree.GetCompilationUnitRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null)
        {
            errors.Add("Unable to parse method declaration from signature.");
            return new MethodSignatureValidationResult(false, normalized, errors);
        }

        if (!SyntaxFacts.IsValidIdentifier(method.Identifier.Text))
        {
            errors.Add($"Method name '{method.Identifier.Text}' is not a valid C# identifier.");
            return new MethodSignatureValidationResult(false, normalized, errors);
        }

        var methodSignature = method
            .WithBody(null)
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .NormalizeWhitespace()
            .ToFullString()
            .Trim();

        return new MethodSignatureValidationResult(true, methodSignature, Array.Empty<string>());
    }

    private static string Normalize(string signature)
    {
        var trimmed = signature.Trim();

        var braceIndex = trimmed.IndexOf('{');
        if (braceIndex >= 0)
        {
            trimmed = trimmed[..braceIndex].Trim();
        }

        if (trimmed.EndsWith(';'))
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        return MultiSpace.Replace(trimmed, " ");
    }
}

