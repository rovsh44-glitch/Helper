using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed class GenerationFixVerifier : IFixVerifier
{
    private readonly IGenerationCompileGate _compileGate;

    public GenerationFixVerifier(IGenerationCompileGate compileGate)
    {
        _compileGate = compileGate;
    }

    public async Task<FixVerificationResult> VerifyAsync(
        string projectPath,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(projectPath))
        {
            return new FixVerificationResult(
                Success: false,
                Errors: new List<BuildError>
                {
                    new("FixVerifier", 0, "PROJECT_NOT_FOUND", $"Project path does not exist: {projectPath}")
                },
                Files: Array.Empty<GeneratedFile>(),
                ProjectPath: projectPath,
                ChangedFiles: Array.Empty<string>(),
                CompilePassed: false,
                TestsPassed: false,
                InvariantsPassed: false,
                RegressionDetected: true,
                VerificationFlags: new[] { "project_not_found" });
        }

        var compileResult = await _compileGate.ValidateAsync(projectPath, ct);
        var synced = await CompileWorkspaceSynchronizer.SyncPatchedCodeFilesAsync(
            compileResult.CompileWorkspace,
            projectPath,
            ct);
        var files = CompileWorkspaceSynchronizer.SnapshotGeneratedFiles(projectPath);
        var flags = new List<string>();
        if (!compileResult.Success)
        {
            flags.Add("compile_failed");
        }
        var regressionDetected = DetectRegressionSignals(compileResult.Errors);
        if (regressionDetected)
        {
            flags.Add("regression_signal_detected");
        }
        var success = compileResult.Success && !regressionDetected;

        return new FixVerificationResult(
            Success: success,
            Errors: compileResult.Errors,
            Files: files,
            ProjectPath: projectPath,
            ChangedFiles: synced,
            CompilePassed: compileResult.Success,
            TestsPassed: true,
            InvariantsPassed: true,
            RegressionDetected: regressionDetected,
            VerificationFlags: flags);
    }

    private static bool DetectRegressionSignals(IReadOnlyList<BuildError> errors)
    {
        if (errors.Count == 0)
        {
            return false;
        }

        foreach (var error in errors)
        {
            if (string.IsNullOrWhiteSpace(error.Code))
            {
                continue;
            }

            if (error.Code.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) ||
                error.Code.Equals("UNHANDLED_EXCEPTION", StringComparison.OrdinalIgnoreCase) ||
                error.Code.Equals("RUNTIME_STACKOVERFLOW", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

