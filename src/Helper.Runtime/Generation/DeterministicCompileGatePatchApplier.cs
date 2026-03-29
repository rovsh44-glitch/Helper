using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed class DeterministicCompileGatePatchApplier : IFixPatchApplier
{
    private readonly IGenerationCompileGate _compileGate;

    public DeterministicCompileGatePatchApplier(IGenerationCompileGate compileGate)
    {
        _compileGate = compileGate;
    }

    public FixStrategyKind Strategy => FixStrategyKind.DeterministicCompileGate;

    public async Task<FixPatchApplyResult> ApplyAsync(
        FixPatchApplyContext context,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(context.CurrentResult.ProjectPath))
        {
            return new FixPatchApplyResult(
                Applied: false,
                Success: false,
                Errors: context.CurrentResult.Errors,
                ChangedFiles: Array.Empty<string>(),
                Notes: "Project path does not exist for deterministic patch.");
        }

        var gateResult = await _compileGate.ValidateAsync(context.CurrentResult.ProjectPath, ct);
        var changedFiles = await CompileWorkspaceSynchronizer.SyncPatchedCodeFilesAsync(
            gateResult.CompileWorkspace,
            context.CurrentResult.ProjectPath,
            ct);

        var notes = gateResult.Success
            ? "Compile gate repair completed successfully."
            : $"Compile gate repair completed with {gateResult.Errors.Count} remaining error(s).";

        return new FixPatchApplyResult(
            Applied: changedFiles.Count > 0,
            Success: gateResult.Success,
            Errors: gateResult.Errors,
            ChangedFiles: changedFiles,
            Notes: notes);
    }
}

