using System.Text;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed class GenerationHealthReporter : IGenerationHealthReporter
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _workspaceRoot;

    public GenerationHealthReporter()
    {
        _workspaceRoot = HelperWorkspacePathResolver.ResolveHelperRoot();
    }

    public async Task AppendAsync(GenerationRunReport report, CancellationToken ct = default)
    {
        var docDir = Path.Combine(_workspaceRoot, "doc");
        Directory.CreateDirectory(docDir);

        var dayPath = Path.Combine(docDir, $"generation_health_{DateTime.UtcNow:yyyy-MM-dd}.md");
        var row =
            $"| {DateTime.UtcNow:HH:mm:ss} | {report.RunId} | {Escape(report.ProjectName)} | {(report.BlueprintAccepted ? "pass" : "fail")} | {(report.CompileGatePassed ? "pass" : "fail")} | {report.RetryCount} | {report.Errors.Count} |";

        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(dayPath))
            {
                var header = new StringBuilder();
                header.AppendLine($"# Generation Health Report ({DateTime.UtcNow:yyyy-MM-dd})");
                header.AppendLine();
                header.AppendLine("| UTC Time | Run Id | Project | Blueprint | Compile Gate | Retries | Errors |");
                header.AppendLine("|---|---|---|---|---|---:|---:|");
                await File.WriteAllTextAsync(dayPath, header.ToString(), Encoding.UTF8, ct);
            }

            await File.AppendAllTextAsync(dayPath, row + Environment.NewLine, Encoding.UTF8, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}

