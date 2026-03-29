using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Generation;

internal static partial class HelperCliCommandDispatcher
{
    private static async Task<bool> HandleCertificationCommandAsync(string command, string[] commandArgs, string query, HelperCliRuntime runtime, CancellationToken ct)
    {
        switch (command)
        {
            case "certify-parity":
                await HandleCertifyParityAsync(query, runtime, ct);
                return true;
            case "backfill-parity-daily":
                await HandleBackfillParityDailyAsync(commandArgs, runtime, ct);
                return true;
            case "certify-parity-gate":
                await HandleCertifyParityGateAsync(query, runtime, ct);
                return true;
            case "certify-parity-window-gate":
                await HandleCertifyParityWindowGateAsync(commandArgs, runtime, ct);
                return true;
            case "benchmark-generation-parity":
                await HandleBenchmarkGenerationParityAsync(commandArgs, runtime, ct);
                return true;
            case "certify-closed-loop-predictability":
                await HandleClosedLoopPredictabilityAsync(commandArgs, runtime, ct);
                return true;
            default:
                return false;
        }
    }

    private static async Task HandleCertifyParityAsync(string query, HelperCliRuntime runtime, CancellationToken ct)
    {
        var outputPath = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var report = await runtime.ParityCertification.GenerateAsync(outputPath, ct);

        Console.WriteLine("Parity certification snapshot generated:");
        Console.WriteLine($"- Path: {report.ReportPath}");
        Console.WriteLine($"- TotalRuns: {report.TotalRuns}");
        Console.WriteLine($"- GoldenHitRate: {report.GoldenHitRate:P2}");
        Console.WriteLine($"- SuccessRate: {report.GenerationSuccessRate:P2}");
        Console.WriteLine($"- P95ReadySeconds: {report.P95ReadySeconds:0.00}");
        Console.WriteLine($"- UnknownErrorRate: {report.UnknownErrorRate:P2}");
        Console.WriteLine($"- ToolSuccessRatio: {report.ToolSuccessRatio:P2}");
        PrintBlock("Alerts", report.Alerts);
    }

    private static async Task HandleBackfillParityDailyAsync(string[] commandArgs, HelperCliRuntime runtime, CancellationToken ct)
    {
        string? reportPath = null;
        string workload = GenerationWorkloadClassifier.Parity;
        string? start = null;
        string? end = null;
        var overwrite = false;
        var dryRun = false;
        var failIfNoDays = true;

        for (var i = 0; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            switch (arg)
            {
                case "--report":
                    reportPath = RequireNext(commandArgs, ref i, arg);
                    break;
                case "--workload":
                    workload = RequireNext(commandArgs, ref i, arg);
                    break;
                case "--start":
                    start = RequireNext(commandArgs, ref i, arg);
                    break;
                case "--end":
                    end = RequireNext(commandArgs, ref i, arg);
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--allow-empty":
                    failIfNoDays = false;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown argument for backfill-parity-daily: {arg}");
            }
        }

        var report = await runtime.ParityDailyBackfill.BackfillAsync(
            new ParityDailyBackfillRequest(
                ReportPath: reportPath,
                WorkloadClasses: workload,
                StartDateUtc: start,
                EndDateUtc: end,
                OverwriteExisting: overwrite,
                DryRun: dryRun,
                FailIfNoDays: failIfNoDays),
            ct);

        Console.WriteLine("Parity daily backfill:");
        Console.WriteLine($"- ReportPath: {report.ReportPath}");
        Console.WriteLine($"- WorkloadFilter: {report.WorkloadFilter}");
        Console.WriteLine($"- DateRange: {report.StartDateUtc ?? "none"} .. {report.EndDateUtc ?? "none"}");
        Console.WriteLine($"- SourceFiles: {report.SourceFileCount}");
        Console.WriteLine($"- ParsedLines: {report.ParsedLineCount}");
        Console.WriteLine($"- DeduplicatedEntries: {report.DeduplicatedEntryCount}");
        Console.WriteLine($"- FilteredEntries: {report.FilteredEntryCount}");
        Console.WriteLine($"- DaysConsidered: {report.DaysConsidered}");
        Console.WriteLine($"- DaysWritten: {report.DaysWritten}");
        Console.WriteLine($"- DaysSkipped: {report.DaysSkipped}");

        if (report.Days.Count > 0)
        {
            Console.WriteLine("- Day actions:");
            foreach (var day in report.Days)
            {
                Console.WriteLine($"  * {day.DateUtc}: {day.Action} | runs={day.TotalRuns} | success={day.GenerationSuccessRate:P2} | golden={day.GoldenHitRate:P2} | p95={day.P95ReadySeconds:0.00}s");
            }
        }
    }

    private static async Task HandleCertifyParityGateAsync(string query, HelperCliRuntime runtime, CancellationToken ct)
    {
        var outputPath = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var report = await runtime.ParityCertification.GenerateAsync(outputPath, ct);
        var thresholds = ParityGateThresholds.FromEnvironment();
        var decision = runtime.ParityGateEvaluator.Evaluate(report, thresholds);

        Console.WriteLine("Parity KPI gate:");
        Console.WriteLine($"- Passed: {decision.Passed}");
        Console.WriteLine($"- ReportPath: {report.ReportPath}");
        Console.WriteLine($"- GoldenHitRate: {report.GoldenHitRate:P2} (threshold >= {thresholds.MinGoldenHitRate:P2})");
        Console.WriteLine($"- SuccessRate: {report.GenerationSuccessRate:P2} (threshold >= {thresholds.MinGenerationSuccessRate:P2})");
        Console.WriteLine($"- P95ReadySeconds: {report.P95ReadySeconds:0.00} (threshold <= {thresholds.MaxP95ReadySeconds:0.00})");
        Console.WriteLine($"- UnknownErrorRate: {report.UnknownErrorRate:P2} (threshold <= {thresholds.MaxUnknownErrorRate:P2})");
        Console.WriteLine($"- ToolSuccessRatio: {report.ToolSuccessRatio:P2} (threshold >= {thresholds.MinToolSuccessRatio:P2})");
        PrintBlock("Violations", decision.Violations);

        if (!decision.Passed)
        {
            Environment.ExitCode = 1;
        }
    }

