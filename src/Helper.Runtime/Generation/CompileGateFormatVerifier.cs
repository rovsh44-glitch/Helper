using System.Diagnostics;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed class CompileGateFormatVerifier
{
    public async Task<bool> VerifyAsync(string workingDirectory, TimeSpan timeout, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = DotnetSandboxSupport.AppendSandboxIntermediateProperties(
                    "format --verify-no-changes --severity info",
                    workingDirectory),
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return process.ExitCode == 0;
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
                // ignore kill failures, caller handles format as non-success
            }

            return false;
        }
    }
}

