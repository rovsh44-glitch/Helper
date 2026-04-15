using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public sealed record EvidenceFusionSnapshot(
    int WebSourceCount,
    int LocalSourceCount,
    int AttachmentSourceCount,
    double WebCitationCoverage,
    double LocalCitationCoverage,
    double FreshClaimWebCoverage,
    double BackgroundClaimCoverage,
    int UnsupportedFreshClaimCount,
    int LocalOnlyFreshClaimCount,
    IReadOnlyList<string> Trace);

internal static class EvidenceFusionService
{
    private static readonly string[] FreshTokens =
    {
        "latest", "current", "today", "recent", "fresh", "deadline", "threshold",
        "price", "release", "version", "2026", "2025",
        "актуаль", "сегодня", "сейчас", "свеж", "последн", "дедлайн", "срок",
        "порог", "налог", "регулятор", "текущ"
    };

    public static EvidenceFusionSnapshot Build(ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requireWebForFresh = ReadBoolEnv("HELPER_MIXED_EVIDENCE_REQUIRE_WEB_FOR_FRESH", defaultValue: true);
        var allowLocalCurrentFacts = ReadBoolEnv("HELPER_LOCAL_LIBRARY_ALLOW_CURRENT_FACTS", defaultValue: false);
        var localCurrentFactAllowList = ReadCsvEnv("HELPER_LOCAL_LIBRARY_CURRENT_FACT_ALLOWLIST");

        var evidenceItems = context.ResearchEvidenceItems
            .Where(static item => !item.IsFallback)
            .OrderBy(static item => item.Ordinal)
            .ToArray();
        var webSourceCount = evidenceItems.Count(ConversationSourceClassifier.IsWebEvidence);
        var localSourceCount = evidenceItems.Count(ConversationSourceClassifier.IsLocalEvidence);
        var attachmentSourceCount = evidenceItems.Count(static item =>
            string.Equals(item.SourceLayer, "attachment", StringComparison.OrdinalIgnoreCase));

        if (evidenceItems.Length == 0)
        {
            webSourceCount = context.Sources.Count(ConversationSourceClassifier.IsHttpSource);
            localSourceCount = context.Sources.Count(source => !ConversationSourceClassifier.IsHttpSource(source));
        }

        var sourceLayerByOrdinal = evidenceItems.ToDictionary(
            static item => item.Ordinal,
            ConversationSourceClassifier.ResolveLayer);
        var sourceByOrdinal = evidenceItems.ToDictionary(static item => item.Ordinal);
        var claims = context.ClaimGroundings.ToArray();
        var freshClaims = claims.Where(claim => IsFreshClaim(context, claim.Claim)).ToArray();
        var backgroundClaims = claims.Except(freshClaims).ToArray();
        var webCoveredFresh = freshClaims.Count(claim => IsClaimSupportedByLayer(claim, sourceLayerByOrdinal, "web"));
        var localCoveredFresh = freshClaims.Count(claim => IsClaimSupportedByLayer(claim, sourceLayerByOrdinal, "local_library"));
        var unsupportedFresh = freshClaims.Count(claim =>
            !IsClaimSupportedByLayer(claim, sourceLayerByOrdinal, "web") &&
            !IsFreshClaimAllowedByLocalPolicy(
                claim,
                sourceLayerByOrdinal,
                sourceByOrdinal,
                requireWebForFresh,
                allowLocalCurrentFacts,
                localCurrentFactAllowList));
        var localOnlyFresh = freshClaims.Count(claim =>
            IsClaimSupportedByLayer(claim, sourceLayerByOrdinal, "local_library") &&
            !IsClaimSupportedByLayer(claim, sourceLayerByOrdinal, "web") &&
            !IsFreshClaimAllowedByLocalPolicy(
                claim,
                sourceLayerByOrdinal,
                sourceByOrdinal,
                requireWebForFresh,
                allowLocalCurrentFacts,
                localCurrentFactAllowList));
        var webCoveredClaims = claims.Count(claim => IsClaimSupportedByLayer(claim, sourceLayerByOrdinal, "web"));
        var localCoveredClaims = claims.Count(claim => IsClaimSupportedByLayer(claim, sourceLayerByOrdinal, "local_library"));
        var coveredBackground = backgroundClaims.Count(claim => claim.SourceIndex.HasValue);

        var trace = new List<string>
        {
            $"evidence_fusion.web_sources={webSourceCount}",
            $"evidence_fusion.local_sources={localSourceCount}",
            $"evidence_fusion.attachment_sources={attachmentSourceCount}",
            $"evidence_fusion.fresh_claims={freshClaims.Length}",
            $"evidence_fusion.unsupported_fresh_claims={unsupportedFresh}",
            $"evidence_fusion.local_only_fresh_claims={localOnlyFresh}",
            $"evidence_fusion.require_web_for_fresh={requireWebForFresh.ToString().ToLowerInvariant()}",
            $"evidence_fusion.local_current_facts_allowed={allowLocalCurrentFacts.ToString().ToLowerInvariant()}"
        };

        return new EvidenceFusionSnapshot(
            WebSourceCount: webSourceCount,
            LocalSourceCount: localSourceCount,
            AttachmentSourceCount: attachmentSourceCount,
            WebCitationCoverage: Rate(webCoveredClaims, claims.Length),
            LocalCitationCoverage: Rate(localCoveredClaims, claims.Length),
            FreshClaimWebCoverage: Rate(webCoveredFresh, freshClaims.Length),
            BackgroundClaimCoverage: Rate(coveredBackground, backgroundClaims.Length),
            UnsupportedFreshClaimCount: unsupportedFresh,
            LocalOnlyFreshClaimCount: localOnlyFresh,
            Trace: trace);
    }

