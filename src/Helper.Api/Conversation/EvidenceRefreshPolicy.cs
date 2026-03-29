namespace Helper.Api.Conversation;

public enum WebEvidenceRefreshAction
{
    UseCached,
    UseCachedWithDisclosure,
    RefreshBeforeUse
}

public sealed record WebEvidenceRefreshDecision(
    WebEvidenceRefreshAction Action,
    string Reason,
    bool UseCachedFallbackOnFailure,
    bool RequiresDisclosure);

public interface IEvidenceRefreshPolicy
{
    WebEvidenceRefreshDecision Evaluate(ChatTurnContext context, WebEvidenceFreshnessAssessment assessment);
}

public sealed class EvidenceRefreshPolicy : IEvidenceRefreshPolicy
{
    public WebEvidenceRefreshDecision Evaluate(ChatTurnContext context, WebEvidenceFreshnessAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestedMode = NormalizeMode(context.Request.LiveWebMode);
        var resolvedRequirement = NormalizeRequirement(context.ResolvedLiveWebRequirement);

        if (requestedMode == "no_web")
        {
            return assessment.State switch
            {
                WebEvidenceFreshnessState.Fresh or WebEvidenceFreshnessState.Recent
                    => new WebEvidenceRefreshDecision(WebEvidenceRefreshAction.UseCached, "user_disabled_web", UseCachedFallbackOnFailure: true, RequiresDisclosure: false),
                _
                    => new WebEvidenceRefreshDecision(WebEvidenceRefreshAction.UseCachedWithDisclosure, "user_disabled_web_stale_cache", UseCachedFallbackOnFailure: true, RequiresDisclosure: true)
            };
        }

        if (assessment.State is WebEvidenceFreshnessState.Fresh or WebEvidenceFreshnessState.Recent)
        {
            return new WebEvidenceRefreshDecision(WebEvidenceRefreshAction.UseCached, "cache_reuse", UseCachedFallbackOnFailure: true, RequiresDisclosure: false);
        }

        if (resolvedRequirement == "web_required" || requestedMode == "force_search")
        {
            return new WebEvidenceRefreshDecision(
                WebEvidenceRefreshAction.RefreshBeforeUse,
                assessment.State == WebEvidenceFreshnessState.Stale ? "required_live_refresh_stale" : "required_live_refresh_aging",
                UseCachedFallbackOnFailure: true,
                RequiresDisclosure: assessment.State != WebEvidenceFreshnessState.Fresh);
        }

        return assessment.State switch
        {
            WebEvidenceFreshnessState.Aging
                => new WebEvidenceRefreshDecision(WebEvidenceRefreshAction.UseCachedWithDisclosure, "aging_cache_disclosed", UseCachedFallbackOnFailure: true, RequiresDisclosure: true),
            _ => new WebEvidenceRefreshDecision(WebEvidenceRefreshAction.RefreshBeforeUse, "stale_cache_refresh", UseCachedFallbackOnFailure: true, RequiresDisclosure: true)
        };
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.Equals(mode, "force_search", StringComparison.OrdinalIgnoreCase))
        {
            return "force_search";
        }

        if (string.Equals(mode, "no_web", StringComparison.OrdinalIgnoreCase))
        {
            return "no_web";
        }

        return "auto";
    }

    private static string NormalizeRequirement(string? requirement)
    {
        if (string.Equals(requirement, "web_required", StringComparison.OrdinalIgnoreCase))
        {
            return "web_required";
        }

        if (string.Equals(requirement, "web_helpful", StringComparison.OrdinalIgnoreCase))
        {
            return "web_helpful";
        }

        return "no_web_needed";
    }
}

