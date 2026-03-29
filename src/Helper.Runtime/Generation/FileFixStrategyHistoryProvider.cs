using System.Text.Json;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed class FileFixStrategyHistoryProvider : IFixStrategyHistoryProvider
{
    private readonly string _root;

    public FileFixStrategyHistoryProvider(string? root = null)
    {
        _root = string.IsNullOrWhiteSpace(root)
            ? HelperWorkspacePathResolver.ResolveLogsPath(Path.Combine("fix_attempts"))
            : Path.GetFullPath(root);
    }

    public IReadOnlyDictionary<FixStrategyKind, double> GetWinRates()
    {
        var priors = new Dictionary<FixStrategyKind, double>
        {
            [FixStrategyKind.DeterministicCompileGate] = 0.78,
            [FixStrategyKind.RuntimeConfig] = 0.62,
            [FixStrategyKind.Regenerate] = 0.55,
            [FixStrategyKind.LlmAutoHealer] = 0.48
        };

        var totals = priors.Keys.ToDictionary(x => x, _ => 0L);
        var wins = priors.Keys.ToDictionary(x => x, _ => 0L);

        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "attempt_*.json", SearchOption.AllDirectories))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var root = doc.RootElement;
                    var strategyRaw = root.TryGetProperty("Strategy", out var strategyNode)
                        ? strategyNode.GetString()
                        : null;
                    var success = root.TryGetProperty("Success", out var successNode) && successNode.GetBoolean();
                    if (!Enum.TryParse<FixStrategyKind>(strategyRaw, ignoreCase: true, out var strategy))
                    {
                        continue;
                    }

                    totals[strategy]++;
                    if (success)
                    {
                        wins[strategy]++;
                    }
                }
                catch
                {
                    // ignore malformed historical rows
                }
            }
        }

        // Beta-like smoothing to avoid unstable ordering for sparse logs.
        const double priorWeight = 4.0;
        var result = new Dictionary<FixStrategyKind, double>();
        foreach (var strategy in priors.Keys)
        {
            var numerator = wins[strategy] + (priors[strategy] * priorWeight);
            var denominator = totals[strategy] + priorWeight;
            result[strategy] = denominator <= 0 ? priors[strategy] : numerator / denominator;
        }

        return result;
    }
}

