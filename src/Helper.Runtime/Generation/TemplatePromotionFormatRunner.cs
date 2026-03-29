using System.Diagnostics;
using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

internal sealed class TemplatePromotionFormatRunner
{
    public bool IsFormattingOnlyFailure(IReadOnlyList<BuildError> errors)
    {
        return errors.Count > 0 && errors.All(x => string.Equals(x.Code, "FORMAT", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> TryRunDotnetFormatAsync(string workingDirectory, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "format --severity info",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0;
    }
}

