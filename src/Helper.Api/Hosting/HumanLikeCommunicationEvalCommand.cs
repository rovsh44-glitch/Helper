using Helper.Api.Conversation;

namespace Helper.Api.Hosting;

public static class HumanLikeCommunicationEvalCommand
{
    public static async Task<bool> TryHandleAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || !string.Equals(args[0], "--export-human-like-eval", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var helperRoot = ApiProgramHelpers.DiscoverHelperRoot(AppContext.BaseDirectory);
        var packageRoot = ResolveUnderHelperRoot(
            args.Length > 1 ? args[1] : null,
            helperRoot,
            Path.Combine("eval", "human_like_communication"));
        var outputDirectory = ResolveUnderHelperRoot(
            args.Length > 2 ? args[2] : null,
            helperRoot,
            Path.Combine("artifacts", "eval", "human_like_communication"));
        var minPreparedRuns = args.Length > 3 && int.TryParse(args[3], out var parsedMinPreparedRuns) && parsedMinPreparedRuns > 0
            ? parsedMinPreparedRuns
            : 240;

        var service = new HumanLikeCommunicationEvalService();
        var result = await service
            .ExportAsync(
                packageRoot,
                outputDirectory,
                new HumanLikeCommunicationEvalOptions(MinPreparedRuns: minPreparedRuns),
                ct)
            .ConfigureAwait(false);

        Console.WriteLine($"[HumanLikeEval] GateStatus={result.Package.Summary.GateStatus}");
        Console.WriteLine($"[HumanLikeEval] Json={result.JsonPath}");
        Console.WriteLine($"[HumanLikeEval] Markdown={result.MarkdownPath}");
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

