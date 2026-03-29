using Helper.Runtime.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.Runtime.Generation;

internal sealed class CompileGateConstructorRepairSet
{
    public async Task<bool> ApplyMissingConstructorFixAsync(
        string compileWorkspace,
        IReadOnlyList<BuildError> errors,
        CancellationToken ct)
    {
        var constructorsByType = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var error in errors.Where(e => string.Equals(CompileGateRepairDiagnostics.ExtractCode(e), "CS1729", StringComparison.Ordinal)))
        {
            var match = CompileGateRepairPatterns.MissingConstructorRegex.Match(error.Message ?? string.Empty);
            if (!match.Success)
            {
                continue;
            }

            var typeName = CompileGateRepairSyntaxHelpers.NormalizeMissingTypeToken(match.Groups["type"].Value);
            if (!CompileGateRepairSyntaxHelpers.IsValidIdentifier(typeName))
            {
                continue;
            }

            if (!int.TryParse(match.Groups["count"].Value, out var parameterCount) || parameterCount < 0 || parameterCount > 8)
            {
                continue;
            }

            if (!constructorsByType.TryGetValue(typeName, out var counts))
            {
                counts = new HashSet<int>();
                constructorsByType[typeName] = counts;
            }

            counts.Add(parameterCount);
        }

        if (constructorsByType.Count == 0)
        {
            return false;
        }

        var changed = false;
        foreach (var filePath in CompileGateRepairDiagnostics.EnumerateCodeFiles(compileWorkspace))
        {
            ct.ThrowIfCancellationRequested();
            if (constructorsByType.Count == 0)
            {
                break;
            }

            var source = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            SyntaxNode updatedRoot = root;
            var fileChanged = false;
            var targetClasses = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(c => constructorsByType.ContainsKey(c.Identifier.Text))
                .ToList();
            foreach (var classNode in targetClasses)
            {
                if (!constructorsByType.TryGetValue(classNode.Identifier.Text, out var requiredCounts) || requiredCounts.Count == 0)
                {
                    continue;
                }

                var existingCounts = classNode.Members
                    .OfType<ConstructorDeclarationSyntax>()
                    .Where(c => string.Equals(c.Identifier.Text, classNode.Identifier.Text, StringComparison.Ordinal))
                    .Select(static constructor => constructor.ParameterList.Parameters.Count)
                    .ToHashSet();

                var missingCounts = requiredCounts
                    .Where(count => !existingCounts.Contains(count))
                    .OrderBy(static count => count)
                    .ToList();
                if (missingCounts.Count == 0)
                {
                    constructorsByType.Remove(classNode.Identifier.Text);
                    continue;
                }

                var updatedClass = classNode;
                foreach (var parameterCount in missingCounts)
                {
                    updatedClass = updatedClass.AddMembers(
                        CompileGateRepairSyntaxHelpers.BuildConstructorStub(classNode.Identifier.Text, parameterCount));
                }

                updatedRoot = updatedRoot.ReplaceNode(classNode, updatedClass);
                constructorsByType.Remove(classNode.Identifier.Text);
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

