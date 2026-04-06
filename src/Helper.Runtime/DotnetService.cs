using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public class DotnetService : IDotnetService
{
    public async Task RestoreAsync(string workingDirectory, CancellationToken ct = default)
    {
        var arguments = DotnetSandboxSupport.AppendSandboxIntermediateProperties("restore", workingDirectory);
        var result = await DotnetProcessRunner.RunAsync(DotnetOperationKind.Restore, arguments, workingDirectory, target: null, ct).ConfigureAwait(false);
        DotnetProcessResultMapper.EnsureRestoreSucceeded(result, DotnetOperationKind.Restore, target: null, ct);
    }

    public async Task<List<BuildError>> BuildAsync(string workingDirectory, CancellationToken ct = default)
    {
        var sln = Directory.GetFiles(workingDirectory, "*.sln", SearchOption.AllDirectories).FirstOrDefault();
        var csproj = Directory.GetFiles(workingDirectory, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
        var target = sln ?? csproj;
        if (target is null)
        {
            return new List<BuildError>();
        }

        var arguments = DotnetSandboxSupport.AppendSandboxIntermediateProperties(
            $@"build ""{target}"" -c Debug --no-incremental -p:RestoreAudit=false",
            workingDirectory);
        var result = await DotnetProcessRunner.RunAsync(DotnetOperationKind.Build, arguments, workingDirectory, target, ct).ConfigureAwait(false);
        return DotnetProcessResultMapper.MapBuildResult(result, target, ct);
    }

    public async Task<TestReport> TestAsync(string workingDirectory, CancellationToken ct = default)
    {
        var arguments = DotnetSandboxSupport.AppendSandboxIntermediateProperties(
            @"test --logger ""console;verbosity=normal""",
            workingDirectory);
        var result = await DotnetProcessRunner.RunAsync(DotnetOperationKind.Test, arguments, workingDirectory, target: null, ct).ConfigureAwait(false);
        return DotnetProcessResultMapper.MapTestResult(result, ct);
    }
}
