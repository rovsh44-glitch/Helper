using System.Text;
using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed class GenerationCompileGate : IGenerationCompileGate
{
    private readonly IDotnetService _dotnetService;
    private readonly ICompileGateRepairService _repairService;
    private readonly CompileGateWorkspacePreparer _workspacePreparer;
    private readonly CompileGateFormatVerifier _formatVerifier;
    private readonly CompileGateFormatMode _formatMode;
    private readonly int _maxRepairIterations;
    private readonly TimeSpan _formatTimeout;

    public GenerationCompileGate(
        IDotnetService dotnetService,
        ICompileGateRepairService repairService,
        CompileGateWorkspacePreparer? workspacePreparer = null,
        CompileGateFormatVerifier? formatVerifier = null)
    {
        _dotnetService = dotnetService;
        _repairService = repairService;
        _workspacePreparer = workspacePreparer ?? new CompileGateWorkspacePreparer();
        _formatVerifier = formatVerifier ?? new CompileGateFormatVerifier();
        _formatMode = TemplatePromotionFeatureProfileService.ReadFormatMode();
        _maxRepairIterations = ReadInt("HELPER_COMPILE_GATE_MAX_REPAIRS", 2, 0, 4);
        _formatTimeout = TimeSpan.FromSeconds(ReadInt("HELPER_COMPILE_GATE_FORMAT_TIMEOUT_SEC", 20, 5, 120));
    }

    public async Task<CompileGateResult> ValidateAsync(string rawProjectRoot, CancellationToken ct = default)
    {
        var compileWorkspace = Path.Combine(rawProjectRoot, ".compile_gate");
        if (Directory.Exists(compileWorkspace))
        {
            Directory.Delete(compileWorkspace, recursive: true);
        }

        Directory.CreateDirectory(compileWorkspace);
        var polyglotProfile = PolyglotCompileGateValidator.DetectProfile(rawProjectRoot);
        if (polyglotProfile.Kind != PolyglotProjectKind.Dotnet)
        {
            var polyglotResult = await PolyglotCompileGateValidator.TryValidateNonDotnetAsync(
                rawProjectRoot,
                compileWorkspace,
                polyglotProfile,
                ct);
            if (polyglotResult is not null)
            {
                return polyglotResult;
            }
        }

        var preparationError = await _workspacePreparer.PrepareAsync(rawProjectRoot, compileWorkspace, ct);
        if (preparationError is not null)
        {
            return new CompileGateResult(
                Success: false,
                Errors: new[] { preparationError },
                CompileWorkspace: compileWorkspace);
        }

        var compileProjectPath = Path.Combine(compileWorkspace, CompileGateWorkspacePreparer.GeneratedProjectFileName);
        var errors = await _dotnetService.BuildAsync(compileWorkspace, compileProjectPath, ct);
        var repairIteration = 0;
        while (errors.Count > 0 && repairIteration < _maxRepairIterations)
        {
            repairIteration++;
            var changed = await _repairService.TryApplyRepairsAsync(compileWorkspace, errors, ct);
            if (!changed)
            {
                break;
            }

            errors = await _dotnetService.BuildAsync(compileWorkspace, compileProjectPath, ct);
        }

        if (errors.Count == 0 && _formatMode != CompileGateFormatMode.Off)
        {
            var formatOk = await _formatVerifier.VerifyAsync(compileWorkspace, _formatTimeout, ct);
            if (!formatOk && _formatMode == CompileGateFormatMode.Strict)
            {
                errors = new List<BuildError>(errors)
                {
                    new("CompileGate", 0, "FORMAT", "dotnet format --verify-no-changes failed for generated package.")
                };
            }
            else if (!formatOk)
            {
                var advisoryPath = Path.Combine(compileWorkspace, "format_advisory.txt");
                await File.WriteAllTextAsync(advisoryPath, "dotnet format verification failed, advisory mode allowed continuation.", Encoding.UTF8, ct);
            }
        }

        return new CompileGateResult(errors.Count == 0, errors, compileWorkspace);
    }

    private static int ReadInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
    }
}

