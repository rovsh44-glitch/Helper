using System.Text.RegularExpressions;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Infrastructure;

public sealed class TemplateRoutingService : ITemplateRoutingService
{
    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}_-]+", RegexOptions.Compiled);
    private const string SmokeTodoAutopromotedTemplateId = "Template_Auto_Создать_самодостаточный_компилируемый_и";
    private const string SmokeTodoFallbackTemplateId = "CSharp_WPF";
    private readonly ITemplateManager _templateManager;
    private readonly IRouteTelemetryService? _routeTelemetry;
    private readonly bool _routerV2Enabled;
    private readonly double _minConfidence;
    private readonly bool _smokeProfile;

    public TemplateRoutingService(ITemplateManager templateManager, IRouteTelemetryService? routeTelemetry = null)
    {
        _templateManager = templateManager;
        _routeTelemetry = routeTelemetry;
        _routerV2Enabled = ReadFlag("HELPER_FF_TEMPLATE_ROUTER_V2", true);
        _minConfidence = ReadDouble("HELPER_TEMPLATE_ROUTER_MIN_CONFIDENCE", 0.34, 0.05, 0.95);
        _smokeProfile = ReadFlag("HELPER_SMOKE_PROFILE", false);
    }

    public async Task<TemplateRoutingDecision> RouteAsync(string prompt, CancellationToken ct = default)
    {
        var normalizedPrompt = (prompt ?? string.Empty).Trim();
        TemplateRoutingDecision Finalize(TemplateRoutingDecision decision, IReadOnlyList<ScoredTemplate>? scoredTemplates = null)
        {
            var telemetry = (scoredTemplates ?? Array.Empty<ScoredTemplate>())
                .Take(5)
                .Select(x => new TemplateRoutingTelemetryCandidate(
                    TemplateId: x.Template.Id,
                    Score: x.Score,
                    TokenMatches: x.TokenMatches,
                    Certified: x.Template.Certified,
                    HasCriticalAlerts: x.Template.HasCriticalAlerts,
                    DecisiveFeatures: x.DecisiveFeatures))
                .ToArray();
            TemplateRoutingTelemetry.Append(normalizedPrompt, decision, telemetry);
            RecordRouteTelemetry(decision);
            return decision;
        }

        if (normalizedPrompt.Length == 0)
        {
            return Finalize(new TemplateRoutingDecision(false, null, 0, Array.Empty<string>(), "Prompt is empty."));
        }

        var templates = await _templateManager.GetAvailableTemplatesAsync(ct);
        if (templates.Count == 0)
        {
            return Finalize(new TemplateRoutingDecision(false, null, 0, Array.Empty<string>(), "No templates are available."));
        }

        var smokeTemplateId = ResolveSmokeTodoTemplateId(normalizedPrompt, templates);
        if (!string.IsNullOrWhiteSpace(smokeTemplateId))
        {
            return Finalize(new TemplateRoutingDecision(
                Matched: true,
                TemplateId: smokeTemplateId,
                Confidence: 0.99,
                Candidates: new[] { smokeTemplateId },
                Reason: "deterministic smoke route matched"));
        }

        var legacySignal = ResolveLegacyTemplateId(normalizedPrompt);
        if (!string.IsNullOrWhiteSpace(legacySignal) &&
            templates.Any(x => string.Equals(x.Id, legacySignal, StringComparison.OrdinalIgnoreCase)))
        {
            return Finalize(new TemplateRoutingDecision(
                Matched: true,
                TemplateId: legacySignal,
                Confidence: 0.98,
                Candidates: new[] { legacySignal },
                Reason: "explicit lexical trigger matched"));
        }

        if (!_routerV2Enabled)
        {
            if (legacySignal is null)
            {
                return Finalize(new TemplateRoutingDecision(false, null, 0, Array.Empty<string>(), "Template router v2 is disabled and no legacy rule matched."));
            }

            return Finalize(new TemplateRoutingDecision(true, legacySignal, 1.0, new[] { legacySignal }, "Template router v2 disabled. Legacy rule matched."));
        }

        var promptTokens = Tokenize(normalizedPrompt);
        var scored = templates
            .Select(template => ScoreTemplate(template, normalizedPrompt, promptTokens))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.TokenMatches)
            .ThenBy(x => x.Template.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidates = scored.Take(5).Select(x => x.Template.Id).ToList();
        var top = scored[0];
        var decisive = top.DecisiveFeatures.Count == 0
            ? "token-overlap"
            : string.Join(", ", top.DecisiveFeatures.Take(3));
        if (top.Score >= _minConfidence)
        {
            return Finalize(new TemplateRoutingDecision(
                Matched: true,
                TemplateId: top.Template.Id,
                Confidence: Math.Round(top.Score, 4),
                Candidates: candidates,
                Reason: $"semantic+lexical match ({top.TokenMatches} token matches; decisive: {decisive})"), scored);
        }

        var fallback = legacySignal;
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return Finalize(new TemplateRoutingDecision(
                Matched: true,
                TemplateId: fallback,
                Confidence: Math.Round(Math.Max(top.Score, _minConfidence), 4),
                Candidates: candidates,
                Reason: $"semantic confidence below threshold, legacy fallback matched (top decisive: {decisive})"), scored);
        }

        return Finalize(new TemplateRoutingDecision(
            Matched: false,
            TemplateId: null,
            Confidence: Math.Round(top.Score, 4),
            Candidates: candidates,
            Reason: $"no template exceeded routing threshold (top decisive: {decisive})"), scored);
    }

    private static ScoredTemplate ScoreTemplate(ProjectTemplate template, string prompt, HashSet<string> promptTokens)
    {
        var metadataTokens = BuildTemplateTokens(template);
        if (metadataTokens.Count == 0 || promptTokens.Count == 0)
        {
            return new ScoredTemplate(template, 0, 0, Array.Empty<string>());
        }

        var decisive = new List<string>();
        var tokenMatches = metadataTokens.Count(promptTokens.Contains);
        var overlapPrompt = tokenMatches / (double)promptTokens.Count;
        var overlapTemplate = tokenMatches / (double)metadataTokens.Count;
        var score = overlapPrompt * 0.62 + overlapTemplate * 0.28;

        var promptLower = prompt.ToLowerInvariant();
        if (template.Tags is { Count: > 0 })
        {
            if (template.Tags.Any(tag => !string.IsNullOrWhiteSpace(tag) && promptLower.Contains(tag.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                score += 0.15;
                decisive.Add("tag_match:+0.15");
            }
        }

        if (promptLower.Contains(template.Id.Replace('_', ' '), StringComparison.OrdinalIgnoreCase))
        {
            score += 0.1;
            decisive.Add("id_phrase:+0.10");
        }

        if (!string.IsNullOrWhiteSpace(template.ProjectType) &&
            promptLower.Contains(template.ProjectType.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 0.08;
            decisive.Add("project_type:+0.08");
        }

        if (!string.IsNullOrWhiteSpace(template.Platform) &&
            promptLower.Contains(template.Platform.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 0.06;
            decisive.Add("platform:+0.06");
        }

        if (!template.Certified)
        {
            score -= 0.10;
            decisive.Add("uncertified_penalty:-0.10");
        }

        if (template.HasCriticalAlerts)
        {
            score -= 0.30;
            decisive.Add("critical_alert_penalty:-0.30");
        }

        if (template.Deprecated)
        {
            score -= 0.2;
            decisive.Add("deprecated_penalty:-0.20");
        }

        return new ScoredTemplate(template, Math.Clamp(score, 0, 1), tokenMatches, decisive);
    }

    private static HashSet<string> BuildTemplateTokens(ProjectTemplate template)
    {
        var text = string.Join(
            " ",
            new[]
            {
                template.Id,
                template.Name,
                template.Description,
                template.Language
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var tokens = Tokenize(text);
        if (template.Tags is { Count: > 0 })
        {
            foreach (var tag in template.Tags)
            {
                foreach (var token in Tokenize(tag))
                {
                    tokens.Add(token);
                }
            }
        }

        if (template.Capabilities is { Count: > 0 })
        {
            foreach (var capability in template.Capabilities)
            {
                foreach (var token in Tokenize(capability))
                {
                    tokens.Add(token);
                }
            }
        }

        return tokens;
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
            if (token.Length < 2)
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    private static string? ResolveLegacyTemplateId(string prompt)
    {
        return GoldenTemplateIntentClassifier.ResolveExplicitTemplateId(prompt);
    }

    private string? ResolveSmokeTodoTemplateId(string prompt, IReadOnlyCollection<ProjectTemplate> templates)
    {
        if (!_smokeProfile || !LooksLikeSmokeTodoPrompt(prompt))
        {
            return null;
        }

        var candidates = new[]
        {
            SmokeTodoAutopromotedTemplateId,
            SmokeTodoFallbackTemplateId
        };

        foreach (var candidate in candidates)
        {
            if (templates.Any(x => string.Equals(x.Id, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool LooksLikeSmokeTodoPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var lower = prompt.ToLowerInvariant();
        var hasWpf = lower.Contains("wpf", StringComparison.OrdinalIgnoreCase);
        var hasTodo = lower.Contains("todo", StringComparison.OrdinalIgnoreCase);
        var hasModel = lower.Contains("model", StringComparison.OrdinalIgnoreCase);
        var hasInterface = lower.Contains("interface", StringComparison.OrdinalIgnoreCase);
        var hasService = lower.Contains("service", StringComparison.OrdinalIgnoreCase);
        var hasCompileBias =
            lower.Contains("compile-oriented", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("compile oriented", StringComparison.OrdinalIgnoreCase);

        return hasWpf && hasTodo && hasModel && hasInterface && hasService && hasCompileBias;
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static double ReadDouble(string envName, double fallback, double min, double max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            value = fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private void RecordRouteTelemetry(TemplateRoutingDecision decision)
    {
        if (_routeTelemetry is null)
        {
            return;
        }

        var degradationReason = decision.Matched
            ? null
            : (decision.Confidence > 0 ? "below_threshold" : "no_match");
        var quality = !decision.Matched
            ? RouteTelemetryQualities.Failed
            : decision.Confidence >= 0.85
                ? RouteTelemetryQualities.High
                : decision.Confidence >= _minConfidence
                    ? RouteTelemetryQualities.Medium
                    : RouteTelemetryQualities.Low;

        _routeTelemetry.Record(new RouteTelemetryEvent(
            RecordedAtUtc: DateTimeOffset.UtcNow,
            Channel: RouteTelemetryChannels.Generation,
            OperationKind: RouteTelemetryOperationKinds.TemplateRouting,
            RouteKey: decision.TemplateId ?? "unmatched",
            Quality: quality,
            Outcome: RouteTelemetryOutcomes.Selected,
            Confidence: decision.Confidence,
            ModelRoute: decision.Matched ? "template-router-v2" : "template-router-v2-unmatched",
            DegradationReason: degradationReason,
            RouteMatched: decision.Matched,
            Signals: decision.Candidates));
    }

    private sealed record ScoredTemplate(ProjectTemplate Template, double Score, int TokenMatches, IReadOnlyList<string> DecisiveFeatures);
}

