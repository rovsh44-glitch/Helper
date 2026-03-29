using System.Text;
using Helper.Runtime.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.Runtime.Generation;

internal static class CompileGateRepairSyntaxHelpers
{
    internal static string GetMethodBodyText(MethodDeclarationSyntax method)
    {
        if (method.ExpressionBody is not null)
        {
            return $"return {method.ExpressionBody.Expression};";
        }

        return method.Body is null
            ? string.Empty
            : string.Join(Environment.NewLine, method.Body.Statements.Select(static statement => statement.ToFullString()));
    }

    internal static bool DiagnosticTargetsMethod(MethodDeclarationSyntax method, IReadOnlySet<int> targetLines)
    {
        if (targetLines.Count == 0)
        {
            return false;
        }

        var span = method.GetLocation().GetLineSpan();
        var startLine = span.StartLinePosition.Line + 1;
        var endLine = span.EndLinePosition.Line + 1;
        foreach (var line in targetLines)
        {
            if (line >= startLine && line <= endLine)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool MethodContainsAnySymbol(MethodDeclarationSyntax method, IReadOnlyCollection<string> symbols)
    {
        if (symbols.Count == 0)
        {
            return false;
        }

        var body = GetMethodBodyText(method);
        return symbols.Any(symbol => body.Contains(symbol, StringComparison.Ordinal));
    }

    internal static string BuildForcedFallbackBody(IMethodBodySemanticGuard semanticGuard, string signature)
    {
        var forced = semanticGuard.Guard(signature, "return UnknownSymbolThatMustNotExist;");
        return forced.Body;
    }

    internal static HashSet<string> ExtractSuspiciousSymbols(IReadOnlyList<BuildError> errors)
    {
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var error in errors.Where(e => CompileGateRepairPatterns.UnknownSymbolCodes.Contains(CompileGateRepairDiagnostics.ExtractCode(e))))
        {
            foreach (System.Text.RegularExpressions.Match match in CompileGateRepairPatterns.MissingTypeRegex.Matches(error.Message))
            {
                var symbol = match.Groups["type"].Value;
                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    symbols.Add(symbol);
                }
            }
        }

        return symbols;
    }

    internal static BlockSyntax ParseBlock(string body)
    {
        var parsed = SyntaxFactory.ParseStatement($"{{ {body} }}");
        if (parsed is BlockSyntax block)
        {
            return block;
        }

        return SyntaxFactory.Block(SyntaxFactory.ParseStatement("return;"));
    }

    internal static string GetMethodKey(MethodDeclarationSyntax method)
    {
        var parameterTypes = string.Join(
            ",",
            method.ParameterList.Parameters.Select(static parameter => parameter.Type?.ToString() ?? "_"));
        return $"{method.Identifier.Text}({parameterTypes})";
    }

    internal static string GetTypeIdentity(TypeDeclarationSyntax typeDeclaration)
    {
        var typeChain = new Stack<string>();
        for (SyntaxNode? current = typeDeclaration; current is TypeDeclarationSyntax td; current = td.Parent)
        {
            typeChain.Push(td.Identifier.Text);
        }

        var typePath = string.Join(".", typeChain);
        var namespaceNode = typeDeclaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        var namespacePath = namespaceNode?.Name.ToString();
        if (string.IsNullOrWhiteSpace(namespacePath))
        {
            return typePath;
        }

        return $"{namespacePath}.{typePath}";
    }

    internal static ConstructorDeclarationSyntax BuildConstructorStub(string className, int parameterCount)
    {
        var args = string.Join(", ", Enumerable.Range(1, parameterCount).Select(i => $"object arg{i}"));
        var parsed = SyntaxFactory.ParseMemberDeclaration($"public {className}({args}) {{ }}");
        if (parsed is ConstructorDeclarationSyntax ctor)
        {
            return ctor;
        }

        return SyntaxFactory.ConstructorDeclaration(className)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithBody(SyntaxFactory.Block());
    }

