using Helper.Runtime.Core;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.Runtime.Generation;

internal sealed class CompileGateXamlBindingRepairSet
{
    public async Task<bool> ApplyXamlCodeBehindBindingFixAsync(
        string compileWorkspace,
        IReadOnlyList<BuildError> errors,
        CancellationToken ct)
    {
        var missingByFile = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var error in errors)
        {
            var code = CompileGateRepairDiagnostics.ExtractCode(error);
            if (!string.Equals(code, "CS0103", StringComparison.Ordinal) &&
                !string.Equals(code, "CS1061", StringComparison.Ordinal))
            {
                continue;
            }

            var resolvedFile = CompileGateRepairDiagnostics.ResolveFilePath(compileWorkspace, error.File);
            if (string.IsNullOrWhiteSpace(resolvedFile) ||
                !resolvedFile.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var symbols = CompileGateRepairSyntaxHelpers.ExtractMissingSymbols(error, code);
            if (symbols.Count == 0)
            {
                continue;
            }

            if (!missingByFile.TryGetValue(resolvedFile, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                missingByFile[resolvedFile] = set;
            }

            foreach (var symbol in symbols)
            {
                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    set.Add(symbol);
                }
            }
        }

        if (missingByFile.Count == 0)
        {
            return false;
        }

        var changed = false;
        foreach (var entry in missingByFile)
        {
            ct.ThrowIfCancellationRequested();
            var filePath = entry.Key;
            if (!File.Exists(filePath))
            {
                continue;
            }

            var source = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax;
            if (root is null)
            {
                continue;
            }

            var classDeclaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDeclaration is null)
            {
                continue;
            }

            var namespaceName = root.DescendantNodes()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault()?
                .Name
                .ToString();
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                continue;
            }

            var className = classDeclaration.Identifier.Text;
            if (string.IsNullOrWhiteSpace(className))
            {
                continue;
            }

            var existingMembers = classDeclaration.Members
                .Select(member => member switch
                {
                    FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
                    PropertyDeclarationSyntax property => property.Identifier.Text,
                    MethodDeclarationSyntax method => method.Identifier.Text,
                    _ => null
                })
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.Ordinal);

            var stubPath = Path.Combine(Path.GetDirectoryName(filePath) ?? compileWorkspace, $"{className}.bindings.g.cs");
            var existingDynamicFields = new HashSet<string>(StringComparer.Ordinal);
            if (File.Exists(stubPath))
            {
                var existingStub = await File.ReadAllTextAsync(stubPath, ct).ConfigureAwait(false);
                foreach (System.Text.RegularExpressions.Match match in CompileGateRepairPatterns.ExistingDynamicFieldRegex.Matches(existingStub))
                {
                    var name = match.Groups["name"].Value;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        existingDynamicFields.Add(name);
                    }
                }
            }

            var symbolsToWrite = entry.Value
                .Where(CompileGateRepairSyntaxHelpers.IsValidIdentifier)
                .Where(symbol => !existingMembers.Contains(symbol))
                .Concat(existingDynamicFields)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static symbol => symbol, StringComparer.Ordinal)
                .ToList();
            if (symbolsToWrite.Count == 0)
            {
                continue;
            }

            var fieldsBlock = string.Join(
                Environment.NewLine,
                symbolsToWrite.Select(symbol => $"        private dynamic {symbol} = default!;"));
            var stub = $@"namespace {namespaceName}
{{
    public partial class {className}
    {{
{fieldsBlock}
    }}
}}";
            await File.WriteAllTextAsync(stubPath, stub, ct).ConfigureAwait(false);
            changed = true;
        }

        return changed;
    }
}

