using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

internal enum PolyglotProjectKind
{
    Dotnet,
    JavaScript,
    TypeScript,
    Python,
    Unknown
}

internal sealed record PolyglotProjectProfile(
    PolyglotProjectKind Kind,
    string? Language);

internal static class PolyglotCompileGateValidator
{
    private static readonly HashSet<string> ExcludedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        ".vs",
        ".compile_gate",
        "node_modules"
    };

    private static readonly HashSet<string> DotnetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".xaml"
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

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(45);

    public static PolyglotProjectProfile DetectProfile(string sourceRoot)
    {
        var language = ReadTemplateLanguage(sourceRoot);
        if (HasAnyFile(sourceRoot, DotnetExtensions) || HasAnyProjectFile(sourceRoot))
        {
            return new PolyglotProjectProfile(PolyglotProjectKind.Dotnet, language);
        }

        if (Contains(language, "python") || HasAnyFile(sourceRoot, PythonExtensions))
        {
            return new PolyglotProjectProfile(PolyglotProjectKind.Python, language);
        }

        if (Contains(language, "typescript") || HasAnyFile(sourceRoot, TypeScriptExtensions))
        {
            return new PolyglotProjectProfile(PolyglotProjectKind.TypeScript, language);
        }

        if (Contains(language, "javascript") || HasAnyFile(sourceRoot, JavaScriptExtensions) || File.Exists(Path.Combine(sourceRoot, "package.json")))
        {
            return new PolyglotProjectProfile(PolyglotProjectKind.JavaScript, language);
        }

        return new PolyglotProjectProfile(PolyglotProjectKind.Unknown, language);
    }

    public static async Task<CompileGateResult?> TryValidateNonDotnetAsync(
        string sourceRoot,
        string compileWorkspace,
        PolyglotProjectProfile profile,
        CancellationToken ct)
    {
        if (profile.Kind == PolyglotProjectKind.Dotnet)
        {
            return null;
        }

        var errors = await ValidateNonDotnetSourcesAsync(sourceRoot, profile, ct);
        return new CompileGateResult(errors.Count == 0, errors, compileWorkspace);
    }

    public static async Task<IReadOnlyList<BuildError>> ValidateNonDotnetSourcesAsync(
        string sourceRoot,
        PolyglotProjectProfile? profile = null,
        CancellationToken ct = default)
    {
        var resolvedProfile = profile ?? DetectProfile(sourceRoot);
        if (resolvedProfile.Kind == PolyglotProjectKind.Dotnet)
        {
            return Array.Empty<BuildError>();
        }

        var errors = new List<BuildError>();
        switch (resolvedProfile.Kind)
        {
            case PolyglotProjectKind.JavaScript:
                await ValidateJavaScriptAsync(sourceRoot, errors, ct);
                break;
            case PolyglotProjectKind.TypeScript:
                await ValidateTypeScriptAsync(sourceRoot, errors, ct);
                break;
            case PolyglotProjectKind.Python:
                await ValidatePythonAsync(sourceRoot, errors, ct);
                break;
            default:
                ValidateUnknown(sourceRoot, errors);
                break;
        }

        return errors;
    }

    private static async Task ValidateJavaScriptAsync(string sourceRoot, List<BuildError> errors, CancellationToken ct)
    {
        var jsFiles = EnumerateFiles(sourceRoot, JavaScriptExtensions).ToList();
        if (jsFiles.Count == 0)
        {
            errors.Add(new BuildError("CompileGate", 0, "NO_SOURCE_FILES", "JavaScript project has no .js/.cjs/.mjs files."));
            return;
        }

        AppendZeroByteErrors(jsFiles, errors);
        ValidatePackageJson(sourceRoot, errors);

        foreach (var file in jsFiles)
        {
            ct.ThrowIfCancellationRequested();
            var result = await RunProcessAsync("node", $"--check \"{file}\"", sourceRoot, ProcessTimeout, ct);
            if (!result.Started)
            {
                errors.Add(new BuildError(
                    file,
                    0,
                    "NODE_NOT_AVAILABLE",
                    "Node.js is not available. Skipping JS syntax verification would be unsafe."));
                return;
            }

            if (result.TimedOut)
            {
                errors.Add(new BuildError(file, 0, "NODE_TIMEOUT", "Node.js syntax check timed out."));
                continue;
            }

            if (result.ExitCode != 0)
            {
                var message = ExtractProcessError(result, "JavaScript syntax check failed.");
                errors.Add(new BuildError(file, 0, "NODE_SYNTAX", message));
            }
        }
    }

    private static async Task ValidateTypeScriptAsync(string sourceRoot, List<BuildError> errors, CancellationToken ct)
    {
        var tsFiles = EnumerateFiles(sourceRoot, TypeScriptExtensions).ToList();
        var jsFiles = EnumerateFiles(sourceRoot, JavaScriptExtensions).ToList();
        if (tsFiles.Count == 0 && jsFiles.Count == 0)
        {
            errors.Add(new BuildError("CompileGate", 0, "NO_SOURCE_FILES", "TypeScript project has no .ts/.tsx/.js source files."));
            return;
        }

        AppendZeroByteErrors(tsFiles, errors);
        AppendZeroByteErrors(jsFiles, errors);
        ValidatePackageJson(sourceRoot, errors);
        ValidateTypeScriptConfig(sourceRoot, errors);

        foreach (var file in jsFiles)
        {
            ct.ThrowIfCancellationRequested();
            var result = await RunProcessAsync("node", $"--check \"{file}\"", sourceRoot, ProcessTimeout, ct);
            if (!result.Started)
            {
                errors.Add(new BuildError(
                    file,
                    0,
                    "NODE_NOT_AVAILABLE",
                    "Node.js is not available. TypeScript companion JavaScript files cannot be verified."));
                return;
            }

            if (result.TimedOut)
            {
                errors.Add(new BuildError(file, 0, "NODE_TIMEOUT", "Node.js syntax check timed out."));
                continue;
            }

            if (result.ExitCode != 0)
            {
                var message = ExtractProcessError(result, "JavaScript companion file syntax check failed.");
                errors.Add(new BuildError(file, 0, "NODE_SYNTAX", message));
            }
        }
    }

    private static async Task ValidatePythonAsync(string sourceRoot, List<BuildError> errors, CancellationToken ct)
    {
        var pyFiles = EnumerateFiles(sourceRoot, PythonExtensions).ToList();
        if (pyFiles.Count == 0)
        {
            errors.Add(new BuildError("CompileGate", 0, "NO_SOURCE_FILES", "Python project has no .py source files."));
            return;
        }

        AppendZeroByteErrors(pyFiles, errors);

        var launcher = await ResolvePythonLauncherAsync(sourceRoot, ct);
        if (launcher is null)
        {
            errors.Add(new BuildError(
                "CompileGate",
                0,
                "PYTHON_NOT_AVAILABLE",
                "Python launcher was not found. Unable to run py_compile for syntax verification."));
            return;
        }

        foreach (var file in pyFiles)
        {
            ct.ThrowIfCancellationRequested();
            var result = await RunProcessAsync(launcher.Value.FileName, $"{launcher.Value.ArgumentsPrefix} -m py_compile \"{file}\"", sourceRoot, ProcessTimeout, ct);
            if (!result.Started)
            {
                errors.Add(new BuildError(file, 0, "PYTHON_NOT_AVAILABLE", "Python launcher became unavailable during syntax verification."));
                return;
            }

            if (result.TimedOut)
            {
                errors.Add(new BuildError(file, 0, "PYTHON_TIMEOUT", "Python syntax check timed out."));
                continue;
            }

            if (result.ExitCode != 0)
            {
                var message = ExtractProcessError(result, "Python syntax validation failed.");
                errors.Add(new BuildError(file, 0, "PYTHON_SYNTAX", message));
            }
        }
    }

    private static void ValidateUnknown(string sourceRoot, List<BuildError> errors)
    {
        var knownSources = EnumerateFiles(
            sourceRoot,
            DotnetExtensions
                .Concat(JavaScriptExtensions)
                .Concat(TypeScriptExtensions)
                .Concat(PythonExtensions)
                .ToHashSet(StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (knownSources.Count == 0)
        {
            errors.Add(new BuildError(
                "CompileGate",
                0,
                "NO_VALIDATION_TARGET",
                "No supported source files were found for compile gate validation."));
            return;
        }

        AppendZeroByteErrors(knownSources, errors);
    }

    private static void ValidatePackageJson(string sourceRoot, List<BuildError> errors)
    {
        var packageJsonPath = Path.Combine(sourceRoot, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            errors.Add(new BuildError("CompileGate", 0, "PACKAGE_JSON_MISSING", "Node/TypeScript project must contain package.json."));
            return;
        }

        try
        {
            using var _ = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        }
        catch (Exception ex)
        {
            errors.Add(new BuildError(packageJsonPath, 0, "PACKAGE_JSON_INVALID", ex.Message));
        }
    }

    private static void ValidateTypeScriptConfig(string sourceRoot, List<BuildError> errors)
    {
        var tsconfigPath = Path.Combine(sourceRoot, "tsconfig.json");
        if (!File.Exists(tsconfigPath))
        {
            errors.Add(new BuildError("CompileGate", 0, "TSCONFIG_MISSING", "TypeScript project must contain tsconfig.json."));
            return;
        }

        try
        {
            using var _ = JsonDocument.Parse(File.ReadAllText(tsconfigPath));
        }
        catch (Exception ex)
        {
            errors.Add(new BuildError(tsconfigPath, 0, "TSCONFIG_INVALID", ex.Message));
        }
    }

    private static void AppendZeroByteErrors(IEnumerable<string> files, List<BuildError> errors)
    {
        foreach (var file in files)
        {
            var length = new FileInfo(file).Length;
            if (length == 0)
            {
                errors.Add(new BuildError(file, 0, "ZERO_BYTE_SOURCE", "Source file is empty (0 bytes)."));
            }
        }
    }

    private static async Task<(string FileName, string ArgumentsPrefix)?> ResolvePythonLauncherAsync(string workingDirectory, CancellationToken ct)
    {
        var pyCheck = await RunProcessAsync("py", "-3 --version", workingDirectory, TimeSpan.FromSeconds(5), ct);
        if (pyCheck.Started && pyCheck.ExitCode == 0)
        {
            return ("py", "-3");
        }

        var pythonCheck = await RunProcessAsync("python", "--version", workingDirectory, TimeSpan.FromSeconds(5), ct);
        if (pythonCheck.Started && pythonCheck.ExitCode == 0)
        {
            return ("python", string.Empty);
        }

        return null;
    }

    private static string? ReadTemplateLanguage(string sourceRoot)
    {
        var metadataPath = Path.Combine(sourceRoot, "template.json");
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(metadataPath));
            if (json.RootElement.TryGetProperty("Language", out var languageElement))
            {
                return languageElement.GetString();
            }

            if (json.RootElement.TryGetProperty("language", out var lowerLanguageElement))
            {
                return lowerLanguageElement.GetString();
            }
        }
        catch
        {
            // Keep detector resilient; validation layer will report structural issues.
        }

        return null;
    }

    private static bool Contains(string? source, string marker)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               source.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAnyProjectFile(string sourceRoot)
    {
        return Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Any(path => !ShouldSkip(sourceRoot, path));
    }

    private static bool HasAnyFile(string sourceRoot, IReadOnlySet<string> extensions)
    {
        return EnumerateFiles(sourceRoot, extensions).Any();
    }

    private static IEnumerable<string> EnumerateFiles(string sourceRoot, IReadOnlySet<string> extensions)
    {
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
        {
            if (ShouldSkip(sourceRoot, file))
            {
                continue;
            }

            var extension = Path.GetExtension(file);
            if (extensions.Contains(extension))
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

        var segments = directory.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => ExcludedSegments.Contains(segment));
    }

    private static string ExtractProcessError(ProcessExecutionResult result, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(result.Stderr))
        {
            return FirstLine(result.Stderr);
        }

        if (!string.IsNullOrWhiteSpace(result.Stdout))
        {
            return FirstLine(result.Stdout);
        }

        return fallback;
    }

    private static string FirstLine(string text)
    {
        return text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim() ?? string.Empty;
    }

    private static async Task<ProcessExecutionResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return ProcessExecutionResult.NotStarted;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore kill failures on timeout branch.
            }

            return new ProcessExecutionResult(true, -1, string.Empty, string.Empty, true);
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        return new ProcessExecutionResult(true, process.ExitCode, stdout, stderr, false);
    }

    private sealed record ProcessExecutionResult(
        bool Started,
        int ExitCode,
        string Stdout,
        string Stderr,
        bool TimedOut)
    {
        public static readonly ProcessExecutionResult NotStarted =
            new(false, -1, string.Empty, string.Empty, false);
    }
}