    internal static string GetTypeSimpleName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            _ => type.ToString().Split('.').LastOrDefault() ?? type.ToString()
        };
    }

    internal static MethodDeclarationSyntax CreateInterfaceStub(MethodDeclarationSyntax source)
    {
        var modifiers = new SyntaxTokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
        var body = BuildStubBody(source.ReturnType);

        return SyntaxFactory.MethodDeclaration(source.ReturnType, source.Identifier)
            .WithTypeParameterList(source.TypeParameterList)
            .WithParameterList(source.ParameterList)
            .WithConstraintClauses(source.ConstraintClauses)
            .WithModifiers(modifiers)
            .WithBody(body)
            .WithExpressionBody(null)
            .WithSemicolonToken(default);
    }

    internal static BlockSyntax BuildStubBody(TypeSyntax returnType)
    {
        if (returnType is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
        {
            return SyntaxFactory.Block();
        }

        return SyntaxFactory.Block(SyntaxFactory.ParseStatement("return default!;"));
    }

    internal static HashSet<string> ExtractNonNullableMembers(IReadOnlyList<BuildError> errors)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var error in errors.Where(e => string.Equals(CompileGateRepairDiagnostics.ExtractCode(e), "CS8618", StringComparison.Ordinal)))
        {
            foreach (System.Text.RegularExpressions.Match match in CompileGateRepairPatterns.NonNullableMemberRegex.Matches(error.Message))
            {
                var value = match.Groups["name"].Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    members.Add(value);
                }
            }
        }

        return members;
    }

    internal static bool IsReferenceTypeCandidate(TypeSyntax type)
    {
        if (type is NullableTypeSyntax)
        {
            return false;
        }

        if (type is PredefinedTypeSyntax predefined)
        {
            return predefined.Keyword.IsKind(SyntaxKind.StringKeyword) ||
                   predefined.Keyword.IsKind(SyntaxKind.ObjectKeyword);
        }

        if (type.ToString().EndsWith("?", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    internal static ExpressionSyntax BuildNonNullableInitializer(TypeSyntax type)
    {
        if (type is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.StringKeyword))
        {
            return SyntaxFactory.ParseExpression("string.Empty");
        }

        return SyntaxFactory.ParseExpression("default!");
    }

    internal static IReadOnlyList<string> ExtractMissingSymbols(BuildError error, string code)
    {
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        if (string.Equals(code, "CS0103", StringComparison.Ordinal))
        {
            foreach (System.Text.RegularExpressions.Match match in CompileGateRepairPatterns.MissingTypeRegex.Matches(error.Message))
            {
                var value = match.Groups["type"].Value;
                if (IsValidIdentifier(value))
                {
                    symbols.Add(value);
                }
            }
        }
        else if (string.Equals(code, "CS1061", StringComparison.Ordinal))
        {
            foreach (System.Text.RegularExpressions.Match match in CompileGateRepairPatterns.MissingMemberRegex.Matches(error.Message))
            {
                var value = match.Groups["name"].Value;
                if (IsValidIdentifier(value))
                {
                    symbols.Add(value);
                }
            }
        }

        return symbols.ToList();
    }

    internal static bool TryResolveAssignedMethodName(ExpressionSyntax left, out string methodName)
    {
        methodName = string.Empty;
        if (left is IdentifierNameSyntax identifier)
        {
            methodName = identifier.Identifier.Text;
            return !string.IsNullOrWhiteSpace(methodName);
        }

        if (left is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name.Identifier.Text;
            return !string.IsNullOrWhiteSpace(methodName);
        }

        return false;
    }

    internal static string NormalizeMissingTypeToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return string.Empty;
        }

        var token = rawToken.Trim();
        var genericTickIndex = token.IndexOf('`');
        if (genericTickIndex > 0)
        {
            token = token[..genericTickIndex];
        }

        var genericBracketIndex = token.IndexOf('<');
        if (genericBracketIndex > 0)
        {
            token = token[..genericBracketIndex];
        }

        if (token.Contains('.'))
        {
            token = token.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? token;
        }

        return token;
    }

    internal static bool NeedsTaskReturnTypeQualification(TypeSyntax returnType)
    {
        var compact = RemoveWhitespace(returnType.ToString());
        if (compact.StartsWith("global::System.Threading.Tasks.Task", StringComparison.Ordinal) ||
            compact.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(compact, "Task", StringComparison.Ordinal) ||
               compact.StartsWith("Task<", StringComparison.Ordinal);
    }

    internal static TypeSyntax QualifyTaskReturnType(TypeSyntax returnType)
    {
        var compact = RemoveWhitespace(returnType.ToString());
        if (compact.StartsWith("global::System.Threading.Tasks.Task", StringComparison.Ordinal) ||
            compact.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal))
        {
            return returnType;
        }

        if (string.Equals(compact, "Task", StringComparison.Ordinal))
        {
            return SyntaxFactory.ParseTypeName("global::System.Threading.Tasks.Task").WithTriviaFrom(returnType);
        }

        if (compact.StartsWith("Task<", StringComparison.Ordinal))
        {
            return SyntaxFactory.ParseTypeName($"global::System.Threading.Tasks.{compact}").WithTriviaFrom(returnType);
        }

        return returnType;
    }

    internal static string RemoveWhitespace(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    internal static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!char.IsLetter(value[0]) && value[0] != '_')
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_')
            {
                return false;
            }
        }

        return true;
    }

    internal static bool LooksLikeInterface(string typeName)
    {
        return !string.IsNullOrWhiteSpace(typeName) &&
               typeName.Length > 1 &&
               typeName[0] == 'I' &&
               char.IsUpper(typeName[1]);
    }

    internal static string BuildTypeStubDeclaration(string typeName)
    {
        if (LooksLikeInterface(typeName))
        {
            return $"public interface {typeName} {{ }}";
        }

        if (string.Equals(typeName, "BaseViewModel", StringComparison.Ordinal))
        {
            return
                "public class BaseViewModel" + Environment.NewLine +
                "{" + Environment.NewLine +
                "    public virtual void OnNavigatedTo(object parameter) { }" + Environment.NewLine +
                "    public virtual void OnNavigatedFrom() { }" + Environment.NewLine +
                "}";
        }

        if (string.Equals(typeName, "RelayCommand", StringComparison.Ordinal))
        {
            return
                "public class RelayCommand" + Environment.NewLine +
                "{" + Environment.NewLine +
                "    public RelayCommand() { }" + Environment.NewLine +
                "    public RelayCommand(global::System.Action execute) { }" + Environment.NewLine +
                "    public RelayCommand(global::System.Action<object> execute) { }" + Environment.NewLine +
                "    public RelayCommand(global::System.Action execute, global::System.Func<bool> canExecute) { }" + Environment.NewLine +
                "    public RelayCommand(global::System.Action<object> execute, global::System.Predicate<object> canExecute) { }" + Environment.NewLine +
                "    public void Execute(object parameter) { }" + Environment.NewLine +
                "}";
        }

        return $"public class {typeName} {{ }}";
    }
}

