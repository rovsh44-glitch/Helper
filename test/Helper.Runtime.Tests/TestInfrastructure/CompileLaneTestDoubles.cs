using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Tests;

internal sealed class PassingCompileGate : IGenerationCompileGate
{
    public Task<CompileGateResult> ValidateAsync(string rawProjectRoot, CancellationToken ct = default)
    {
        return Task.FromResult(new CompileGateResult(
            Success: true,
            Errors: Array.Empty<BuildError>(),
            CompileWorkspace: Path.Combine(rawProjectRoot, ".compile_gate.stub")));
    }
}

internal sealed class FailingCompileGate : IGenerationCompileGate
{
    private readonly IReadOnlyList<BuildError> _errors;

    public FailingCompileGate(params BuildError[] errors)
    {
        _errors = errors.Length > 0
            ? errors
            : new[] { new BuildError("CompileGate", 0, "FAIL", "Compile gate failed.") };
    }

    public Task<CompileGateResult> ValidateAsync(string rawProjectRoot, CancellationToken ct = default)
    {
        return Task.FromResult(new CompileGateResult(
            Success: false,
            Errors: _errors,
            CompileWorkspace: Path.Combine(rawProjectRoot, ".compile_gate.stub")));
    }
}

internal sealed class PassingBuildValidator : IBuildValidator
{
    public Task<List<BuildError>> ValidateAsync(string projectPath, CancellationToken ct = default)
    {
        return Task.FromResult(new List<BuildError>());
    }
}

internal sealed class FailingBuildValidator : IBuildValidator
{
    private readonly IReadOnlyList<BuildError> _errors;

    public FailingBuildValidator(params BuildError[] errors)
    {
        _errors = errors.Length > 0
            ? errors
            : new[] { new BuildError("Build", 0, "FAIL", "Build validation failed.") };
    }

    public Task<List<BuildError>> ValidateAsync(string projectPath, CancellationToken ct = default)
    {
        return Task.FromResult(_errors.ToList());
    }
}

internal sealed class RecordingBuildValidator : IBuildValidator
{
    private readonly IReadOnlyList<BuildError> _errors;

    public RecordingBuildValidator(params BuildError[] errors)
    {
        _errors = errors.Length > 0
            ? errors
            : Array.Empty<BuildError>();
    }

    public List<string> ObservedProjectPaths { get; } = new();

    public Task<List<BuildError>> ValidateAsync(string projectPath, CancellationToken ct = default)
    {
        ObservedProjectPaths.Add(projectPath);
        return Task.FromResult(_errors.ToList());
    }
}

internal sealed class NoOpArtifactValidator : IForgeArtifactValidator
{
    public Task<ForgeVerificationResult> ValidateAsync(string projectPath, IReadOnlyList<BuildError> buildErrors, CancellationToken ct = default)
    {
        return Task.FromResult(new ForgeVerificationResult(true, "Artifact validation bypassed for stubbed certification test."));
    }
}
