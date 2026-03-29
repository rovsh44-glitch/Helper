using Helper.Runtime.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.Runtime.Generation;

internal sealed class CompileGateMemberContractRepairSet
{
    public async Task<bool> ApplyMethodGroupAssignmentFixAsync(string compileWorkspace, CancellationToken ct)
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

            var fileChanged = false;
            SyntaxNode updatedRoot = root;
            var assignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().ToList();
            foreach (var assignment in assignments)
            {
                if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    continue;
                }

                if (assignment.Parent is not ExpressionStatementSyntax expressionStatement)
                {
                    continue;
                }

                if (!CompileGateRepairSyntaxHelpers.TryResolveAssignedMethodName(assignment.Left, out var methodName))
                {
                    continue;
                }

                var ownerClass = assignment.FirstAncestorOrSelf<ClassDeclarationSyntax>();
                if (ownerClass is null)
                {
                    continue;
                }

                var methodNames = ownerClass.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Select(static method => method.Identifier.Text)
                    .ToHashSet(StringComparer.Ordinal);
                if (!methodNames.Contains(methodName))
                {
                    continue;
                }

                var invocation = SyntaxFactory.InvocationExpression(assignment.Left.WithoutTrivia());
                var replacement = SyntaxFactory.ExpressionStatement(invocation).WithTriviaFrom(expressionStatement);
                updatedRoot = updatedRoot.ReplaceNode(expressionStatement, replacement);
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

    public async Task<bool> ApplyNonNullableInitializationFixAsync(
        string compileWorkspace,
        IReadOnlyList<BuildError> errors,
        CancellationToken ct)
    {
        var memberNames = CompileGateRepairSyntaxHelpers.ExtractNonNullableMembers(errors);
        if (memberNames.Count == 0)
        {
            return false;
        }

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
            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                if (property.Initializer is not null || property.ExpressionBody is not null)
                {
                    continue;
                }

                if (!memberNames.Contains(property.Identifier.Text))
                {
                    continue;
                }

                if (!CompileGateRepairSyntaxHelpers.IsReferenceTypeCandidate(property.Type))
                {
                    continue;
                }

                var initializer = CompileGateRepairSyntaxHelpers.BuildNonNullableInitializer(property.Type);
                var updatedProperty = property
                    .WithInitializer(SyntaxFactory.EqualsValueClause(initializer))
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

                updatedRoot = updatedRoot.ReplaceNode(property, updatedProperty);
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

    public async Task<bool> ApplyMissingInterfaceMembersFixAsync(string compileWorkspace, CancellationToken ct)
    {
        var fileTrees = new List<(string Path, CompilationUnitSyntax Root)>();
        foreach (var filePath in CompileGateRepairDiagnostics.EnumerateCodeFiles(compileWorkspace))
        {
            ct.ThrowIfCancellationRequested();
            var source = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
            if (root is not null)
            {
                fileTrees.Add((filePath, root));
            }
        }

        if (fileTrees.Count == 0)
        {
            return false;
        }

        var interfaceMethods = fileTrees
            .SelectMany(static entry => entry.Root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            .GroupBy(static declaration => declaration.Identifier.Text, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.SelectMany(static declaration => declaration.Members.OfType<MethodDeclarationSyntax>()).ToList(),
                StringComparer.Ordinal);

        var changed = false;
        foreach (var (filePath, root) in fileTrees)
        {
            SyntaxNode updatedRoot = root;
            var fileChanged = false;

            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var interfaceNames = classDecl.BaseList?.Types
                    .Select(static type => CompileGateRepairSyntaxHelpers.GetTypeSimpleName(type.Type))
                    .Where(interfaceMethods.ContainsKey)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (interfaceNames is null || interfaceNames.Count == 0)
                {
                    continue;
                }

                var existing = classDecl.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Select(CompileGateRepairSyntaxHelpers.GetMethodKey)
                    .ToHashSet(StringComparer.Ordinal);

                var missing = new List<MethodDeclarationSyntax>();
                foreach (var interfaceName in interfaceNames)
                {
                    foreach (var method in interfaceMethods[interfaceName])
                    {
                        var key = CompileGateRepairSyntaxHelpers.GetMethodKey(method);
                        if (existing.Contains(key))
                        {
                            continue;
                        }

                        missing.Add(CompileGateRepairSyntaxHelpers.CreateInterfaceStub(method));
                        existing.Add(key);
                    }
                }

                if (missing.Count == 0)
                {
                    continue;
                }

                var updatedClass = classDecl.AddMembers(missing.ToArray());
                updatedRoot = updatedRoot.ReplaceNode(classDecl, updatedClass);
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

    public async Task<bool> ApplyDuplicateSignatureFixAsync(string compileWorkspace, CancellationToken ct)
    {
        var rootsByFile = new Dictionary<string, CompilationUnitSyntax>(StringComparer.OrdinalIgnoreCase);
        var methodsByIdentity = new Dictionary<string, List<(string FilePath, MethodDeclarationSyntax Method)>>(StringComparer.Ordinal);

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

            rootsByFile[filePath] = root;

            foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeIdentity = CompileGateRepairSyntaxHelpers.GetTypeIdentity(typeDeclaration);
                foreach (var method in typeDeclaration.Members.OfType<MethodDeclarationSyntax>())
                {
                    var identityKey = $"{typeIdentity}::{CompileGateRepairSyntaxHelpers.GetMethodKey(method)}";
                    if (!methodsByIdentity.TryGetValue(identityKey, out var list))
                    {
                        list = new List<(string FilePath, MethodDeclarationSyntax Method)>();
                        methodsByIdentity[identityKey] = list;
                    }

                    list.Add((filePath, method));
                }
            }
        }

        if (rootsByFile.Count == 0)
        {
            return false;
        }

        var removalsByFile = new Dictionary<string, List<MethodDeclarationSyntax>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in methodsByIdentity.Values.Where(static group => group.Count > 1))
        {
            var ordered = group
                .OrderBy(static item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Method.SpanStart)
                .ToList();

            foreach (var duplicate in ordered.Skip(1))
            {
                if (!removalsByFile.TryGetValue(duplicate.FilePath, out var list))
                {
                    list = new List<MethodDeclarationSyntax>();
                    removalsByFile[duplicate.FilePath] = list;
                }

                list.Add(duplicate.Method);
            }
        }

        var changed = false;
        foreach (var fileEntry in removalsByFile)
        {
            if (!rootsByFile.TryGetValue(fileEntry.Key, out var root))
            {
                continue;
            }

            var nodesToRemove = fileEntry.Value.Distinct().ToList();
            if (nodesToRemove.Count == 0)
            {
                continue;
            }

            var updatedRoot = root.RemoveNodes(nodesToRemove, SyntaxRemoveOptions.KeepNoTrivia) as CompilationUnitSyntax;
            if (updatedRoot is null)
            {
                continue;
            }

            await File.WriteAllTextAsync(fileEntry.Key, updatedRoot.NormalizeWhitespace().ToFullString(), ct).ConfigureAwait(false);
            changed = true;
        }

        return changed;
    }
}

