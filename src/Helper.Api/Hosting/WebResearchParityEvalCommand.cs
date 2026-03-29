using Helper.Api.Conversation;

namespace Helper.Api.Hosting;

public static class WebResearchParityEvalCommand
{
    public static async Task<bool> TryHandleAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || !string.Equals(args[0], "--export-web-research-eval", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var helperRoot = ApiProgramHelpers.DiscoverHelperRoot(AppContext.BaseDirectory);
        var packageRoot = ResolveUnderHelperRoot(
            args.Length > 1 ? args[1] : null,
            helperRoot,
            Path.Combine("eval", "web_research_parity"));
        var outputDirectory = ResolveUnderHelperRoot(
            args.Length > 2 ? args[2] : null,
            helperRoot,
            Path.Combine("artifacts", "eval", "web_research_parity"));
        var minPreparedRuns = args.Length > 3 && int.TryParse(args[3], out var parsedMinPreparedRuns) && parsedMinPreparedRuns > 0
            ? parsedMinPreparedRuns
            : 240;

        var service = new WebResearchParityEvalService();
        var result = await service
            .ExportAsync(
                packageRoot,
                outputDirectory,
                new WebResearchParityEvalOptions(MinPreparedRuns: minPreparedRuns),
                ct)
            .ConfigureAwait(false);

        Console.WriteLine($"[WebResearchEval] GateStatus={result.Package.Summary.GateStatus}");
        Console.WriteLine($"[WebResearchEval] Json={result.JsonPath}");
        Console.WriteLine($"[WebResearchEval] Markdown={result.MarkdownPath}");
        return true;
    }

    private static string ResolveUnderHelperRoot(string? candidate, string helperRoot, string fallbackRelative)
    {
        var raw = string.IsNullOrWhiteSpace(candidate) ? fallbackRelative : candidate.Trim();
        if (!Path.IsPathRooted(raw))
        {
            raw = Path.Combine(helperRoot, raw);
        }

        return Path.GetFullPath(raw);
    }
}

