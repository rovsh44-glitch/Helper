using System.Text.Json;
using System.Xml.Linq;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Infrastructure;

public sealed class ForgeArtifactValidator : IForgeArtifactValidator
{
    private static readonly HashSet<string> ExcludedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        ".vs",
        ".compile_gate",
        "node_modules",
        "__pycache__"
    };

    private static readonly HashSet<string> JavaScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js",
        ".cjs",
        ".mjs"
    };

    private static readonly HashSet<string> TypeScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ts",
        ".tsx"
    };

    private static readonly HashSet<string> PythonExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".py"
    };

    private static readonly HashSet<string> GenericSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".xaml",
        ".js",
        ".cjs",
        ".mjs",
        ".ts",
        ".tsx",
        ".py",
        ".json"
    };

    public Task<ForgeVerificationResult> ValidateAsync(string projectPath, IReadOnlyList<BuildError> buildErrors, CancellationToken ct = default)
    {
        if (buildErrors.Count > 0)
        {
            var first = buildErrors[0];
            var reason = string.IsNullOrWhiteSpace(first.Code)
                ? first.Message
                : $"[{first.Code}] {first.Message}";
            return Task.FromResult(new ForgeVerificationResult(false, $"Template has build errors: {reason}"));
        }

        if (!Directory.Exists(projectPath))
        {
            return Task.FromResult(new ForgeVerificationResult(false, "Project path does not exist."));
        }

        var profile = PolyglotCompileGateValidator.DetectProfile(projectPath);
        return profile.Kind switch
        {
            PolyglotProjectKind.Dotnet => ValidateDotnetArtifacts(projectPath, ct),
            PolyglotProjectKind.JavaScript => ValidateNodeArtifacts(projectPath, expectTypeScriptSources: false, ct),
            PolyglotProjectKind.TypeScript => ValidateNodeArtifacts(projectPath, expectTypeScriptSources: true, ct),
            PolyglotProjectKind.Python => ValidatePythonArtifacts(projectPath, ct),
            _ => ValidateGenericArtifacts(projectPath, ct)
        };
    }

    private static Task<ForgeVerificationResult> ValidateDotnetArtifacts(string projectPath, CancellationToken ct)
    {
        var projectFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !ShouldSkip(projectPath, path))
            .ToArray();
        if (projectFiles.Length == 0)
        {
            var hasAnyArtifact = Directory.Exists(Path.Combine(projectPath, "bin")) &&
                                 Directory.GetFiles(Path.Combine(projectPath, "bin"), "*.*", SearchOption.AllDirectories)
                                     .Any(file => file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                                  file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(
                hasAnyArtifact
                    ? new ForgeVerificationResult(true, "No csproj found, but binary artifacts were detected.")
                    : new ForgeVerificationResult(false, "No csproj or binary artifacts were found."));
        }

        foreach (var projectFile in projectFiles)
        {
            ct.ThrowIfCancellationRequested();

            var projectDirectory = Path.GetDirectoryName(projectFile);
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                return Task.FromResult(new ForgeVerificationResult(false, $"Cannot resolve project directory for '{projectFile}'."));
            }

            var expectedExtension = ResolveExpectedArtifactExtension(projectFile);
            var binPath = Path.Combine(projectDirectory, "bin");
            if (!Directory.Exists(binPath))
            {
                return Task.FromResult(new ForgeVerificationResult(false, $"Build output folder is missing for '{Path.GetFileName(projectFile)}'."));
            }

            var hasArtifact = Directory.GetFiles(binPath, $"*{expectedExtension}", SearchOption.AllDirectories).Any();
            if (!hasArtifact)
            {
                return Task.FromResult(new ForgeVerificationResult(false, $"Expected '{expectedExtension}' artifact was not found for '{Path.GetFileName(projectFile)}'."));
            }
        }

        return Task.FromResult(new ForgeVerificationResult(true, "Artifacts validated for all project files."));
    }

    private static Task<ForgeVerificationResult> ValidateNodeArtifacts(string projectPath, bool expectTypeScriptSources, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var packageJsonPath = Path.Combine(projectPath, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return Task.FromResult(new ForgeVerificationResult(false, "Node/TypeScript template must contain package.json."));
        }

        try
        {
            using var _ = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ForgeVerificationResult(false, $"package.json is invalid: {ex.Message}"));
        }

        var requiredExtensions = expectTypeScriptSources
            ? TypeScriptExtensions
            : JavaScriptExtensions;
        var files = EnumerateFiles(projectPath, requiredExtensions).ToList();
        if (files.Count == 0)
        {
            var kind = expectTypeScriptSources ? "TypeScript" : "JavaScript";
            return Task.FromResult(new ForgeVerificationResult(false, $"{kind} source files were not found."));
        }

        var zeroBytePath = files.FirstOrDefault(path => new FileInfo(path).Length == 0);
        if (!string.IsNullOrWhiteSpace(zeroBytePath))
        {
            return Task.FromResult(new ForgeVerificationResult(false, $"Zero-byte source detected: {Path.GetFileName(zeroBytePath)}"));
        }

        return Task.FromResult(new ForgeVerificationResult(true, "Node/TypeScript artifact structure validated."));
    }

    private static Task<ForgeVerificationResult> ValidatePythonArtifacts(string projectPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var files = EnumerateFiles(projectPath, PythonExtensions).ToList();
        if (files.Count == 0)
        {
            return Task.FromResult(new ForgeVerificationResult(false, "Python source files were not found."));
        }

        var zeroBytePath = files.FirstOrDefault(path => new FileInfo(path).Length == 0);
        if (!string.IsNullOrWhiteSpace(zeroBytePath))
        {
            return Task.FromResult(new ForgeVerificationResult(false, $"Zero-byte source detected: {Path.GetFileName(zeroBytePath)}"));
        }

        return Task.FromResult(new ForgeVerificationResult(true, "Python artifact structure validated."));
    }

    private static Task<ForgeVerificationResult> ValidateGenericArtifacts(string projectPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var files = EnumerateFiles(projectPath, GenericSourceExtensions).ToList();
        if (files.Count == 0)
        {
            return Task.FromResult(new ForgeVerificationResult(false, "No supported source files were found."));
        }

        var zeroBytePath = files.FirstOrDefault(path => new FileInfo(path).Length == 0);
        if (!string.IsNullOrWhiteSpace(zeroBytePath))
        {
            return Task.FromResult(new ForgeVerificationResult(false, $"Zero-byte source detected: {Path.GetFileName(zeroBytePath)}"));
        }

        return Task.FromResult(new ForgeVerificationResult(true, "Generic artifact structure validated."));
    }

    private static IEnumerable<string> EnumerateFiles(string projectPath, IReadOnlySet<string> extensions)
    {
        foreach (var file in Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories))
        {
            if (ShouldSkip(projectPath, file))
            {
                continue;
            }

            if (extensions.Contains(Path.GetExtension(file)))
            {
                yield return file;
            }
        }
    }

    private static bool ShouldSkip(string sourceRoot, string path)
    {
        var relative = Path.GetRelativePath(sourceRoot, path);
        var directory = Path.GetDirectoryName(relative);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var segments = directory.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => ExcludedSegments.Contains(segment));
    }

    private static string ResolveExpectedArtifactExtension(string projectFilePath)
    {
        try
        {
            var xml = XDocument.Load(projectFilePath);
            var outputType = xml.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("OutputType", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            var sdkValue = xml.Root?.Attribute("Sdk")?.Value ?? string.Empty;

            if (sdkValue.Contains("Web", StringComparison.OrdinalIgnoreCase))
            {
                return ".dll";
            }

            if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase))
            {
                return ".exe";
            }

            return ".dll";
        }
        catch
        {
            return ".dll";
        }
    }
}

