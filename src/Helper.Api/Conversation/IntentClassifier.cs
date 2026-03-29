using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public sealed record IntentClassification(
    IntentAnalysis Analysis,
    double Confidence,
    string Source,
    IReadOnlyList<string> Signals);

public interface IIntentClassifier
{
    Task<IntentClassification> ClassifyAsync(string message, CancellationToken ct);
}

public sealed class HybridIntentClassifier : IIntentClassifier
{
    private readonly IModelOrchestrator _modelOrchestrator;
    private readonly IChatResiliencePolicy _resilience;
    private readonly ILogger<HybridIntentClassifier> _logger;
    private readonly double _generateMinConfidence;

    public HybridIntentClassifier(
        IModelOrchestrator modelOrchestrator,
        IChatResiliencePolicy resilience,
        ILogger<HybridIntentClassifier> logger)
    {
        _modelOrchestrator = modelOrchestrator;
        _resilience = resilience;
        _logger = logger;
        _generateMinConfidence = ReadDouble("HELPER_CHAT_GENERATE_MIN_CONFIDENCE", 0.70, 0.0, 1.0);
    }

    public async Task<IntentClassification> ClassifyAsync(string message, CancellationToken ct)
    {
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, string.Empty),
                0.0,
                "empty",
                Array.Empty<string>());
        }

        var stats = IntentSignalStats.FromText(text);
        var signals = stats.ToSignals().ToList();
        var ruleIntent = ResolveRuleIntent(stats);

        try
        {
            var modelDecision = await _resilience.ExecuteAsync(
                "intent.analyze",
                retryCt => _modelOrchestrator.AnalyzeIntentAsync(text, retryCt),
                ct);

            if (modelDecision is null)
            {
                return ApplyGenerationAdmission(
                    BuildRuleFallback(text, ruleIntent, stats, signals, "rules"),
                    text);
            }

            signals.Add("model:analyze_intent");
            var confidence = EstimateHybridConfidence(text, stats, modelDecision.Intent, ruleIntent);
            var modelFirst = new IntentClassification(modelDecision, confidence, "model_first", signals);

            if (ruleIntent.HasValue)
            {
                var ruleCandidate = BuildRuleFallback(text, ruleIntent, stats, signals, "hybrid_rules_override");
                if (ShouldPreferRule(ruleCandidate, modelFirst, stats))
                {
                    return ApplyGenerationAdmission(
                        ruleCandidate with
                        {
                            Signals = ruleCandidate.Signals.Concat(new[] { "hybrid:rule_override" }).ToArray()
                        },
                        text);
                }
            }

            return ApplyGenerationAdmission(modelFirst, text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Intent model fallback triggered.");
            return ApplyGenerationAdmission(
                BuildRuleFallback(text, ruleIntent, stats, signals, "rules"),
                text);
        }
    }

    private static IntentType? ResolveRuleIntent(IntentSignalStats stats)
    {
        if (stats.ResearchExplicit && stats.ResearchScore >= stats.GenerateScore)
        {
            return IntentType.Research;
        }

        if (stats.ResearchScore >= Math.Max(2, stats.GenerateScore + 1))
        {
            return IntentType.Research;
        }

        if (stats.GenerateScore >= 3 && stats.GenerateScore >= stats.ResearchScore + 2)
        {
            return IntentType.Generate;
        }

        return null;
    }

    private static IntentClassification BuildRuleFallback(
        string text,
        IntentType? ruleIntent,
        IntentSignalStats stats,
        IReadOnlyList<string> signals,
        string source)
    {
        if (ruleIntent.HasValue)
        {
            var confidence = ruleIntent.Value switch
            {
                IntentType.Research => stats.ResearchExplicit ? 0.9 : 0.78,
                IntentType.Generate => 0.8,
                _ => 0.65
            };

            return new IntentClassification(
                new IntentAnalysis(ruleIntent.Value, string.Empty),
                confidence,
                source == "rules" ? "rules" : source,
                signals);
        }

        return new IntentClassification(
            new IntentAnalysis(IntentType.Unknown, string.Empty),
            text.Length >= 24 ? 0.45 : 0.38,
            "fallback",
            signals.Count == 0 ? new[] { "fallback:default_unknown" } : signals);
    }

    private static bool ShouldPreferRule(IntentClassification ruleCandidate, IntentClassification modelFirst, IntentSignalStats stats)
    {
        if (ruleCandidate.Analysis.Intent == IntentType.Research)
        {
            return stats.ResearchExplicit ||
                   modelFirst.Analysis.Intent != IntentType.Research ||
                   modelFirst.Confidence < 0.72;
        }

        if (ruleCandidate.Analysis.Intent == IntentType.Generate)
        {
            return modelFirst.Analysis.Intent == IntentType.Unknown && modelFirst.Confidence < 0.58;
        }

        return false;
    }

    private static double EstimateHybridConfidence(string text, IntentSignalStats stats, IntentType modelIntent, IntentType? ruleIntent)
    {
        var confidence = 0.56;
        if (text.Length >= 24) confidence += 0.08;
        if (text.Length >= 80) confidence += 0.05;
        if (stats.TotalSignals > 0) confidence += 0.06;
        if (ruleIntent.HasValue && ruleIntent.Value == modelIntent)
        {
            confidence += 0.14;
        }
        else if (ruleIntent.HasValue && modelIntent != IntentType.Unknown && ruleIntent.Value != modelIntent)
        {
            confidence -= 0.16;
        }

        if (modelIntent == IntentType.Unknown)
        {
            confidence -= 0.08;
        }

        return Math.Clamp(confidence, 0.18, 0.93);
    }

    private IntentClassification ApplyGenerationAdmission(IntentClassification classification, string text)
    {
        if (classification.Analysis.Intent != IntentType.Generate)
        {
            return classification;
        }

        if (GenerationAdmissionPolicy.IsGenerateAdmitted(text, classification.Confidence, _generateMinConfidence))
        {
            return classification;
        }

        var downgraded = classification.Analysis with { Intent = IntentType.Unknown };
        var downgradedConfidence = Math.Min(classification.Confidence, 0.46);
        var updatedSignals = classification.Signals
            .Concat(new[] { "generation:admission_denied" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new IntentClassification(downgraded, downgradedConfidence, classification.Source, updatedSignals);
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

    private sealed record IntentSignalStats(
        int StrongResearchHits,
        int WeakResearchHits,
        int CitationHits,
        int GenerateHits)
    {
        public int ResearchScore => (StrongResearchHits * 3) + (CitationHits * 2) + WeakResearchHits;
        public int GenerateScore => GenerateHits * 2;
        public int TotalSignals => StrongResearchHits + WeakResearchHits + CitationHits + GenerateHits;
        public bool ResearchExplicit => StrongResearchHits > 0 || CitationHits > 0;

        public IReadOnlyList<string> ToSignals()
        {
            var signals = new List<string>();
            if (StrongResearchHits > 0) signals.Add($"rule:research_strong={StrongResearchHits}");
            if (WeakResearchHits > 0) signals.Add($"rule:research_weak={WeakResearchHits}");
            if (CitationHits > 0) signals.Add($"rule:citation_hits={CitationHits}");
            if (GenerateHits > 0) signals.Add($"rule:generate_hits={GenerateHits}");
            return signals;
        }

        public static IntentSignalStats FromText(string text)
        {
            return new IntentSignalStats(
                ResearchIntentPolicy.CountStrongResearchSignals(text),
                ResearchIntentPolicy.CountWeakResearchSignals(text),
                ResearchIntentPolicy.CountCitationSignals(text),
                ResearchIntentPolicy.CountGenerateSignals(text));
        }
    }
}

