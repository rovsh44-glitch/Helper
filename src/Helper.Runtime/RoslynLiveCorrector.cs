using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Helper.Runtime.Infrastructure
{
    public class RoslynLiveCorrector
    {
        public ValidationResult ValidateSyntax(string code)
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var diagnostics = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
            
            if (diagnostics.Any())
            {
                var errors = diagnostics.Select(d => $"Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}").ToList();
                return new ValidationResult(false, errors);
            }
            
            return new ValidationResult(true, new List<string>());
        }

        public string SuggestFix(string code, List<string> errors)
        {
            // This is where we feed errors back to the AI in a loop
            return $"Code has errors: {string.Join(", ", errors)}";
        }
    }

    public record ValidationResult(bool Valid, List<string> Errors);
}

