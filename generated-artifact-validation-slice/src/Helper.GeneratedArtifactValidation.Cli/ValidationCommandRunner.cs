using Helper.GeneratedArtifactValidation.Contracts;
using Helper.GeneratedArtifactValidation.Core;

namespace Helper.GeneratedArtifactValidation.Cli;

public static class ValidationCommandRunner
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct = default)
    {
        if (args.Length == 0)
        {
            WriteUsage(stdout);
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "artifacts" => await RunArtifactsAsync(args, stdout, stderr, ct),
            "blueprint" => await RunBlueprintAsync(args, stdout, stderr, ct),
            "compile-gate" => await RunCompileGateAsync(args, stdout, stderr, ct),
            "samples" => await RunSamplesAsync(args, stdout, stderr, ct),
            "help" or "--help" or "-h" => WriteHelp(stdout),
            _ => WriteUnknownCommand(args[0], stdout)
        };
    }

    private static async Task<int> RunArtifactsAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var root = ReadRequiredOption(args, "--root");
        if (string.IsNullOrWhiteSpace(root))
        {
            await stderr.WriteLineAsync("Missing required option: --root <fixture-directory>");
            return 2;
        }

        var validator = new ArtifactValidationService();
        var report = await validator.ValidateFixtureDirectoryAsync(Path.GetFullPath(root), ct);
        await WriteArtifactReportAsync(report, stdout);
        return report.Success ? 0 : 2;
    }

    private static async Task<int> RunBlueprintAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var input = ReadRequiredOption(args, "--input");
        if (string.IsNullOrWhiteSpace(input))
        {
            await stderr.WriteLineAsync("Missing required option: --input <blueprint-json>");
            return 3;
        }

        var fullPath = Path.GetFullPath(input);
        if (!File.Exists(fullPath))
        {
            await stderr.WriteLineAsync($"Blueprint file '{fullPath}' does not exist.");
            return 3;
        }

        var blueprint = await ValidationJson.LoadBlueprintAsync(fullPath, ct);
        if (blueprint is null)
        {
            await stderr.WriteLineAsync($"Blueprint file '{fullPath}' is empty or invalid JSON.");
            return 3;
        }

        var result = new BlueprintContractValidator().ValidateAndNormalize(blueprint);
        if (!result.IsValid || result.Blueprint is null)
        {
            foreach (var error in result.Errors)
            {
                await stderr.WriteLineAsync(error);
            }

            return 3;
        }

        await stdout.WriteLineAsync(ValidationJson.Serialize(result.Blueprint));
        foreach (var warning in result.Warnings)
        {
            await stdout.WriteLineAsync($"WARNING: {warning}");
        }

        return 0;
    }

    private static async Task<int> RunCompileGateAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var root = ReadRequiredOption(args, "--root");
        if (string.IsNullOrWhiteSpace(root))
        {
            await stderr.WriteLineAsync("Missing required option: --root <project-directory>");
            return 4;
        }

        var expectFailure = HasFlag(args, "--expect-failure");
        var validator = new CompileGateValidator();
        var result = await validator.ValidateAsync(Path.GetFullPath(root), ct);

        try
        {
            if (result.Success)
            {
                await stdout.WriteLineAsync($"Compile gate PASS: {root}");
            }
            else
            {
                await stdout.WriteLineAsync($"Compile gate FAIL: {root}");
                foreach (var error in result.Errors)
                {
                    await stdout.WriteLineAsync($"  [{error.Code}] {error.File}:{error.Line} {error.Message}");
                }
            }

            if (expectFailure)
            {
                return result.Success ? 4 : 0;
            }

            return result.Success ? 0 : 4;
        }
        finally
        {
            TryDeleteDirectory(result.CompileWorkspace);
        }
    }

    private static async Task<int> RunSamplesAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var sliceRoot = ResolveSliceRoot(ReadOptionalOption(args, "--root"));
        var fixtureRoot = Path.Combine(sliceRoot, "sample_fixtures");
        var goodArtifactsRoot = Path.Combine(fixtureRoot, "artifacts", "good");
        var badArtifactsRoot = Path.Combine(fixtureRoot, "artifacts", "bad");
        var blueprintPath = Path.Combine(fixtureRoot, "blueprints", "malformed-blueprint.json");
        var goodCompileRoot = Path.Combine(fixtureRoot, "compile_gate", "good_project");
        var badCompileRoot = Path.Combine(fixtureRoot, "compile_gate", "bad_project");

        var artifactValidator = new ArtifactValidationService();
        var compileGateValidator = new CompileGateValidator();
        var blueprintValidator = new BlueprintContractValidator();

        var goodArtifacts = await artifactValidator.ValidateFixtureDirectoryAsync(goodArtifactsRoot, ct);
        var badArtifacts = await artifactValidator.ValidateFixtureDirectoryAsync(badArtifactsRoot, ct);
        var blueprint = await ValidationJson.LoadBlueprintAsync(blueprintPath, ct);

        if (blueprint is null)
        {
            await stderr.WriteLineAsync("Malformed blueprint fixture could not be loaded.");
            return 5;
        }

        var blueprintResult = blueprintValidator.ValidateAndNormalize(blueprint);
        var goodCompile = await compileGateValidator.ValidateAsync(goodCompileRoot, ct);
        var badCompile = await compileGateValidator.ValidateAsync(badCompileRoot, ct);

        try
        {
            await stdout.WriteLineAsync($"Artifacts good fixture: {(goodArtifacts.Success ? "PASS" : "FAIL")}");
            await stdout.WriteLineAsync($"Artifacts bad fixture: {(!badArtifacts.Success ? "PASS" : "FAIL")}");
            await stdout.WriteLineAsync($"Blueprint malformed fixture: {(blueprintResult.IsValid ? "PASS" : "FAIL")}");
            await stdout.WriteLineAsync($"Compile gate good fixture: {(goodCompile.Success ? "PASS" : "FAIL")}");
            await stdout.WriteLineAsync($"Compile gate bad fixture: {(!badCompile.Success ? "PASS" : "FAIL")}");

            var success =
                goodArtifacts.Success &&
                !badArtifacts.Success &&
                blueprintResult.IsValid &&
                blueprintResult.Blueprint is not null &&
                goodCompile.Success &&
                !badCompile.Success;

            if (!success)
            {
                await stdout.WriteLineAsync("Sample validation sweep failed.");
                return 5;
            }

            await stdout.WriteLineAsync("Sample validation sweep passed.");
            return 0;
        }
        finally
        {
            TryDeleteDirectory(goodCompile.CompileWorkspace);
            TryDeleteDirectory(badCompile.CompileWorkspace);
        }
    }

    private static async Task WriteArtifactReportAsync(ArtifactValidationReport report, TextWriter stdout)
    {
        await stdout.WriteLineAsync(report.Success ? "Artifact validation PASS" : "Artifact validation FAIL");

        foreach (var file in report.Files)
        {
            await stdout.WriteLineAsync($"- {file.RelativePath}: {(file.Success ? "PASS" : "FAIL")}");
            foreach (var warning in file.Warnings)
            {
                await stdout.WriteLineAsync($"  WARNING: {warning}");
            }

            foreach (var error in file.Errors)
            {
                await stdout.WriteLineAsync($"  ERROR: {error}");
            }

            foreach (var finding in file.PlaceholderFindings)
            {
                await stdout.WriteLineAsync($"  FINDING: {finding.ToDisplayString()}");
            }
        }
    }

    private static int WriteHelp(TextWriter stdout)
    {
        WriteUsage(stdout);
        return 0;
    }

    private static int WriteUnknownCommand(string command, TextWriter stdout)
    {
        stdout.WriteLine($"Unknown command: {command}");
        WriteUsage(stdout);
        return 1;
    }

    private static void WriteUsage(TextWriter stdout)
    {
        stdout.WriteLine("Usage:");
        stdout.WriteLine("  artifacts --root <fixture-directory>");
        stdout.WriteLine("  blueprint --input <blueprint-json>");
        stdout.WriteLine("  compile-gate --root <project-directory> [--expect-failure]");
        stdout.WriteLine("  samples [--root <slice-root>]");
    }

    private static string? ReadRequiredOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 1; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static string? ReadOptionalOption(IReadOnlyList<string> args, string name)
        => ReadRequiredOption(args, name);

    private static bool HasFlag(IReadOnlyList<string> args, string flag)
        => args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));

    private static string ResolveSliceRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot);
        }

        var candidates = new[]
        {
            new DirectoryInfo(Directory.GetCurrentDirectory()),
            new DirectoryInfo(AppContext.BaseDirectory)
        };

        foreach (var candidate in candidates)
        {
            var current = candidate;
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "sample_fixtures")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Unable to resolve the generated-artifact-validation-slice root.");
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
        }
    }
}
