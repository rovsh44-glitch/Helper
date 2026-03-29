using System.Text;
using System.Text.Json;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed class GenerationValidationReportWriter : IGenerationValidationReportWriter
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task WriteAsync(GenerationRunReport report, CancellationToken ct = default)
    {
        var reportPath = Path.Combine(report.RawProjectRoot, "validation_report.json");
        var logsPath = HelperWorkspacePathResolver.ResolveProjectRunLogPath(report.RawProjectRoot);

        var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var line = JsonSerializer.Serialize(report);

        await _lock.WaitAsync(ct);
        try
        {
            var reportDirectory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(reportDirectory))
            {
                Directory.CreateDirectory(reportDirectory);
            }

            var logDirectory = Path.GetDirectoryName(logsPath);
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            await File.WriteAllTextAsync(reportPath, reportJson, Encoding.UTF8, ct);
            await File.AppendAllTextAsync(logsPath, line + Environment.NewLine, Encoding.UTF8, ct);
        }
        finally
        {
            _lock.Release();
        }
    }
}

