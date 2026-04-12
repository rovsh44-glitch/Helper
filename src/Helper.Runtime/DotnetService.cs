using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public class DotnetService : IDotnetService
{
    public async Task RestoreAsync(string workingDirectory, CancellationToken ct = default)
    {
        var arguments = DotnetSandboxSupport.AppendSandboxIntermediateProperties(
            @"restore --disable-build-servers -p:UseSharedCompilation=false -nr:false",
            workingDirectory);
        var result = await DotnetProcessRunner.RunAsync(DotnetOperationKind.Restore, arguments, workingDirectory, target: null, ct).ConfigureAwait(false);
        DotnetProcessResultMapper.EnsureRestoreSucceeded(result, DotnetOperationKind.Restore, target: null, ct);
    }

    public async Task<List<BuildError>> BuildAsync(string workingDirectory, CancellationToken ct = default)
    {
        return await BuildAsync(workingDirectory, allowRecursiveDiscovery: false, ct).ConfigureAwait(false);
    }

    public async Task<List<BuildError>> BuildAsync(string workingDirectory, bool allowRecursiveDiscovery, CancellationToken ct = default)
    {
        var resolution = DotnetBuildTargetResolver.Resolve(workingDirectory, allowRecursiveDiscovery);
        if (!resolution.Succeeded)
        {
            return resolution.ToBuildErrors();
        }

        return await BuildCoreAsync(workingDirectory, resolution.TargetPath!, ct).ConfigureAwait(false);
    }

    public async Task<List<BuildError>> BuildAsync(string workingDirectory, string targetPath, CancellationToken ct = default)
    {
        var resolution = DotnetBuildTargetResolver.ResolveExplicit(workingDirectory, targetPath);
        if (!resolution.Succeeded)
        {
            return resolution.ToBuildErrors();
        }

        return await BuildCoreAsync(workingDirectory, resolution.TargetPath!, ct).ConfigureAwait(false);
    }

    private static async Task<List<BuildError>> BuildCoreAsync(string workingDirectory, string target, CancellationToken ct)
    {
        var arguments = DotnetSandboxSupport.AppendSandboxIntermediateProperties(
            $@"build ""{target}"" -c Debug --no-incremental --disable-build-servers -p:RestoreAudit=false -p:UseSharedCompilation=false -nr:false",
            workingDirectory);
        var result = await DotnetProcessRunner.RunAsync(DotnetOperationKind.Build, arguments, workingDirectory, target, ct).ConfigureAwait(false);
        return DotnetProcessResultMapper.MapBuildResult(result, target, ct);
    }

    public async Task<TestReport> TestAsync(string workingDirectory, CancellationToken ct = default)
    {
        var arguments = DotnetSandboxSupport.AppendSandboxIntermediateProperties(
            @"test --logger ""console;verbosity=normal"" --disable-build-servers -p:UseSharedCompilation=false -nr:false",
            workingDirectory);
        var result = await DotnetProcessRunner.RunAsync(DotnetOperationKind.Test, arguments, workingDirectory, target: null, ct).ConfigureAwait(false);
        return DotnetProcessResultMapper.MapTestResult(result, ct);
    }
}
