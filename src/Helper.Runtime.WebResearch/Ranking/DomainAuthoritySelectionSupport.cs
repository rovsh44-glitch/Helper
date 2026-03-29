namespace Helper.Runtime.WebResearch.Ranking;

internal static class DomainAuthoritySelectionSupport
{
    public static bool HasStrongAuthoritySignal(DomainAuthorityProfile profile, RankedWebDocumentCandidate candidate)
    {
        if (profile.MinimumStrongCandidates <= 0)
        {
            return false;
        }

        if (candidate.Spam.LowTrust)
        {
            return false;
        }

        if (Matches(candidate.Authority.Label, profile.WeakLabels))
        {
            return candidate.Authority.Reasons.Any(reason => Matches(reason, profile.StrongReasonMarkers));
        }

        if (candidate.Authority.Score >= profile.StrongAuthorityFloor)
        {
            return true;
        }

        if (candidate.Authority.IsAuthoritative &&
            candidate.Authority.Score >= profile.StrongAuthorityFloor - 0.08d &&
            !Matches(candidate.Authority.Label, profile.WeakLabels))
        {
            return true;
        }

        if (Matches(candidate.Authority.Label, profile.PreferredLabels) &&
            candidate.Authority.Score >= profile.StrongAuthorityFloor - 0.10d)
        {
            return true;
        }

        return candidate.Authority.Reasons.Any(reason => Matches(reason, profile.StrongReasonMarkers));
    }

    public static bool ShouldRetain(DomainAuthorityProfile profile, RankedWebDocumentCandidate candidate)
    {
        if (candidate.Spam.LowTrust)
        {
            return false;
        }

        if (HasStrongAuthoritySignal(profile, candidate))
        {
            return true;
        }

        if (Matches(candidate.Authority.Label, profile.WeakLabels))
        {
            return false;
        }

        if (Matches(candidate.Authority.Label, profile.PreferredLabels))
        {
            return candidate.Authority.Score >= profile.StrongAuthorityFloor - 0.14d;
        }

        if (profile.AllowAuthoritativeNewsBypass && candidate.Authority.IsAuthoritative)
        {
            return candidate.Authority.Score >= profile.StrongAuthorityFloor - 0.14d;
        }

        return candidate.Authority.Score >= profile.StrongAuthorityFloor - 0.06d;
    }

    public static IReadOnlyList<RankedWebDocumentCandidate> ApplyProfile(
        DomainAuthorityProfile profile,
        IReadOnlyList<RankedWebDocumentCandidate> rankedDocuments,
        List<string> trace,
        string reasonCode)
    {
        if (rankedDocuments.Count == 0 || profile.MinimumStrongCandidates <= 0)
        {
            return rankedDocuments;
        }

        var strongCount = rankedDocuments.Count(candidate => HasStrongAuthoritySignal(profile, candidate));
        if (strongCount < profile.MinimumStrongCandidates)
        {
            return rankedDocuments;
        }

        var retained = rankedDocuments
            .Where(candidate => ShouldRetain(profile, candidate))
            .ToList();
        if (retained.Count < Math.Min(profile.MinimumStrongCandidates, rankedDocuments.Count))
        {
            return rankedDocuments;
        }

        var dropped = rankedDocuments.Count - retained.Count;
        if (dropped > 0)
        {
            trace.Add($"web_search.evidence_floor applied=yes dropped={dropped} profile={profile.Name} strong={strongCount} reason={reasonCode}");
            foreach (var candidate in rankedDocuments.Except(retained).Take(4))
            {
                trace.Add(
                    $"web_search.evidence_floor.drop profile={profile.Name} low_trust={(candidate.Spam.LowTrust ? "yes" : "no")} authority={candidate.Authority.Label}:{candidate.Authority.Score:0.000} final={candidate.FinalScore:0.000} url={candidate.Document.Url}");
            }
        }

        return retained;
    }

    public static bool Matches(string? value, IReadOnlyList<string> markers)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var marker in markers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

