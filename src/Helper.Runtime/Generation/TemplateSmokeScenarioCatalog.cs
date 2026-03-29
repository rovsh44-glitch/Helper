using System.Text.Json;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

internal static class TemplateSmokeScenarioCatalog
{
    public static IReadOnlyList<string> ResolveScenarioIds(
        string templatePath,
        TemplateMetadataModel? metadata,
        PolyglotProjectProfile profile)
    {
        if (metadata?.SmokeScenarios is { Length: > 0 })
        {
            return metadata.SmokeScenarios
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var scenarios = new List<string>
        {
            "compile",
            "artifact-validation",
            "no-zero-byte-sources",
            "capability-contract"
        };

        switch (profile.Kind)
        {
            case PolyglotProjectKind.Dotnet:
                scenarios.Add("dotnet-csproj-exists");
                scenarios.Add("csharp-source-exists");
                if (IsWpfTemplate(templatePath, metadata))
                {
                    scenarios.Add("wpf-mainwindow-present");
                }

                break;
            case PolyglotProjectKind.JavaScript:
                scenarios.Add("node-package-json");
                scenarios.Add("javascript-source-exists");
                break;
            case PolyglotProjectKind.TypeScript:
                scenarios.Add("node-package-json");
                scenarios.Add("typescript-source-exists");
                scenarios.Add("typescript-tsconfig-present");
                scenarios.Add("react-entrypoint-present");
                break;
            case PolyglotProjectKind.Python:
                scenarios.Add("python-source-exists");
                scenarios.Add("python-requirements-present");
                break;
            default:
                scenarios.Add("supported-source-exists");
                break;
        }

        return scenarios;
    }

    public static async Task<TemplateCertificationSmokeScenario> EvaluateScenarioAsync(
        string templatePath,
        TemplateMetadataModel? metadata,
        PolyglotProjectProfile profile,
        string scenarioId,
        bool compilePassed,
        bool artifactPassed,
        CancellationToken ct)
    {
        var normalized = scenarioId.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "compile":
                return new TemplateCertificationSmokeScenario(
                    Id: scenarioId,
                    Description: "Compile gate must pass.",
                    Passed: compilePassed,
                    Details: compilePassed ? "Compile gate passed." : "Compile gate failed.");
            case "artifact-validation":
                return new TemplateCertificationSmokeScenario(
                    Id: scenarioId,
                    Description: "Artifact validation must pass.",
                    Passed: artifactPassed,
                    Details: artifactPassed ? "Artifact validation passed." : "Artifact validation failed.");
            case "dotnet-csproj-exists":
            case "smoke.csproj.exists":
                return EvaluateFilePresenceScenario(
                    scenarioId,
                    "Template contains at least one .csproj file.",
                    Directory.EnumerateFiles(templatePath, "*.csproj", SearchOption.AllDirectories)
                        .Any(path => !IsBuildArtifactPath(path)),
                    "No .csproj files found.");
            case "csharp-source-exists":
            case "smoke.csharp.exists":
                return EvaluateFilePresenceScenario(
                    scenarioId,
                    "Template contains C# sources.",
                    Directory.EnumerateFiles(templatePath, "*.cs", SearchOption.AllDirectories)
                        .Any(path => !IsBuildArtifactPath(path)),
                    "No C# files found.");
            case "wpf-mainwindow-present":
            case "smoke.wpf.mainwindow":
                return EvaluateFilePresenceScenario(
                    scenarioId,
                    "WPF template contains MainWindow.xaml.",
                    Directory.EnumerateFiles(templatePath, "MainWindow.xaml", SearchOption.AllDirectories)
                        .Any(path => !IsBuildArtifactPath(path)),
                    "MainWindow.xaml missing.");
            case "node-package-json":
                return EvaluateNodePackageJsonScenario(templatePath, scenarioId);
            case "javascript-source-exists":
                return EvaluateFilePresenceScenario(
                    scenarioId,
                    "Template contains JavaScript sources.",
                    Directory.EnumerateFiles(templatePath, "*.*", SearchOption.AllDirectories)
                        .Any(path => !IsBuildArtifactPath(path) &&
                                     (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                                      path.EndsWith(".cjs", StringComparison.OrdinalIgnoreCase) ||
                                      path.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))),
                    "No JavaScript source files found.");
            case "typescript-source-exists":
                return EvaluateFilePresenceScenario(
                    scenarioId,
                    "Template contains TypeScript sources.",
                    Directory.EnumerateFiles(templatePath, "*.*", SearchOption.AllDirectories)
                        .Any(path => !IsBuildArtifactPath(path) &&
                                     (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                                      path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))),
                    "No TypeScript source files found.");
            case "python-source-exists":
                return EvaluateFilePresenceScenario(
                    scenarioId,
                    "Template contains Python sources.",
                    Directory.EnumerateFiles(templatePath, "*.py", SearchOption.AllDirectories)
                        .Any(path => !IsBuildArtifactPath(path)),
                    "No Python source files found.");
            case "python-requirements-present":
                return EvaluateFilePresenceScenario(
                    scenarioId,
                    "Python template contains dependency manifest.",
                    File.Exists(Path.Combine(templatePath, "requirements.txt")) ||
                    File.Exists(Path.Combine(templatePath, "pyproject.toml")),
                    "requirements.txt or pyproject.toml not found.");
            case "supported-source-exists":
                return EvaluateFilePresenceScenario(
                    scenarioId,
                    "Template contains supported source files.",
                    EnumerateSourceFiles(templatePath, profile).Any(),
                    "No supported source files found.");
            case "typescript-tsconfig-present":
                return EvaluateFilePresenceScenario(
                    scenarioId,
                    "TypeScript template contains tsconfig.json.",
                    File.Exists(Path.Combine(templatePath, "tsconfig.json")),
                    "tsconfig.json not found.");
            case "react-entrypoint-present":
                return EvaluateFilePresenceScenario(
                    scenarioId,
                    "React template contains src/main.tsx and src/App.tsx.",
                    File.Exists(Path.Combine(templatePath, "src", "main.tsx")) &&
                    File.Exists(Path.Combine(templatePath, "src", "App.tsx")),
                    "React entrypoint files are missing.");
            case "capability-contract":
                return CapabilityContractValidator.EvaluateContract(
                    scenarioId,
                    templatePath,
                    metadata,
                    profile,
                    IsBuildArtifactPath);
            case "no-zero-byte-sources":
                return EvaluateZeroByteSourceScenario(templatePath, profile, scenarioId);
            case "pdf-epub-roundtrip-e2e":
            case "pdf-epub-e2e":
            case "smoke.pdfepub.e2e":
                return await PdfEpubSmokeScenarioRunner.EvaluateAsync(templatePath, scenarioId, ct).ConfigureAwait(false);
            default:
                return new TemplateCertificationSmokeScenario(
                    Id: scenarioId,
                    Description: "Unknown smoke scenario id.",
                    Passed: false,
                    Details: $"Unknown smoke scenario: '{scenarioId}'.");
        }
    }

    public static bool IsBuildArtifactPath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.DirectorySeparatorChar}__pycache__{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static TemplateCertificationSmokeScenario EvaluateNodePackageJsonScenario(string templatePath, string scenarioId)
    {
        var packageJsonPath = Path.Combine(templatePath, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: "Node template contains package.json.",
                Passed: false,
                Details: "package.json not found.");
        }

        try
        {
            using var _ = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: "Node template contains valid package.json.",
                Passed: true,
                Details: "package.json is present and valid.");
        }
        catch (Exception ex)
        {
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: "Node template contains valid package.json.",
                Passed: false,
                Details: $"package.json parse failed: {ex.Message}");
        }
    }

    private static TemplateCertificationSmokeScenario EvaluateZeroByteSourceScenario(
        string templatePath,
        PolyglotProjectProfile profile,
        string scenarioId)
    {
        var sourceFiles = EnumerateSourceFiles(templatePath, profile).ToList();
        if (sourceFiles.Count == 0)
        {
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: "All source files are non-empty.",
                Passed: false,
                Details: "No source files were found for zero-byte validation.");
        }

        var zeroByte = sourceFiles.Count(path => new FileInfo(path).Length == 0);
        return new TemplateCertificationSmokeScenario(
            Id: scenarioId,
            Description: "All source files are non-empty.",
            Passed: zeroByte == 0,
            Details: zeroByte == 0
                ? $"Checked {sourceFiles.Count} source file(s)."
                : $"Detected {zeroByte} zero-byte source file(s).");
    }

    private static TemplateCertificationSmokeScenario EvaluateFilePresenceScenario(
        string id,
        string description,
        bool exists,
        string missingMessage)
    {
        return new TemplateCertificationSmokeScenario(
            Id: id,
            Description: description,
            Passed: exists,
            Details: exists ? "Found." : missingMessage);
    }

    private static bool IsWpfTemplate(string templatePath, TemplateMetadataModel? metadata)
    {
        if (metadata?.Tags?.Any(x => x.Contains("wpf", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return true;
        }

        return Directory.EnumerateFiles(templatePath, "*.xaml", SearchOption.AllDirectories)
            .Any(path => !IsBuildArtifactPath(path));
    }

    private static IEnumerable<string> EnumerateSourceFiles(string templatePath, PolyglotProjectProfile profile)
    {
        foreach (var file in Directory.EnumerateFiles(templatePath, "*.*", SearchOption.AllDirectories))
        {
            if (IsBuildArtifactPath(file))
            {
                continue;
            }

            var extension = Path.GetExtension(file);
            var include = profile.Kind switch
            {
                PolyglotProjectKind.Dotnet => extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                                              extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase),
                PolyglotProjectKind.JavaScript => extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                                                  extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase) ||
                                                  extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase),
                PolyglotProjectKind.TypeScript => extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                                                  extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
                                                  extension.Equals(".js", StringComparison.OrdinalIgnoreCase),
                PolyglotProjectKind.Python => extension.Equals(".py", StringComparison.OrdinalIgnoreCase),
                _ => extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".py", StringComparison.OrdinalIgnoreCase)
            };

            if (include)
            {
                yield return file;
            }
        }
    }
}

