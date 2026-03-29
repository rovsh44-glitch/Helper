using Helper.Runtime.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.Runtime.Generation;

internal sealed class CompileGateMissingReturnRepair
{
    private readonly IMethodBodySemanticGuard _semanticGuard;

    public CompileGateMissingReturnRepair(IMethodBodySemanticGuard semanticGuard)
    {
        _semanticGuard = semanticGuard;
    }

    public async Task<bool> TryApplyAsync(
        string compileWorkspace,
        IReadOnlyList<BuildError> errors,
        CancellationToken ct)
    {
        var diagnosticReferences = CompileGateRepairDiagnostics.ResolveDiagnosticReferences(
            compileWorkspace,
            errors,
            new HashSet<string>(new[] { "CS0161" }, StringComparer.Ordinal));
        if (diagnosticReferences.Count == 0)
        {
            return false;
        }

        var changed = false;
        foreach (var filePath in CompileGateRepairDiagnostics.EnumerateTargetCodeFiles(diagnosticReferences))
        {
            ct.ThrowIfCancellationRequested();
            var source = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            var targetLines = diagnosticReferences
                .Where(reference => string.Equals(reference.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .Select(static reference => reference.Line)
                .Where(static line => line > 0)
                .ToHashSet();
            var methods = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Parent is not InterfaceDeclarationSyntax)
                .Where(m => !string.Equals(m.ReturnType.ToString(), "void", StringComparison.OrdinalIgnoreCase))
                .Where(m => m.Body is not null || m.ExpressionBody is not null)
                .Where(m => CompileGateRepairSyntaxHelpers.DiagnosticTargetsMethod(m, targetLines))
                .ToList();

            if (methods.Count == 0)
            {
                continue;
            }

            SyntaxNode updatedRoot = root;
            var fileChanged = false;
            foreach (var method in methods)
            {
                var signature = method
                    .WithBody(null)
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .NormalizeWhitespace()
                    .ToFullString();
                var fallbackBlock = CompileGateRepairSyntaxHelpers.ParseBlock(
                    CompileGateRepairSyntaxHelpers.BuildForcedFallbackBody(_semanticGuard, signature));
                var fixedMethod = method
                    .WithBody(fallbackBlock)
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default);

                updatedRoot = updatedRoot.ReplaceNode(method, fixedMethod);
                fileChanged = true;
            }

            if (!fileChanged)
            {
                continue;
            }

            await File.WriteAllTextAsync(filePath, updatedRoot.NormalizeWhitespace().ToFullString(), ct).ConfigureAwait(false);
            changed = true;
        }

        return changed;
    }
}