    private static bool IsClaimSupportedByLayer(
        ClaimGrounding claim,
        IReadOnlyDictionary<int, string> sourceLayerByOrdinal,
        string layer)
    {
        return claim.SourceIndex.HasValue &&
               sourceLayerByOrdinal.TryGetValue(claim.SourceIndex.Value, out var sourceLayer) &&
               string.Equals(sourceLayer, layer, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFreshClaimAllowedByLocalPolicy(
        ClaimGrounding claim,
        IReadOnlyDictionary<int, string> sourceLayerByOrdinal,
        IReadOnlyDictionary<int, ResearchEvidenceItem> sourceByOrdinal,
        bool requireWebForFresh,
        bool allowLocalCurrentFacts,
        IReadOnlySet<string> localCurrentFactAllowList)
    {
        if (!claim.SourceIndex.HasValue ||
            !sourceLayerByOrdinal.TryGetValue(claim.SourceIndex.Value, out var sourceLayer) ||
            !string.Equals(sourceLayer, "local_library", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!requireWebForFresh)
        {
            return true;
        }

        if (!allowLocalCurrentFacts ||
            localCurrentFactAllowList.Count == 0 ||
            !sourceByOrdinal.TryGetValue(claim.SourceIndex.Value, out var source))
        {
            return false;
        }

        return MatchesAllowList(source.SourceId, localCurrentFactAllowList) ||
               MatchesAllowList(source.DisplayTitle, localCurrentFactAllowList) ||
               MatchesAllowList(source.Title, localCurrentFactAllowList) ||
               MatchesAllowList(source.Url, localCurrentFactAllowList);
    }

    private static bool MatchesAllowList(string? value, IReadOnlySet<string> allowList)
    {
        return !string.IsNullOrWhiteSpace(value) && allowList.Contains(value.Trim());
    }

    private static bool IsFreshClaim(ChatTurnContext context, string claim)
    {
        if (string.Equals(context.ResolvedLiveWebRequirement, "web_required", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (LooksLikeLocalContextReference(claim))
        {
            return false;
        }

        if (LooksLikeEvidenceBoundaryClaim(claim))
        {
            return false;
        }

        return FreshTokens.Any(token => claim.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static double Rate(int numerator, int denominator)
    {
        return denominator <= 0 ? 0.0 : numerator / (double)denominator;
    }

    private static bool LooksLikeLocalContextReference(string claim)
    {
        return (claim.Contains("локаль", StringComparison.OrdinalIgnoreCase) ||
                claim.Contains("local", StringComparison.OrdinalIgnoreCase)) &&
               (claim.Contains("текущ", StringComparison.OrdinalIgnoreCase) ||
                claim.Contains("current", StringComparison.OrdinalIgnoreCase)) &&
               (claim.Contains("отрыв", StringComparison.OrdinalIgnoreCase) ||
                claim.Contains("excerpt", StringComparison.OrdinalIgnoreCase) ||
                claim.Contains("context", StringComparison.OrdinalIgnoreCase) ||
                claim.Contains("контекст", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeEvidenceBoundaryClaim(string claim)
    {
        return ContainsAny(claim, "непровер", "без live", "без live-", "границ", "ограничен", "ограничения evidence", "unverified", "not verified", "evidence limit");
    }

    private static bool ContainsAny(string text, params string[] markers)
    {
        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ReadBoolEnv(string name, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private static IReadOnlySet<string> ReadCsvEnv(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return raw
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
