using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

internal sealed class CompileGateTypeContractRepairSet
{
    private readonly CompileGateOverrideRepairSet _overrideRepairSet = new();
    private readonly CompileGateMemberContractRepairSet _memberContractRepairSet = new();
    private readonly CompileGateXamlBindingRepairSet _xamlBindingRepairSet = new();
    private readonly CompileGateConstructorRepairSet _constructorRepairSet = new();

    public Task<bool> ApplyInvalidOverrideFixAsync(string compileWorkspace, CancellationToken ct)
        => _overrideRepairSet.ApplyInvalidOverrideFixAsync(compileWorkspace, ct);

    public Task<bool> ApplyInvalidExplicitInterfaceSpecifierFixAsync(string compileWorkspace, CancellationToken ct)
        => _overrideRepairSet.ApplyInvalidExplicitInterfaceSpecifierFixAsync(compileWorkspace, ct);

    public Task<bool> ApplyMethodGroupAssignmentFixAsync(string compileWorkspace, CancellationToken ct)
        => _memberContractRepairSet.ApplyMethodGroupAssignmentFixAsync(compileWorkspace, ct);

    public Task<bool> ApplyXamlCodeBehindBindingFixAsync(
        string compileWorkspace,
        IReadOnlyList<BuildError> errors,
        CancellationToken ct)
        => _xamlBindingRepairSet.ApplyXamlCodeBehindBindingFixAsync(compileWorkspace, errors, ct);

    public Task<bool> ApplyNonNullableInitializationFixAsync(
        string compileWorkspace,
        IReadOnlyList<BuildError> errors,
        CancellationToken ct)
        => _memberContractRepairSet.ApplyNonNullableInitializationFixAsync(compileWorkspace, errors, ct);

    public Task<bool> ApplyMissingInterfaceMembersFixAsync(string compileWorkspace, CancellationToken ct)
        => _memberContractRepairSet.ApplyMissingInterfaceMembersFixAsync(compileWorkspace, ct);

    public Task<bool> ApplyInterfaceAsyncFixAsync(string compileWorkspace, CancellationToken ct)
        => _overrideRepairSet.ApplyInterfaceAsyncFixAsync(compileWorkspace, ct);

    public Task<bool> ApplyDuplicateSignatureFixAsync(string compileWorkspace, CancellationToken ct)
        => _memberContractRepairSet.ApplyDuplicateSignatureFixAsync(compileWorkspace, ct);

    public Task<bool> ApplyMissingConstructorFixAsync(
        string compileWorkspace,
        IReadOnlyList<BuildError> errors,
        CancellationToken ct)
        => _constructorRepairSet.ApplyMissingConstructorFixAsync(compileWorkspace, errors, ct);
}

