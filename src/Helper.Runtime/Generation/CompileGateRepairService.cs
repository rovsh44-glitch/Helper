using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed class CompileGateRepairService : ICompileGateRepairService
{
    private readonly CompileGateProjectRepairSet _projectRepairSet;
    private readonly CompileGateSymbolRecoveryRepairSet _symbolRecoveryRepairSet;
    private readonly CompileGateTypeContractRepairSet _typeContractRepairSet;
    private readonly CompileGateMissingReturnRepair _missingReturnRepair;

    public CompileGateRepairService(
        IUsingInferenceService usingInference,
        IMethodBodySemanticGuard semanticGuard)
    {
        _projectRepairSet = new CompileGateProjectRepairSet();
        _symbolRecoveryRepairSet = new CompileGateSymbolRecoveryRepairSet(usingInference, semanticGuard);
        _typeContractRepairSet = new CompileGateTypeContractRepairSet();
        _missingReturnRepair = new CompileGateMissingReturnRepair(semanticGuard);
    }

    public async Task<bool> TryApplyRepairsAsync(
        string compileWorkspace,
        IReadOnlyList<BuildError> errors,
        CancellationToken ct = default)
    {
        var codes = errors
            .Select(CompileGateRepairDiagnostics.ExtractCode)
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.Ordinal);
        var changed = false;

        if (codes.Contains("CS0246"))
        {
            changed |= await _symbolRecoveryRepairSet.ApplyMissingUsingFixAsync(compileWorkspace, errors, ct).ConfigureAwait(false);
            changed |= await _symbolRecoveryRepairSet.ApplyMissingTypeStubFixAsync(compileWorkspace, errors, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS1994"))
        {
            changed |= await _typeContractRepairSet.ApplyInterfaceAsyncFixAsync(compileWorkspace, ct).ConfigureAwait(false);
        }

        if (codes.Overlaps(CompileGateRepairPatterns.UnknownSymbolCodes))
        {
            changed |= await _symbolRecoveryRepairSet.ApplyUnknownSymbolFallbackAsync(compileWorkspace, errors, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS0161"))
        {
            changed |= await _missingReturnRepair.TryApplyAsync(compileWorkspace, errors, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS0535"))
        {
            changed |= await _typeContractRepairSet.ApplyMissingInterfaceMembersFixAsync(compileWorkspace, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS0538"))
        {
            changed |= await _typeContractRepairSet.ApplyInvalidExplicitInterfaceSpecifierFixAsync(compileWorkspace, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS0115"))
        {
            changed |= await _typeContractRepairSet.ApplyInvalidOverrideFixAsync(compileWorkspace, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS1656"))
        {
            changed |= await _typeContractRepairSet.ApplyMethodGroupAssignmentFixAsync(compileWorkspace, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS5001"))
        {
            changed |= await _projectRepairSet.ApplyMissingEntryPointFixAsync(compileWorkspace, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS1983"))
        {
            changed |= await _projectRepairSet.ApplyAsyncTaskReturnTypeFixAsync(compileWorkspace, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS8618"))
        {
            changed |= await _typeContractRepairSet.ApplyNonNullableInitializationFixAsync(compileWorkspace, errors, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS0246") || codes.Contains("CS0234") || codes.Contains("CS0012"))
        {
            changed |= await _projectRepairSet.ApplyMissingPackageReferenceFixAsync(compileWorkspace, errors, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS0103") || codes.Contains("CS1061"))
        {
            changed |= await _typeContractRepairSet.ApplyXamlCodeBehindBindingFixAsync(compileWorkspace, errors, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS0111") || errors.Any(static error =>
                string.Equals(error.Code, "DUPLICATE_SIGNATURE", StringComparison.OrdinalIgnoreCase) ||
                error.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)))
        {
            changed |= await _typeContractRepairSet.ApplyDuplicateSignatureFixAsync(compileWorkspace, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS1729"))
        {
            changed |= await _typeContractRepairSet.ApplyMissingConstructorFixAsync(compileWorkspace, errors, ct).ConfigureAwait(false);
        }

        if (codes.Contains("CS0138"))
        {
            changed |= await _projectRepairSet.ApplyInvalidNamespaceUsingFixAsync(compileWorkspace, errors, ct).ConfigureAwait(false);
        }

        if (codes.Overlaps(CompileGateRepairPatterns.SyntaxNoiseCodes))
        {
            changed |= await _projectRepairSet.ApplyTrailingNarrativeAfterClosingBraceFixAsync(compileWorkspace, errors, ct).ConfigureAwait(false);
        }

        return changed;
    }
}

