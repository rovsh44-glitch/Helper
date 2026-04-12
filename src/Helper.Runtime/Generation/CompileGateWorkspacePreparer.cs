using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed class CompileGateWorkspacePreparer
{
    internal const string GeneratedProjectFileName = "GeneratedCompileGate.csproj";

    private static readonly Regex NamespaceRegex = new(@"namespace\s+([A-Za-z0-9_.]+)", RegexOptions.Compiled);
    private static readonly Regex PartialClassRegex = new(@"partial\s+class\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex XamlEventRegex = new(@"\b(?:Click|Loaded|SelectionChanged|TextChanged|Checked|Unchecked|MouseDown|MouseUp|Drop|DragOver|KeyDown|KeyUp|Closed|Opened|Navigated)=""(?<handler>[A-Za-z_][A-Za-z0-9_]*)""", RegexOptions.Compiled);

    public async Task<BuildError?> PrepareAsync(string rawProjectRoot, string compileWorkspace, CancellationToken ct)
    {
        CopyCompilationInputs(rawProjectRoot, compileWorkspace);
        var dependencyRefs = CollectDependencyReferences(rawProjectRoot);
        var profile = InferCompileProjectProfile(rawProjectRoot);
        var declaredProjectTypeValidation = ValidateDeclaredProjectType(rawProjectRoot, profile);
        if (declaredProjectTypeValidation is not null)
        {
            return declaredProjectTypeValidation;
        }

        await EnsureCompileProjectAsync(compileWorkspace, dependencyRefs, profile, ct);
        await CreateXamlCodeBehindStubsAsync(compileWorkspace, ct);
        return null;
    }

    public DependencyReferences CollectDependencyReferences(string sourceRoot)
    {
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var csproj in Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories))
        {
            if (csproj.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                csproj.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                csproj.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var doc = XDocument.Load(csproj);
                foreach (var package in doc.Descendants().Where(x => x.Name.LocalName == "PackageReference"))
                {
                    var include = package.Attribute("Include")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(include))
                    {
                        continue;
                    }

                    var version = package.Attribute("Version")?.Value?.Trim() ??
                                  package.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value?.Trim() ??
                                  string.Empty;
                    if (!packages.TryGetValue(include, out var currentVersion) ||
                        string.IsNullOrWhiteSpace(currentVersion))
                    {
                        packages[include] = version;
                    }
                }

                foreach (var framework in doc.Descendants().Where(x => x.Name.LocalName == "FrameworkReference"))
                {
                    var include = framework.Attribute("Include")?.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(include))
                    {
                        frameworks.Add(include);
                    }
                }
            }
            catch
            {
                // ignore malformed project files in compile gate dependency extraction
            }
        }

        return new DependencyReferences(packages, frameworks);
    }

    public CompileProjectProfile InferCompileProjectProfile(string sourceRoot)
    {
        var csproj = Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .FirstOrDefault();

        var hasXaml = Directory.EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Any(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        var hasWpfCodeIndicators = DetectWpfCodeIndicators(sourceRoot);
        var hasAppXaml = Directory.EnumerateFiles(sourceRoot, "App.xaml", SearchOption.AllDirectories)
            .Any(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        string? targetFramework = null;
        bool useWpf = hasXaml || hasWpfCodeIndicators;
        bool useWindowsForms = false;
        string? outputType = null;
        bool enableWindowsTargeting = false;
        var sdk = "Microsoft.NET.Sdk";

        if (!string.IsNullOrWhiteSpace(csproj))
        {
            try
            {
                var doc = XDocument.Load(csproj);
                sdk = doc.Root?.Attribute("Sdk")?.Value?.Trim() ?? sdk;
                targetFramework = doc.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "TargetFramework")
                    ?.Value
                    ?.Trim();
                if (string.IsNullOrWhiteSpace(targetFramework))
                {
                    var tfms = doc.Descendants()
                        .FirstOrDefault(x => x.Name.LocalName == "TargetFrameworks")
                        ?.Value
                        ?.Trim();
                    if (!string.IsNullOrWhiteSpace(tfms))
                    {
                        targetFramework = tfms.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                    }
                }

                outputType = doc.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "OutputType")
                    ?.Value
                    ?.Trim();

                var useWpfRaw = doc.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "UseWPF")
                    ?.Value
                    ?.Trim();
                if (bool.TryParse(useWpfRaw, out var parsedUseWpf))
                {
                    useWpf = parsedUseWpf || hasXaml || hasWpfCodeIndicators;
                }
                else
                {
                    useWpf = hasXaml || hasWpfCodeIndicators;
                }

                var useWindowsFormsRaw = doc.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "UseWindowsForms")
                    ?.Value
                    ?.Trim();
                if (bool.TryParse(useWindowsFormsRaw, out var parsedUseWindowsForms))
                {
                    useWindowsForms = parsedUseWindowsForms;
                }

                var ewtRaw = doc.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "EnableWindowsTargeting")
                    ?.Value
                    ?.Trim();
                if (bool.TryParse(ewtRaw, out var parsedEwt))
                {
                    enableWindowsTargeting = parsedEwt;
                }
            }
            catch
            {
                // fall back to heuristic profile
            }
        }

        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            targetFramework = useWpf ? "net8.0-windows" : "net8.0";
        }

        if (useWpf && !targetFramework.Contains("-windows", StringComparison.OrdinalIgnoreCase))
        {
            targetFramework += "-windows";
        }

        if (string.IsNullOrWhiteSpace(outputType))
        {
            if (useWpf)
            {
                outputType = hasAppXaml ? "WinExe" : "Library";
            }
            else if (sdk.Contains("Web", StringComparison.OrdinalIgnoreCase))
            {
                outputType = "Exe";
            }
            else
            {
                outputType = "Library";
            }
        }
        else if (useWpf)
        {
            if (hasAppXaml)
            {
                outputType = "WinExe";
            }
            else if (string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase))
            {
                outputType = "Library";
            }
        }

        enableWindowsTargeting = enableWindowsTargeting || useWpf || useWindowsForms || targetFramework.Contains("windows", StringComparison.OrdinalIgnoreCase);
        return new CompileProjectProfile(targetFramework, outputType, useWpf, useWindowsForms, enableWindowsTargeting);
    }

    public BuildError? ValidateDeclaredProjectType(string sourceRoot, CompileProjectProfile profile)
    {
        var metadataPath = Path.Combine(sourceRoot, "template.json");
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(metadataPath));
            if (!json.RootElement.TryGetProperty("ProjectType", out var projectTypeElement))
            {
                return null;
            }

            var declared = projectTypeElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(declared))
            {
                return null;
            }

            var normalized = declared.ToLowerInvariant();
            switch (normalized)
            {
                case "wpf-app":
                    if (!profile.UseWpf || !string.Equals(profile.OutputType, "WinExe", StringComparison.OrdinalIgnoreCase))
                    {
                        return new BuildError("CompileGate", 0, "PROJECT_TYPE_MISMATCH", $"ProjectType '{declared}' requires WPF WinExe profile.");
                    }

                    return null;
                case "wpf-library":
                    if (!profile.UseWpf || string.Equals(profile.OutputType, "WinExe", StringComparison.OrdinalIgnoreCase))
                    {
                        return new BuildError("CompileGate", 0, "PROJECT_TYPE_MISMATCH", $"ProjectType '{declared}' requires WPF library profile.");
                    }

                    return null;
                case "console":
                    if (profile.UseWpf)
                    {
                        return new BuildError("CompileGate", 0, "PROJECT_TYPE_MISMATCH", "ProjectType 'console' cannot be compiled with WPF profile.");
                    }

                    return null;
                case "web":
                    return null;
                case "class-library":
                case "library":
                    if (string.Equals(profile.OutputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(profile.OutputType, "WinExe", StringComparison.OrdinalIgnoreCase))
                    {
                        return new BuildError("CompileGate", 0, "PROJECT_TYPE_MISMATCH", $"ProjectType '{declared}' requires library output type.");
                    }

                    return null;
                default:
                    return new BuildError("CompileGate", 0, "PROJECT_TYPE_UNSUPPORTED", $"Unsupported ProjectType '{declared}'.");
            }
        }
        catch (Exception ex)
        {
            return new BuildError("CompileGate", 0, "PROJECT_TYPE_INVALID_METADATA", $"Failed to parse template metadata: {ex.Message}");
        }
    }

    private static void CopyCompilationInputs(string sourceRoot, string compileWorkspace)
    {
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
        {
            if (!ShouldCopyCompilationInput(file))
            {
                continue;
            }

            if (file.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceRoot, file);
            var destination = Path.Combine(compileWorkspace, relative);
            var destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(file, destination, overwrite: true);
        }
    }

    private static async Task EnsureCompileProjectAsync(
        string compileWorkspace,
        DependencyReferences refs,
        CompileProjectProfile profile,
        CancellationToken ct)
    {
        var csprojPath = Path.Combine(compileWorkspace, GeneratedProjectFileName);
        var sb = new StringBuilder();
        sb.AppendLine("""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
""");
        sb.AppendLine($"""    <TargetFramework>{EscapeXml(profile.TargetFramework)}</TargetFramework>""");
        sb.AppendLine($"""    <OutputType>{EscapeXml(profile.OutputType)}</OutputType>""");
        if (profile.UseWpf)
        {
            sb.AppendLine("    <UseWPF>true</UseWPF>");
        }

        if (profile.UseWindowsForms)
        {
            sb.AppendLine("    <UseWindowsForms>true</UseWindowsForms>");
        }

        if (profile.EnableWindowsTargeting)
        {
            sb.AppendLine("    <EnableWindowsTargeting>true</EnableWindowsTargeting>");
        }

        sb.AppendLine("""
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
""");
        if (refs.PackageReferences.Count > 0 || refs.FrameworkReferences.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var package in refs.PackageReferences.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(package.Value))
                {
                    sb.AppendLine($"""    <PackageReference Include="{EscapeXml(package.Key)}" />""");
                }
                else
                {
                    sb.AppendLine($"""    <PackageReference Include="{EscapeXml(package.Key)}" Version="{EscapeXml(package.Value)}" />""");
                }
            }

            foreach (var framework in refs.FrameworkReferences.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"""    <FrameworkReference Include="{EscapeXml(framework)}" />""");
            }

            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine("</Project>");
        await File.WriteAllTextAsync(csprojPath, sb.ToString(), Encoding.UTF8, ct);
    }

    private static async Task CreateXamlCodeBehindStubsAsync(string compileWorkspace, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(compileWorkspace, "*.xaml.cs", SearchOption.AllDirectories))
        {
            var associatedXaml = file[..^3];
            if (File.Exists(associatedXaml))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(file, ct);
            var namespaceName = NamespaceRegex.Match(content).Groups[1].Value;
            var className = PartialClassRegex.Match(content).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(namespaceName) || string.IsNullOrWhiteSpace(className))
            {
                continue;
            }

            var stubPath = Path.Combine(Path.GetDirectoryName(file) ?? compileWorkspace, $"{className}.g.cs");
            if (File.Exists(stubPath))
            {
                continue;
            }

            var stub = $@"namespace {namespaceName}
{{
    public partial class {className}
    {{
        private void InitializeComponent()
        {{
        }}
    }}
}}";
            await File.WriteAllTextAsync(stubPath, stub, Encoding.UTF8, ct);
        }

        foreach (var xamlFile in Directory.EnumerateFiles(compileWorkspace, "*.xaml", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var xamlCsFile = xamlFile + ".cs";
            if (!File.Exists(xamlCsFile))
            {
                continue;
            }

            var xamlText = await File.ReadAllTextAsync(xamlFile, ct);
            var handlers = XamlEventRegex.Matches(xamlText)
                .Select(m => m.Groups["handler"].Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (handlers.Count == 0)
            {
                continue;
            }

            var codeBehind = await File.ReadAllTextAsync(xamlCsFile, ct);
            var ns = NamespaceRegex.Match(codeBehind).Groups[1].Value;
            var className = PartialClassRegex.Match(codeBehind).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(className))
            {
                continue;
            }

            var existing = handlers
                .Where(handler => Regex.IsMatch(codeBehind, $@"\b{Regex.Escape(handler)}\s*\(", RegexOptions.CultureInvariant))
                .ToHashSet(StringComparer.Ordinal);
            var missing = handlers.Where(handler => !existing.Contains(handler)).ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            var eventsStubPath = Path.Combine(
                Path.GetDirectoryName(xamlCsFile) ?? compileWorkspace,
                $"{className}.events.g.cs");
            var methods = string.Join(Environment.NewLine + Environment.NewLine, missing.Select(BuildEventHandlerStub));
            var stubContent = $@"namespace {ns}
{{
    public partial class {className}
    {{
{IndentBlock(methods, 8)}
    }}
}}";
            await File.WriteAllTextAsync(eventsStubPath, stubContent, Encoding.UTF8, ct);
        }
    }

    private static bool DetectWpfCodeIndicators(string sourceRoot)
    {
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(file);
                if (content.Contains("global::System.Windows.", StringComparison.Ordinal) ||
                    content.Contains("System.Windows.", StringComparison.Ordinal) ||
                    content.Contains("InitializeComponent(", StringComparison.Ordinal) ||
                    content.Contains("RoutedEventArgs", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore unreadable files during heuristic detection.
            }
        }

        return false;
    }

    private static string EscapeXml(string value)
    {
        return System.Security.SecurityElement.Escape(value) ?? value;
    }

    private static bool ShouldCopyCompilationInput(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildEventHandlerStub(string methodName)
    {
        return $@"private void {methodName}(global::System.Object sender, global::System.Windows.RoutedEventArgs e)
        {{
        }}";
    }

    private static string IndentBlock(string text, int spaces)
    {
        var indent = new string(' ', spaces);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return string.Join(Environment.NewLine, lines.Select(line => indent + line));
    }
}

public sealed record DependencyReferences(
    IReadOnlyDictionary<string, string> PackageReferences,
    IReadOnlySet<string> FrameworkReferences);

public sealed record CompileProjectProfile(
    string TargetFramework,
    string OutputType,
    bool UseWpf,
    bool UseWindowsForms,
    bool EnableWindowsTargeting);

