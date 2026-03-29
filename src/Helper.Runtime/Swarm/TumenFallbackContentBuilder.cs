using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Swarm.Core;

namespace Helper.Runtime.Swarm;

internal static class TumenFallbackContentBuilder
{
    public static string BuildNonCodeFileContent(
        string rootNamespace,
        string safeRelativePath,
        FileRole role,
        IReadOnlySet<string> knownSanitizedPaths,
        Func<string, string> resolveClassName)
    {
        if (safeRelativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            return BuildXamlFallback(rootNamespace, safeRelativePath, role, knownSanitizedPaths, resolveClassName);
        }

        if (safeRelativePath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return "Set-StrictMode -Version Latest";
        }

        if (safeRelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return "{}";
        }

        if (safeRelativePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            safeRelativePath.EndsWith(".cjs", StringComparison.OrdinalIgnoreCase) ||
            safeRelativePath.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
        {
            return "\"use strict\";" + Environment.NewLine;
        }

        if (safeRelativePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
            return "export {};" + Environment.NewLine;
        }

        if (safeRelativePath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
        {
            return """
export default function App() {
    return null;
}
""";
        }

        if (safeRelativePath.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
        {
            return """
def main() -> int:
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
""";
        }

        if (safeRelativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return """
<!doctype html>
<html>
<head>
    <meta charset="utf-8" />
    <title>Generated</title>
</head>
<body>
</body>
</html>
""";
        }

        if (safeRelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return $"# {Path.GetFileNameWithoutExtension(safeRelativePath)}{Environment.NewLine}";
        }

        if (safeRelativePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return $"Generated file: {Path.GetFileName(safeRelativePath)}";
        }

        if (safeRelativePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
            safeRelativePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
        {
            return "version: \"1\"" + Environment.NewLine;
        }

        if (safeRelativePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return "<root />" + Environment.NewLine;
        }

        return $"auto-generated {role} file: {safeRelativePath}" + Environment.NewLine;
    }

    public static string BuildFallbackFile(string rootNamespace, string className, FileRole role)
    {
        return GenerationFallbackPolicy.BuildFileValidationFallback(
            rootNamespace,
            className,
            role == FileRole.Interface);
    }

    private static string BuildXamlFallback(
        string rootNamespace,
        string safeRelativePath,
        FileRole role,
        IReadOnlySet<string> knownSanitizedPaths,
        Func<string, string> resolveClassName)
    {
        var hasCodeBehind = knownSanitizedPaths.Contains($"{safeRelativePath}.cs");
        var fileName = Path.GetFileName(safeRelativePath);
        if (string.Equals(fileName, "App.xaml", StringComparison.OrdinalIgnoreCase))
        {
            var appXClass = hasCodeBehind
                ? $"\n             x:Class=\"{rootNamespace}.App\""
                : string.Empty;
            return $"""
<Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"{appXClass}>
    <Application.Resources>
    </Application.Resources>
</Application>
""";
        }

        var isResource = role == FileRole.Resource ||
                         safeRelativePath.Contains("Resource", StringComparison.OrdinalIgnoreCase);
        if (isResource)
        {
            return """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
</ResourceDictionary>
""";
        }

        var className = resolveClassName(safeRelativePath);
        var xClassAttribute = hasCodeBehind
            ? $"\n        x:Class=\"{rootNamespace}.{className}\""
            : string.Empty;

        return $"""
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"{xClassAttribute}
        Title="{className}" Height="450" Width="800">
    <Grid />
</Window>
""";
    }
}

