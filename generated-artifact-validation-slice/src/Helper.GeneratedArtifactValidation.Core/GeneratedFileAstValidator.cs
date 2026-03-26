using Helper.GeneratedArtifactValidation.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.GeneratedArtifactValidation.Core;

public sealed class GeneratedFileAstValidator
{
    public GeneratedFileValidationResult ValidateFile(
        string relativePath,
        string code,
        FileRole role,
        string expectedNamespace,
        string expectedTypeName)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return new GeneratedFileValidationResult(true, code, errors, warnings);
        }

        var tree = CSharpSyntaxTree.ParseText(code);
        var parseErrors = tree
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();

        if (parseErrors.Count > 0)
        {
            return new GeneratedFileValidationResult(false, code, parseErrors, warnings);
        }

        var root = tree.GetCompilationUnitRoot();
        var namespaceNodes = root.DescendantNodes()
            .Where(x => x is NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax)
            .ToList();

        if (namespaceNodes.Count != 1)
        {
            errors.Add("Generated file must contain exactly one namespace declaration.");
            return new GeneratedFileValidationResult(false, code, errors, warnings);
        }

        var namespaceName = namespaceNodes[0] switch
        {
            NamespaceDeclarationSyntax ns => ns.Name.ToString(),
            FileScopedNamespaceDeclarationSyntax fs => fs.Name.ToString(),
            _ => string.Empty
        };

        if (!string.Equals(namespaceName, expectedNamespace, StringComparison.Ordinal))
        {
            warnings.Add($"Namespace '{namespaceName}' differs from expected '{expectedNamespace}'.");
        }

        var typeDeclarations = root.DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Where(x => x is ClassDeclarationSyntax or InterfaceDeclarationSyntax or RecordDeclarationSyntax)
            .ToList();

        if (typeDeclarations.Count != 1)
        {
            errors.Add("Generated file must contain exactly one top-level class/interface/record.");
            return new GeneratedFileValidationResult(false, code, errors, warnings);
        }

        var typeName = typeDeclarations[0].Identifier.Text;
        if (!string.Equals(typeName, expectedTypeName, StringComparison.Ordinal))
        {
            warnings.Add($"Type name '{typeName}' differs from expected '{expectedTypeName}'.");
        }

        if (role == FileRole.Interface && typeDeclarations[0] is not InterfaceDeclarationSyntax)
        {
            warnings.Add("Interface role produced non-interface declaration.");
        }

        if (typeDeclarations[0] is TypeDeclarationSyntax typed)
        {
            var duplicateMethods = typed.Members
                .OfType<MethodDeclarationSyntax>()
                .Select(x => $"{x.Identifier.Text}({string.Join(",", x.ParameterList.Parameters.Select(p => p.Type?.ToString() ?? "_"))})")
                .GroupBy(x => x, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateMethods.Count > 0)
            {
                errors.Add($"Duplicate method signatures detected: {string.Join(", ", duplicateMethods)}.");
                return new GeneratedFileValidationResult(false, code, errors, warnings);
            }
        }

        return new GeneratedFileValidationResult(true, code, errors, warnings);
    }
}

