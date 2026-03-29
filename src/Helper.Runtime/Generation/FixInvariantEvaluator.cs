using System.Diagnostics;
using System.Text.RegularExpressions;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed class FixInvariantEvaluator : IFixInvariantEvaluator
{
    private static readonly Regex PublicTypeRegex = new(
        @"\bpublic\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*(class|interface|record|struct)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PublicMethodRegex = new(
        @"\bpublic\s+(?:async\s+)?(?:static\s+)?[A-Za-z0-9_<>\[\]\?,\.\s]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<args>[^\)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TokenRegex = new(
        @"[\p{L}\p{N}_-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> StopTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "with", "from", "into", "that", "this", "have", "were", "will", "your", "для", "чтобы",
        "если", "тогда", "вариант", "compile", "output", "mode"
    };

    public async Task<FixInvariantEvaluationResult> EvaluateAsync(
        GenerationRequest request,
        GenerationResult baseline,
        FixStrategyKind strategy,
        FixVerificationResult verification,
        CancellationToken ct = default)
    {
        var violations = new List<string>();
        var baselineSymbols = ExtractPublicSymbols(baseline.Files);
        var currentSymbols = ExtractPublicSymbols(verification.Files);

        if (baselineSymbols.Count > 0 && currentSymbols.Count > 0)
        {
            var removed = baselineSymbols.Except(currentSymbols, StringComparer.Ordinal).ToList();
            var removedRatio = removed.Count / (double)Math.Max(1, baselineSymbols.Count);
            var maxRemovedRatio = ReadDouble("HELPER_FIX_INVARIANT_MAX_SYMBOL_REMOVAL_RATIO", 0.15, 0.0, 1.0);
            if (removedRatio > maxRemovedRatio)
            {
                violations.Add($"invariant.api_contract_removed_symbols ratio={removedRatio:0.000}");
            }
        }

        var intentPreservationScore = ComputeIntentPreservationScore(request.Prompt, verification.Files);
        var minIntentScore = ReadDouble("HELPER_FIX_MIN_INTENT_PRESERVATION_SCORE", 0.35, 0.0, 1.0);
        if (intentPreservationScore < minIntentScore)
        {
            violations.Add($"invariant.intent_drift score={intentPreservationScore:0.000}");
        }

        var testsPassed = true;
        if (ReadFlag("HELPER_FIX_INVARIANTS_RUN_TESTS", false))
        {
            testsPassed = await TryRunTestsAsync(verification.ProjectPath, ct);
            if (!testsPassed)
            {
                violations.Add("invariant.critical_tests_failed");
            }
        }

        var regressionDetected = verification.RegressionDetected;
        if (regressionDetected)
        {
            violations.Add("invariant.regression_detected");
        }

        // L3 changes require stricter intent score to prevent semantic stubs from passing as "fixed".
        if (strategy == FixStrategyKind.LlmAutoHealer && intentPreservationScore < 0.55)
        {
            violations.Add("invariant.l3_intent_threshold_failed");
        }

        var passed = violations.Count == 0;
        return new FixInvariantEvaluationResult(
            Passed: passed,
            IntentPreservationScore: Math.Clamp(intentPreservationScore, 0.0, 1.0),
            RegressionDetected: regressionDetected,
            TestsPassed: testsPassed,
            Violations: violations);
    }

    private static HashSet<string> ExtractPublicSymbols(IReadOnlyList<GeneratedFile> files)
    {
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (!file.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match match in PublicTypeRegex.Matches(file.Content))
            {
                var name = match.Groups["name"].Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    symbols.Add($"type:{name}");
                }
            }

            foreach (Match match in PublicMethodRegex.Matches(file.Content))
            {
                var name = match.Groups["name"].Value;
                var args = match.Groups["args"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    symbols.Add($"method:{name}({NormalizeArguments(args)})");
                }
            }
        }

        return symbols;
    }

    private static string NormalizeArguments(string rawArgs)
    {
        if (string.IsNullOrWhiteSpace(rawArgs))
        {
            return string.Empty;
        }

        var segments = rawArgs
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "_")
            .ToArray();
        return string.Join(",", segments);
    }

    private static double ComputeIntentPreservationScore(string prompt, IReadOnlyList<GeneratedFile> files)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return 1.0;
        }

        var promptTokens = Tokenize(prompt);
        if (promptTokens.Count == 0)
        {
            return 1.0;
        }

        var content = string.Join(
            "\n",
            files
                .Where(x => x.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                            x.RelativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                            x.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Content));
        var currentTokens = Tokenize(content);
        if (currentTokens.Count == 0)
        {
            return 0.0;
        }

        var overlap = promptTokens.Intersect(currentTokens, StringComparer.OrdinalIgnoreCase).Count();
        return overlap / (double)promptTokens.Count;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return tokens;
        }

        foreach (Match match in TokenRegex.Matches(text.ToLowerInvariant()))
        {
            var token = match.Value.Trim();
            if (token.Length < 4 || StopTokens.Contains(token))
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    private static async Task<bool> TryRunTestsAsync(string projectPath, CancellationToken ct)
    {
        var timeoutSeconds = ReadInt("HELPER_FIX_INVARIANT_TEST_TIMEOUT_SEC", 120, 30, 900);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "test --nologo --verbosity quiet",
            WorkingDirectory = Directory.Exists(projectPath) ? projectPath : HelperWorkspacePathResolver.ResolveHelperRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();
            await process.WaitForExitAsync(linked.Token);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static int ReadInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static double ReadDouble(string envName, double fallback, double min, double max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
    }
}

