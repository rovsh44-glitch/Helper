using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.Runtime.Generation;

public sealed class MethodBodySemanticGuard : IMethodBodySemanticGuard
{
    private static readonly HashSet<string> FallbackCodes = new(StringComparer.Ordinal)
    {
        "CS0103",
        "CS0117",
        "CS1061",
        "CS0234",
        "CS0246",
        "CS0012"
    };

    private static readonly Lazy<IReadOnlyList<MetadataReference>> MetadataReferences = new(BuildMetadataReferences);

    public MethodBodySafetyResult Guard(string methodSignature, string methodBody)
    {
        var diagnostics = new List<string>();
        var body = methodBody?.Trim() ?? string.Empty;

        var method = ParseMethod(methodSignature);
        if (method is null)
        {
            diagnostics.Add(GenerationFallbackRegistry.SemanticGuardParseFailureDiagnostic);
            return new MethodBodySafetyResult(true, "return;", diagnostics);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            diagnostics.Add(GenerationFallbackRegistry.EmptyMethodBodyDiagnostic);
            return new MethodBodySafetyResult(true, BuildFallbackBody(method), diagnostics);
        }

        var wrapped = GenerationSyntaxProbeBuilder.BuildSemanticGuardWrapper(methodSignature, body);
        var tree = CSharpSyntaxTree.ParseText(wrapped);
        var parseErrors = tree
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (parseErrors.Count > 0)
        {
            diagnostics.AddRange(parseErrors.Select(x => $"{x.Id}: {x.GetMessage()}"));
            return new MethodBodySafetyResult(true, BuildFallbackBody(method), diagnostics);
        }

        var compilation = CSharpCompilation.Create(
            $"SemanticGuard_{Guid.NewGuid():N}",
            new[] { tree },
            MetadataReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semanticErrors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (semanticErrors.Any(d => FallbackCodes.Contains(d.Id)))
        {
            diagnostics.AddRange(semanticErrors.Where(d => FallbackCodes.Contains(d.Id)).Select(x => $"{x.Id}: {x.GetMessage()}"));
            return new MethodBodySafetyResult(true, BuildFallbackBody(method), diagnostics);
        }

        return new MethodBodySafetyResult(false, body, diagnostics);
    }

    private static MethodDeclarationSyntax? ParseMethod(string signature)
    {
        var wrapped = GenerationSyntaxProbeBuilder.BuildSemanticGuardWrapper(signature, "return;");
        var tree = CSharpSyntaxTree.ParseText(wrapped);
        return tree.GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault();
    }

    private static string BuildFallbackBody(MethodDeclarationSyntax method)
    {
        var returnType = method.ReturnType;
        var isAsync = method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));

        if (returnType is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
        {
            return "return;";
        }

        if (IsTaskLike(returnType, "Task", out var taskGenericArg))
        {
            if (isAsync)
            {
                return taskGenericArg is null ? "return;" : "return default!;";
            }

            if (taskGenericArg is null)
            {
                return "return global::System.Threading.Tasks.Task.CompletedTask;";
            }

            var argumentType = taskGenericArg.ToString();
            return $"return global::System.Threading.Tasks.Task.FromResult(default({argumentType})!);";
        }

        if (IsTaskLike(returnType, "ValueTask", out var valueTaskGenericArg))
        {
            if (isAsync)
            {
                return valueTaskGenericArg is null ? "return;" : "return default!;";
            }

            if (valueTaskGenericArg is null)
            {
                return "return global::System.Threading.Tasks.ValueTask.CompletedTask;";
            }

            var argumentType = valueTaskGenericArg.ToString();
            return $"return global::System.Threading.Tasks.ValueTask.FromResult(default({argumentType})!);";
        }

        return IsKnownValueType(returnType)
            ? "return default;"
            : "return default!;";
    }

    private static bool IsTaskLike(TypeSyntax returnType, string typeName, out TypeSyntax? genericArgument)
    {
        genericArgument = null;
        var terminal = GetTerminalType(returnType);
        if (!string.Equals(terminal.identifier, typeName, StringComparison.Ordinal))
        {
            return false;
        }

        if (terminal.generic is null || terminal.generic.TypeArgumentList.Arguments.Count == 0)
        {
            return true;
        }

        genericArgument = terminal.generic.TypeArgumentList.Arguments[0];
        return true;
    }

    private static (string identifier, GenericNameSyntax? generic) GetTerminalType(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax identifier => (identifier.Identifier.Text, null),
            GenericNameSyntax generic => (generic.Identifier.Text, generic),
            QualifiedNameSyntax qualified => GetTerminalType(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => GetTerminalType(aliasQualified.Name),
            NullableTypeSyntax nullable => GetTerminalType(nullable.ElementType),
            _ => (typeSyntax.ToString(), null)
        };
    }

    private static bool IsKnownValueType(TypeSyntax returnType)
    {
        if (returnType is NullableTypeSyntax)
        {
            return true;
        }

        if (returnType is not PredefinedTypeSyntax predefined)
        {
            return false;
        }

        return predefined.Keyword.Kind() switch
        {
            SyntaxKind.BoolKeyword => true,
            SyntaxKind.ByteKeyword => true,
            SyntaxKind.SByteKeyword => true,
            SyntaxKind.ShortKeyword => true,
            SyntaxKind.UShortKeyword => true,
            SyntaxKind.IntKeyword => true,
            SyntaxKind.UIntKeyword => true,
            SyntaxKind.LongKeyword => true,
            SyntaxKind.ULongKeyword => true,
            SyntaxKind.FloatKeyword => true,
            SyntaxKind.DoubleKeyword => true,
            SyntaxKind.DecimalKeyword => true,
            SyntaxKind.CharKeyword => true,
            _ => false
        };
    }

    private static IReadOnlyList<MetadataReference> BuildMetadataReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
            {
                continue;
            }

            if (!references.ContainsKey(assembly.Location))
            {
                references[assembly.Location] = MetadataReference.CreateFromFile(assembly.Location);
            }
        }

        return references.Values.ToList();
    }
}

