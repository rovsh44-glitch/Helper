using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.Runtime.Generation;

public sealed class TypeTokenExtractor : ITypeTokenExtractor
{
    private static readonly Regex IdentifierTokenRegex = new(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled);
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "void", "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double", "decimal", "char", "string", "object", "nint", "nuint"
    };

    public IReadOnlyCollection<string> ExtractFromSignature(string signature)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(signature))
        {
            return Array.Empty<string>();
        }

        if (TryExtractViaRoslyn(signature, tokens))
        {
            return tokens.ToList();
        }

        // Fallback for malformed signatures that still carry useful type hints.
        foreach (Match match in IdentifierTokenRegex.Matches(signature))
        {
            var token = match.Value;
            if (Keywords.Contains(token))
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens.ToList();
    }

    private static bool TryExtractViaRoslyn(string signature, HashSet<string> tokens)
    {
        var wrapped = GenerationSyntaxProbeBuilder.BuildMethodProbeWrapper("TypeTokenGuard", signature);
        var tree = CSharpSyntaxTree.ParseText(wrapped);
        var parseErrors = tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error);
        if (parseErrors)
        {
            return false;
        }

        var method = tree.GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null)
        {
            return false;
        }

        CollectTokens(method.ReturnType, tokens);
        foreach (var parameter in method.ParameterList.Parameters)
        {
            if (parameter.Type is not null)
            {
                CollectTokens(parameter.Type, tokens);
            }
        }

        return true;
    }

    private static void CollectTokens(TypeSyntax typeSyntax, HashSet<string> tokens)
    {
        switch (typeSyntax)
        {
            case PredefinedTypeSyntax:
                return;
            case IdentifierNameSyntax identifierName:
                AddToken(identifierName.Identifier.Text, tokens);
                return;
            case GenericNameSyntax genericName:
                AddToken(genericName.Identifier.Text, tokens);
                foreach (var argument in genericName.TypeArgumentList.Arguments)
                {
                    CollectTokens(argument, tokens);
                }

                return;
            case QualifiedNameSyntax qualifiedName:
                CollectTokens(qualifiedName.Left, tokens);
                CollectTokens(qualifiedName.Right, tokens);
                return;
            case AliasQualifiedNameSyntax aliasQualifiedName:
                CollectTokens(aliasQualifiedName.Name, tokens);
                return;
            case NullableTypeSyntax nullableType:
                CollectTokens(nullableType.ElementType, tokens);
                return;
            case ArrayTypeSyntax arrayType:
                CollectTokens(arrayType.ElementType, tokens);
                return;
            case TupleTypeSyntax tupleType:
                foreach (var element in tupleType.Elements)
                {
                    CollectTokens(element.Type, tokens);
                }

                return;
            default:
                foreach (var id in typeSyntax.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken)))
                {
                    AddToken(id.ValueText, tokens);
                }

                return;
        }
    }

    private static void AddToken(string token, HashSet<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(token) || Keywords.Contains(token))
        {
            return;
        }

        tokens.Add(token);
    }
}

