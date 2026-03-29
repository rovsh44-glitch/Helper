using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.Runtime.Generation;

internal sealed class CompileGateOverrideRepairSet
{
    public async Task<bool> ApplyInvalidOverrideFixAsync(string compileWorkspace, CancellationToken ct)
    {
        var changed = false;
        foreach (var filePath in CompileGateRepairDiagnostics.EnumerateCodeFiles(compileWorkspace))
        {
            ct.ThrowIfCancellationRequested();
            var source = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            SyntaxNode updatedRoot = root;
            var fileChanged = false;
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!method.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.OverrideKeyword)))
                {
                    continue;
                }

                var fixedMethod = method.WithModifiers(new SyntaxTokenList(
                    method.Modifiers.Where(static modifier => !modifier.IsKind(SyntaxKind.OverrideKeyword))));
                updatedRoot = updatedRoot.ReplaceNode(method, fixedMethod);
                fileChanged = true;
            }

            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                if (!property.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.OverrideKeyword)))
                {
                    continue;
                }

                var fixedProperty = property.WithModifiers(new SyntaxTokenList(
                    property.Modifiers.Where(static modifier => !modifier.IsKind(SyntaxKind.OverrideKeyword))));
                updatedRoot = updatedRoot.ReplaceNode(property, fixedProperty);
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

    public async Task<bool> ApplyInvalidExplicitInterfaceSpecifierFixAsync(string compileWorkspace, CancellationToken ct)
    {
        var changed = false;
        foreach (var filePath in CompileGateRepairDiagnostics.EnumerateCodeFiles(compileWorkspace))
        {
            ct.ThrowIfCancellationRequested();
            var source = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            SyntaxNode updatedRoot = root;
            var fileChanged = false;

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var explicitSpecifier = method.ExplicitInterfaceSpecifier;
                if (explicitSpecifier is null)
                {
                    continue;
                }

                var typeName = CompileGateRepairSyntaxHelpers.GetTypeSimpleName(explicitSpecifier.Name);
                if (CompileGateRepairSyntaxHelpers.LooksLikeInterface(typeName))
                {
                    continue;
                }

                var fixedMethod = method.WithExplicitInterfaceSpecifier(null);
                updatedRoot = updatedRoot.ReplaceNode(method, fixedMethod);
                fileChanged = true;
            }

            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                var explicitSpecifier = property.ExplicitInterfaceSpecifier;
                if (explicitSpecifier is null)
                {
                    continue;
                }

                var typeName = CompileGateRepairSyntaxHelpers.GetTypeSimpleName(explicitSpecifier.Name);
                if (CompileGateRepairSyntaxHelpers.LooksLikeInterface(typeName))
                {
                    continue;
                }

                var fixedProperty = property.WithExplicitInterfaceSpecifier(null);
                updatedRoot = updatedRoot.ReplaceNode(property, fixedProperty);
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

    public async Task<bool> ApplyInterfaceAsyncFixAsync(string compileWorkspace, CancellationToken ct)
    {
        var changed = false;
        foreach (var filePath in CompileGateRepairDiagnostics.EnumerateCodeFiles(compileWorkspace))
        {
            ct.ThrowIfCancellationRequested();
            var source = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            var interfaceMethods = root.DescendantNodes()
                .OfType<InterfaceDeclarationSyntax>()
                .SelectMany(static declaration => declaration.Members.OfType<MethodDeclarationSyntax>())
                .ToList();
            if (interfaceMethods.Count == 0)
            {
                continue;
            }

            SyntaxNode updatedRoot = root;
            var fileChanged = false;
            foreach (var method in interfaceMethods)
            {
                var strippedModifiers = method.Modifiers.Where(static modifier => !modifier.IsKind(SyntaxKind.AsyncKeyword)).ToArray();
                var fixedMethod = method
                    .WithModifiers(new SyntaxTokenList(strippedModifiers))
                    .WithBody(null)
                    .WithExpressionBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

                if (fixedMethod.ToFullString() == method.ToFullString())
                {
                    continue;
                }

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

