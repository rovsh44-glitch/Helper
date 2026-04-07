namespace Helper.Api.Backend.Providers;

public sealed class ProviderRecommendationPolicy : IProviderRecommendationPolicy
{
    public ProviderRecommendationResult Recommend(ProviderRecommendationRequest request, IReadOnlyList<ProviderProfileSummary> candidates)
    {
        var viable = candidates
            .Where(summary => summary.Profile.Enabled && summary.Validation.IsValid)
            .ToArray();
        if (viable.Length == 0)
        {
            return new ProviderRecommendationResult(
                RecommendedProfileId: null,
                AlternativeProfileIds: Array.Empty<string>(),
                ReasonCodes: new[] { "no_viable_profiles" },
                Warnings: new[] { "No enabled and valid provider profiles are available." });
        }

        var ranked = viable
            .Select(summary => new
            {
                Summary = summary,
                Score = Score(summary, request),
                Reasons = BuildReasons(summary, request)
            })
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Summary.Profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var recommended = ranked[0];
        return new ProviderRecommendationResult(
            recommended.Summary.Profile.Id,
            ranked.Skip(1).Take(3).Select(entry => entry.Summary.Profile.Id).ToArray(),
            recommended.Reasons,
            BuildWarnings(request, ranked.Select(entry => entry.Summary).ToArray()));
    }

    private static int Score(ProviderProfileSummary summary, ProviderRecommendationRequest request)
    {
        var score = 0;
        if (request.PreferLocal == summary.Profile.IsLocal)
        {
            score += 4;
        }

        if (request.NeedVision && summary.Capabilities.SupportsVision)
        {
            score += 5;
        }

        if (!request.NeedVision)
        {
            score += 1;
        }

        score += request.Goal.Trim().ToLowerInvariant() switch
        {
            "local_fast" when summary.Profile.SupportedGoals.Contains(ProviderWorkloadGoal.LocalFast) => 6,
            "local_coder" when summary.Profile.SupportedGoals.Contains(ProviderWorkloadGoal.LocalCoder) => 6,
            "hosted_reasoning" when summary.Profile.SupportedGoals.Contains(ProviderWorkloadGoal.HostedReasoning) => 6,
            "research_verified" when summary.Profile.SupportedGoals.Contains(ProviderWorkloadGoal.ResearchVerified) => 6,
            "privacy_first" when summary.Profile.SupportedGoals.Contains(ProviderWorkloadGoal.PrivacyFirst) => 6,
            _ => 0
        };

        score += request.CodingIntensity.Trim().ToLowerInvariant() switch
        {
            "heavy" when summary.Capabilities.SupportsCoder => 4,
            "medium" when summary.Capabilities.SupportsCoder => 2,
            _ => 0
        };

        score += request.LatencyPreference.Trim().ToLowerInvariant() switch
        {
            "low" when summary.Capabilities.SupportsFast => 3,
            "quality" when summary.Capabilities.SupportsReasoning => 3,
            _ => 1
        };

        return score;
    }

    private static IReadOnlyList<string> BuildReasons(ProviderProfileSummary summary, ProviderRecommendationRequest request)
    {
        var reasons = new List<string>();
        if (request.PreferLocal == summary.Profile.IsLocal)
        {
            reasons.Add(summary.Profile.IsLocal ? "locality_match" : "remote_preference_match");
        }

        if (request.NeedVision && summary.Capabilities.SupportsVision)
        {
            reasons.Add("vision_supported");
        }

        if (summary.Capabilities.SupportsCoder && request.CodingIntensity is "medium" or "heavy")
        {
            reasons.Add("coder_supported");
        }

        if (summary.Capabilities.SupportsReasoning)
        {
            reasons.Add("reasoning_supported");
        }

        reasons.Add($"goal:{request.Goal.Trim().ToLowerInvariant()}");
        reasons.Add($"profile:{summary.Profile.Id}");
        return reasons;
    }

    private static IReadOnlyList<string> BuildWarnings(ProviderRecommendationRequest request, IReadOnlyList<ProviderProfileSummary> ranked)
    {
        var warnings = new List<string>();
        if (request.NeedVision && ranked.All(summary => !summary.Capabilities.SupportsVision))
        {
            warnings.Add("No available provider profile advertises vision support.");
        }

        if (request.PreferLocal && ranked.All(summary => !summary.Profile.IsLocal))
        {
            warnings.Add("No local provider profile is currently available.");
        }

        return warnings;
    }
}