    private static async Task HandleCertifyParityWindowGateAsync(string[] commandArgs, HelperCliRuntime runtime, CancellationToken ct)
    {
        var outputPath = commandArgs.Length > 0 && !string.IsNullOrWhiteSpace(commandArgs[0]) ? commandArgs[0] : null;
        var windowDays = 7;
        if (commandArgs.Length > 1 && int.TryParse(commandArgs[1], out var parsedWindow))
        {
            windowDays = Math.Clamp(parsedWindow, 1, 30);
        }

        var report = await runtime.ParityWindowGate.EvaluateAsync(windowDays, outputPath, ct);
        Console.WriteLine("Parity window gate:");
        Console.WriteLine($"- Passed: {report.Passed}");
        Console.WriteLine($"- WindowDays: {report.WindowDays}");
        Console.WriteLine($"- AvailableDays: {report.AvailableDays}");
        Console.WriteLine($"- WindowComplete: {report.WindowComplete}");
        Console.WriteLine($"- ReportPath: {report.ReportPath}");
        PrintBlock("Violations", report.Violations);

        if (!report.Passed)
        {
            Environment.ExitCode = 1;
        }
    }

    private static async Task HandleBenchmarkGenerationParityAsync(string[] commandArgs, HelperCliRuntime runtime, CancellationToken ct)
    {
        var goldenPath = commandArgs.Length > 0 && !string.IsNullOrWhiteSpace(commandArgs[0]) ? commandArgs[0] : Path.Combine("eval", "golden_template_prompts_ru_en.jsonl");
        var incidentPath = commandArgs.Length > 1 && !string.IsNullOrWhiteSpace(commandArgs[1]) ? commandArgs[1] : Path.Combine("eval", "incident_corpus.jsonl");
        var outputPath = commandArgs.Length > 2 && !string.IsNullOrWhiteSpace(commandArgs[2]) ? commandArgs[2] : null;

        var report = await runtime.GenerationParityBenchmark.RunAsync(goldenPath, incidentPath, outputPath, ct);
        Console.WriteLine("Generation parity benchmark:");
        Console.WriteLine($"- Passed: {report.Passed}");
        Console.WriteLine($"- ReportPath: {report.ReportPath}");
        Console.WriteLine($"- GoldenCaseCount: {report.GoldenCaseCount}");
        Console.WriteLine($"- GoldenFamilyCount: {report.GoldenFamilyCount}");
        Console.WriteLine($"- GoldenHitRate: {report.GoldenHitRate:P2}");
        Console.WriteLine($"- IncidentCaseCount: {report.IncidentCaseCount}");
        Console.WriteLine($"- IncidentErrorCodeCount: {report.IncidentErrorCodeCount}");
        Console.WriteLine($"- IncidentRootCauseClassCount: {report.IncidentRootCauseClassCount}");
        Console.WriteLine($"- RootCausePrecision: {report.RootCausePrecision:P2}");
        Console.WriteLine($"- UnknownErrorRate: {report.UnknownErrorRate:P2}");
        Console.WriteLine($"- DeterministicAutofixCoverageRate: {report.DeterministicAutofixCoverageRate:P2}");
        PrintBlock("Violations", report.Violations);

        if (!report.Passed)
        {
            Environment.ExitCode = 1;
        }
    }

    private static async Task HandleClosedLoopPredictabilityAsync(string[] commandArgs, HelperCliRuntime runtime, CancellationToken ct)
    {
        var incidentPath = commandArgs.Length > 0 && !string.IsNullOrWhiteSpace(commandArgs[0]) ? commandArgs[0] : Path.Combine("eval", "incident_corpus.jsonl");
        var outputPath = commandArgs.Length > 1 && !string.IsNullOrWhiteSpace(commandArgs[1]) ? commandArgs[1] : null;

        var report = await runtime.ClosedLoopPredictability.EvaluateAsync(incidentPath, outputPath, ct);
        Console.WriteLine("Closed-loop predictability:");
        Console.WriteLine($"- Passed: {report.Passed}");
        Console.WriteLine($"- ReportPath: {report.ReportPath}");
        Console.WriteLine($"- TopIncidentClasses: {report.TopIncidentClasses}");
        Console.WriteLine($"- RepeatsPerClass: {report.RepeatsPerClass}");
        Console.WriteLine($"- MaxAllowedVariance: {report.MaxAllowedVariance:P2}");
        PrintBlock("Violations", report.Violations);

        if (!report.Passed)
        {
            Environment.ExitCode = 1;
        }
    }

    private static string RequireNext(string[] commandArgs, ref int index, string arg)
    {
        if (index + 1 >= commandArgs.Length)
        {
            throw new InvalidOperationException($"Missing value for {arg}.");
        }

        return commandArgs[++index];
    }
}

