using System.Text.RegularExpressions;

namespace Helper.Runtime.Generation;

internal sealed class TemplatePromotionScaffoldService
{
    private static readonly Regex DeclaredTypeRegex = new(@"\b(?:class|interface|record|struct|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex NamespaceRegex = new(@"\bnamespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Compiled);

    public async Task EnsureCertificationScaffoldAsync(string rootPath, CancellationToken ct)
    {
        var csFiles = Directory.EnumerateFiles(rootPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifactPath(path))
            .ToList();
        var hasXaml = Directory.EnumerateFiles(rootPath, "*.xaml", SearchOption.AllDirectories)
            .Any(path => !IsBuildArtifactPath(path));
        var hasMainWindowXaml = Directory.EnumerateFiles(rootPath, "MainWindow.xaml", SearchOption.AllDirectories)
            .Any(path => !IsBuildArtifactPath(path));

        var primaryNamespace = "TemplatePromotion";
        var existingTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in csFiles)
        {
            ct.ThrowIfCancellationRequested();
            var source = await File.ReadAllTextAsync(file, ct);
            if (primaryNamespace == "TemplatePromotion")
            {
                var nsMatch = NamespaceRegex.Match(source);
                if (nsMatch.Success)
                {
                    var candidate = nsMatch.Groups["name"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        primaryNamespace = candidate;
                    }
                }
            }

            foreach (Match match in DeclaredTypeRegex.Matches(source))
            {
                var name = match.Groups["name"].Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    existingTypes.Add(name);
                }
            }
        }

        var requestedStubs = new (string Name, string Declaration)[]
        {
            ("IModule", "public interface IModule { }"),
            ("IMainWindowModule", "public interface IMainWindowModule { }"),
            ("IWindowService", "public interface IWindowService { }"),
            ("IWindow", "public interface IWindow { }"),
            ("BaseViewModel", "public class BaseViewModel { }"),
            ("Window", "public class Window { }"),
            ("LogLevel", "public enum LogLevel { Trace, Debug, Information, Warning, Error, Critical, None }")
        };

        var missingDeclarations = requestedStubs
            .Where(x => !existingTypes.Contains(x.Name))
            .Select(x => x.Declaration)
            .ToList();
        if (missingDeclarations.Count > 0)
        {
            var stubsPath = Path.Combine(rootPath, "TemplatePromotionStubs.g.cs");
            var content = string.Join(Environment.NewLine, missingDeclarations) + Environment.NewLine;
            await File.WriteAllTextAsync(stubsPath, content, ct);
        }

        await AlignAppCodeBehindAsync(rootPath, ct);

        if (hasXaml && !hasMainWindowXaml)
        {
            var xamlPath = Path.Combine(rootPath, "MainWindow.xaml");
            var xamlContent =
                "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" + Environment.NewLine +
                "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"" + Environment.NewLine +
                $"        x:Class=\"{primaryNamespace}.MainWindow\"" + Environment.NewLine +
                "        Title=\"MainWindow\" Height=\"450\" Width=\"800\">" + Environment.NewLine +
                "    <Grid />" + Environment.NewLine +
                "</Window>" + Environment.NewLine;
            await File.WriteAllTextAsync(xamlPath, xamlContent, ct);

            var xamlCodeBehindPath = Path.Combine(rootPath, "MainWindow.xaml.cs");
            if (!File.Exists(xamlCodeBehindPath))
            {
                var codeBehind =
                    "namespace " + primaryNamespace + Environment.NewLine +
                    "{" + Environment.NewLine +
                    "    public partial class MainWindow" + Environment.NewLine +
                    "    {" + Environment.NewLine +
                    "        public MainWindow()" + Environment.NewLine +
                    "        {" + Environment.NewLine +
                    "        }" + Environment.NewLine +
                    "    }" + Environment.NewLine +
                    "}" + Environment.NewLine;
                await File.WriteAllTextAsync(xamlCodeBehindPath, codeBehind, ct);
            }
        }
    }

    public async Task EnsureTemplateProjectFileAsync(string rootPath, string templateId, CancellationToken ct)
    {
        var hasProjectFile = Directory.EnumerateFiles(rootPath, "*.csproj", SearchOption.AllDirectories)
            .Any(path => !IsBuildArtifactPath(path));
        if (hasProjectFile)
        {
            return;
        }

        var hasXaml = Directory.EnumerateFiles(rootPath, "*.xaml", SearchOption.AllDirectories).Any();
        var hasProgram = Directory.EnumerateFiles(rootPath, "Program.cs", SearchOption.AllDirectories).Any();
        var assemblyName = BuildProjectName(templateId);
        var projectFilePath = Path.Combine(rootPath, $"{assemblyName}.csproj");

        string projectFileContent;
        if (hasXaml)
        {
            projectFileContent =
                "<Project Sdk=\"Microsoft.NET.Sdk\">" + Environment.NewLine +
                Environment.NewLine +
                "  <PropertyGroup>" + Environment.NewLine +
                "    <OutputType>WinExe</OutputType>" + Environment.NewLine +
                "    <TargetFramework>net8.0-windows</TargetFramework>" + Environment.NewLine +
                "    <UseWPF>true</UseWPF>" + Environment.NewLine +
                "    <ImplicitUsings>enable</ImplicitUsings>" + Environment.NewLine +
                "    <Nullable>enable</Nullable>" + Environment.NewLine +
                "  </PropertyGroup>" + Environment.NewLine +
                Environment.NewLine +
                "</Project>" + Environment.NewLine;
        }
        else
        {
            var outputType = hasProgram ? "Exe" : "Library";
            projectFileContent =
                "<Project Sdk=\"Microsoft.NET.Sdk\">" + Environment.NewLine +
                Environment.NewLine +
                "  <PropertyGroup>" + Environment.NewLine +
                $"    <OutputType>{outputType}</OutputType>" + Environment.NewLine +
                "    <TargetFramework>net8.0</TargetFramework>" + Environment.NewLine +
                "    <ImplicitUsings>enable</ImplicitUsings>" + Environment.NewLine +
                "    <Nullable>enable</Nullable>" + Environment.NewLine +
                "  </PropertyGroup>" + Environment.NewLine +
                Environment.NewLine +
                "</Project>" + Environment.NewLine;
        }

        await File.WriteAllTextAsync(projectFilePath, projectFileContent, ct);
    }

    public static bool IsBuildArtifactPath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private async Task AlignAppCodeBehindAsync(string rootPath, CancellationToken ct)
    {
        var appXamlPath = Directory.EnumerateFiles(rootPath, "App.xaml", SearchOption.AllDirectories)
            .FirstOrDefault(path => !IsBuildArtifactPath(path));
        var appCodeBehindPath = Directory.EnumerateFiles(rootPath, "App.xaml.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => !IsBuildArtifactPath(path));
        if (string.IsNullOrWhiteSpace(appCodeBehindPath) || !File.Exists(appCodeBehindPath))
        {
            return;
        }

        var appCodeBehind = await File.ReadAllTextAsync(appCodeBehindPath, ct);
        var patchedCodeBehind = appCodeBehind.Replace("override void OnStartup(", "void OnStartup(", StringComparison.Ordinal);
        if (!string.Equals(appCodeBehind, patchedCodeBehind, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(appCodeBehindPath, patchedCodeBehind, ct);
        }

        if (string.IsNullOrWhiteSpace(appXamlPath) || !File.Exists(appXamlPath))
        {
            return;
        }

        var nsMatch = NamespaceRegex.Match(patchedCodeBehind);
        if (!nsMatch.Success)
        {
            return;
        }

        var appNamespace = nsMatch.Groups["name"].Value.Trim();
        if (string.IsNullOrWhiteSpace(appNamespace))
        {
            return;
        }

        var appXaml = await File.ReadAllTextAsync(appXamlPath, ct);
        var patchedXaml = Regex.Replace(
            appXaml,
            "x:Class=\"[^\"]+\\.App\"",
            $"x:Class=\"{appNamespace}.App\"",
            RegexOptions.CultureInvariant);
        if (!string.Equals(appXaml, patchedXaml, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(appXamlPath, patchedXaml, ct);
        }
    }

    private static string BuildProjectName(string templateId)
    {
        var alnum = templateId.Where(char.IsLetterOrDigit).ToArray();
        if (alnum.Length == 0)
        {
            return "TemplateProject";
        }

        var ascii = new string(alnum.Where(c => c <= 127).ToArray());
        if (string.IsNullOrWhiteSpace(ascii))
        {
            return "TemplateProject";
        }

        if (!char.IsLetter(ascii[0]) && ascii[0] != '_')
        {
            ascii = "_" + ascii;
        }

        return ascii;
    }
}

