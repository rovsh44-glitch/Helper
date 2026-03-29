using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Swarm;

internal sealed record TumenRunReportContext(
    string RunId,
    string Prompt,
    string ProjectName,
    DateTime StartedAtUtc,
    string RawProjectRoot,
    string? ValidatedProjectRoot,
    int FileCount,
    int MethodCount,
    int RetryCount,
    bool BlueprintAccepted,
    bool CompileGatePassed,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    TemplateRoutingDecision? RouteDecision,
    bool GoldenTemplateEligible,
    string WorkloadClass,
    IReadOnlyDictionary<string, double> StageDurationsSec,
    IReadOnlyList<string>? PlaceholderFindings = null);

internal sealed class TumenRunReportService
{
    private readonly IGenerationValidationReportWriter _reportWriter;
    private readonly IGenerationHealthReporter _healthReporter;

    public TumenRunReportService(
        IGenerationValidationReportWriter reportWriter,
        IGenerationHealthReporter healthReporter)
    {
        _reportWriter = reportWriter;
        _healthReporter = healthReporter;
    }

    public GenerationRunReport Build(TumenRunReportContext context)
    {
        return new GenerationRunReport(
            RunId: context.RunId,
            Prompt: context.Prompt,
            ModelRoute: "reasoning/coder/fast",
            ProjectName: context.ProjectName,
            StartedAtUtc: context.StartedAtUtc,
            CompletedAtUtc: DateTime.UtcNow,
            RawProjectRoot: context.RawProjectRoot,
            ValidatedProjectRoot: context.ValidatedProjectRoot,
            FileCount: context.FileCount,
            MethodCount: context.MethodCount,
            RetryCount: context.RetryCount,
            BlueprintAccepted: context.BlueprintAccepted,
            CompileGatePassed: context.CompileGatePassed,
            Errors: context.Errors,
            Warnings: context.Warnings,
            RouteMatched: context.RouteDecision?.Matched,
            RoutedTemplateId: context.RouteDecision?.TemplateId,
            RouteConfidence: context.RouteDecision?.Confidence,
            GoldenTemplateMatched: context.RouteDecision?.Matched,
            GoldenTemplateEligible: context.GoldenTemplateEligible,
            WorkloadClass: context.WorkloadClass,
            StageDurationsSec: context.StageDurationsSec,
            PlaceholderFindings: context.PlaceholderFindings);
    }

    public async Task PersistAsync(GenerationRunReport report, CancellationToken ct)
    {
        await _reportWriter.WriteAsync(report, ct);
        await _healthReporter.AppendAsync(report, ct);
    }

    public async Task PersistSafelyAsync(GenerationRunReport report)
    {
        try
        {
            await _reportWriter.WriteAsync(report, CancellationToken.None);
            await _healthReporter.AppendAsync(report, CancellationToken.None);
        }
        catch
        {
            // do not shadow root exception
        }
    }

    public static string ResolveProjectName(GenerationRunContext? runContext, SwarmBlueprint? blueprint)
    {
        if (!string.IsNullOrWhiteSpace(runContext?.RawProjectRoot))
        {
            var resolved = Path.GetFileName(runContext.RawProjectRoot);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return blueprint?.ProjectName ?? "UnknownProject";
    }
}

