namespace Helper.Runtime.WebResearch.Ranking;

internal sealed record DomainTrustProfile(
    string Label,
    double AuthorityBoost,
    bool IsAuthoritative,
    bool IsLowTrust);

internal static class LowTrustDomainRegistry
{
    private static readonly IReadOnlyDictionary<string, DomainTrustProfile> ExactProfiles =
        new Dictionary<string, DomainTrustProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["learn.microsoft.com"] = new("official_vendor_docs", 0.42d, IsAuthoritative: true, IsLowTrust: false),
            ["devblogs.microsoft.com"] = new("official_vendor_blog", 0.24d, IsAuthoritative: true, IsLowTrust: false),
            ["developer.mozilla.org"] = new("official_reference_docs", 0.40d, IsAuthoritative: true, IsLowTrust: false),
            ["docs.python.org"] = new("official_reference_docs", 0.40d, IsAuthoritative: true, IsLowTrust: false),
            ["nodejs.org"] = new("official_reference_docs", 0.36d, IsAuthoritative: true, IsLowTrust: false),
            ["postgresql.org"] = new("official_reference_docs", 0.36d, IsAuthoritative: true, IsLowTrust: false),
            ["kubernetes.io"] = new("official_reference_docs", 0.36d, IsAuthoritative: true, IsLowTrust: false),
            ["docs.github.com"] = new("official_reference_docs", 0.34d, IsAuthoritative: true, IsLowTrust: false),
            ["github.com"] = new("source_repository", 0.22d, IsAuthoritative: true, IsLowTrust: false),
            ["nuget.org"] = new("package_registry", 0.34d, IsAuthoritative: true, IsLowTrust: false),
            ["npmjs.com"] = new("package_registry", 0.34d, IsAuthoritative: true, IsLowTrust: false),
            ["pypi.org"] = new("package_registry", 0.34d, IsAuthoritative: true, IsLowTrust: false),
            ["openai.com"] = new("official_vendor", 0.30d, IsAuthoritative: true, IsLowTrust: false),
            ["help.openai.com"] = new("official_vendor", 0.32d, IsAuthoritative: true, IsLowTrust: false),
            ["who.int"] = new("global_public_health", 0.46d, IsAuthoritative: true, IsLowTrust: false),
            ["cdc.gov"] = new("government_primary", 0.46d, IsAuthoritative: true, IsLowTrust: false),
            ["nih.gov"] = new("government_research", 0.42d, IsAuthoritative: true, IsLowTrust: false),
            ["europa.eu"] = new("government_primary", 0.44d, IsAuthoritative: true, IsLowTrust: false),
            ["ec.europa.eu"] = new("government_primary", 0.44d, IsAuthoritative: true, IsLowTrust: false),
            ["ncbi.nlm.nih.gov"] = new("medical_research_index", 0.40d, IsAuthoritative: true, IsLowTrust: false),
            ["pubmed.ncbi.nlm.nih.gov"] = new("medical_research_index", 0.42d, IsAuthoritative: true, IsLowTrust: false),
            ["pmc.ncbi.nlm.nih.gov"] = new("medical_research_fulltext", 0.40d, IsAuthoritative: true, IsLowTrust: false),
            ["cochrane.org"] = new("evidence_review", 0.38d, IsAuthoritative: true, IsLowTrust: false),
            ["link.springer.com"] = new("academic_publisher_article", 0.26d, IsAuthoritative: true, IsLowTrust: false),
            ["springermedicine.com"] = new("academic_publisher_article", 0.18d, IsAuthoritative: true, IsLowTrust: false),
            ["sciencedirect.com"] = new("academic_publisher_article", 0.20d, IsAuthoritative: true, IsLowTrust: false),
            ["medlineplus.gov"] = new("government_health_reference", 0.36d, IsAuthoritative: true, IsLowTrust: false),
            ["nhs.uk"] = new("national_health_service", 0.36d, IsAuthoritative: true, IsLowTrust: false),
            ["nice.org.uk"] = new("clinical_guideline_body", 0.40d, IsAuthoritative: true, IsLowTrust: false),
            ["mayoclinic.org"] = new("major_medical_reference", 0.24d, IsAuthoritative: true, IsLowTrust: false),
            ["news.un.org"] = new("multilateral_news", 0.20d, IsAuthoritative: true, IsLowTrust: false),
            ["unicef.org"] = new("multilateral_health", 0.24d, IsAuthoritative: true, IsLowTrust: false),
            ["www.unicef.org"] = new("multilateral_health", 0.24d, IsAuthoritative: true, IsLowTrust: false),
            ["edu.rosminzdrav.ru"] = new("government_primary", 0.42d, IsAuthoritative: true, IsLowTrust: false),
            ["cr.minzdrav.gov.ru"] = new("government_primary", 0.46d, IsAuthoritative: true, IsLowTrust: false),
            ["painrussia.ru"] = new("clinical_reference_society", 0.16d, IsAuthoritative: false, IsLowTrust: false),
            ["headache.ru"] = new("clinical_reference_society", 0.14d, IsAuthoritative: false, IsLowTrust: false),
            ["emcmos.ru"] = new("major_medical_clinic", 0.24d, IsAuthoritative: true, IsLowTrust: false),
            ["legalacts.ru"] = new("legal_document_host", 0.18d, IsAuthoritative: false, IsLowTrust: false),
            ["medelement.com"] = new("clinical_reference", 0.18d, IsAuthoritative: false, IsLowTrust: false),
            ["diseases.medelement.com"] = new("clinical_reference", 0.20d, IsAuthoritative: false, IsLowTrust: false),
            ["evidence-neurology.ru"] = new("evidence_reference", 0.18d, IsAuthoritative: false, IsLowTrust: false),
            ["medaccreditation.online"] = new("medical_guideline_digest", 0.08d, IsAuthoritative: false, IsLowTrust: false),
            ["reshnmo.ru"] = new("medical_guideline_digest", 0.08d, IsAuthoritative: false, IsLowTrust: false),
            ["researchgate.net"] = new("secondary_research_aggregator", -0.12d, IsAuthoritative: false, IsLowTrust: true),
            ["www.researchgate.net"] = new("secondary_research_aggregator", -0.12d, IsAuthoritative: false, IsLowTrust: true),
            ["jeffreypengmd.com"] = new("medical_opinion_blog", -0.16d, IsAuthoritative: false, IsLowTrust: true),
            ["physio-pedia.com"] = new("medical_wiki_reference", -0.12d, IsAuthoritative: false, IsLowTrust: true),
            ["vplaboutlet.by"] = new("fitness_blog", -0.20d, IsAuthoritative: false, IsLowTrust: false),
            ["mygenetics.ru"] = new("health_media_portal", -0.18d, IsAuthoritative: false, IsLowTrust: false),
            ["lighttherapyhome.com"] = new("commercial_health_vendor", -0.24d, IsAuthoritative: false, IsLowTrust: false),
            ["reddotled.com"] = new("commercial_health_vendor", -0.26d, IsAuthoritative: false, IsLowTrust: false),
            ["theragunrussia.ru"] = new("commercial_health_vendor", -0.22d, IsAuthoritative: false, IsLowTrust: false),
            ["icetribe.ru"] = new("commercial_health_vendor", -0.26d, IsAuthoritative: false, IsLowTrust: false),
            ["consultant.ru"] = new("trusted_legal_reference", 0.22d, IsAuthoritative: false, IsLowTrust: false),
            ["www.consultant.ru"] = new("trusted_legal_reference", 0.22d, IsAuthoritative: false, IsLowTrust: false),
            ["garant.ru"] = new("trusted_legal_reference", 0.20d, IsAuthoritative: false, IsLowTrust: false),
            ["base.garant.ru"] = new("trusted_legal_reference", 0.24d, IsAuthoritative: false, IsLowTrust: false),
            ["gdpr-info.eu"] = new("trusted_legal_reference", 0.22d, IsAuthoritative: false, IsLowTrust: false),
            ["www.gdpr-info.eu"] = new("trusted_legal_reference", 0.22d, IsAuthoritative: false, IsLowTrust: false),
            ["cyberleninka.ru"] = new("academic_index", 0.16d, IsAuthoritative: false, IsLowTrust: false),
            ["doctor.rambler.ru"] = new("health_media_portal", -0.10d, IsAuthoritative: false, IsLowTrust: false),
            ["fitstars.ru"] = new("fitness_blog", -0.12d, IsAuthoritative: false, IsLowTrust: false),
            ["fitness-pro.ru"] = new("fitness_blog", -0.14d, IsAuthoritative: false, IsLowTrust: false),
            ["glamour.ru"] = new("health_media_portal", -0.14d, IsAuthoritative: false, IsLowTrust: false),
            ["www.glamour.ru"] = new("health_media_portal", -0.14d, IsAuthoritative: false, IsLowTrust: false),
            ["medaboutme.ru"] = new("health_media_portal", -0.16d, IsAuthoritative: false, IsLowTrust: false),
            ["www.medaboutme.ru"] = new("health_media_portal", -0.16d, IsAuthoritative: false, IsLowTrust: false),
            ["woman.ru"] = new("health_media_portal", -0.18d, IsAuthoritative: false, IsLowTrust: false),
            ["www.woman.ru"] = new("health_media_portal", -0.18d, IsAuthoritative: false, IsLowTrust: false),
            ["kak-pishetsya.com"] = new("dictionary_spelling", -0.38d, IsAuthoritative: false, IsLowTrust: true),
            ["otvet.mail.ru"] = new("ugc_answers", -0.42d, IsAuthoritative: false, IsLowTrust: true),
            ["t.me"] = new("ugc_forum", -0.34d, IsAuthoritative: false, IsLowTrust: true),
            ["telegram.me"] = new("ugc_forum", -0.34d, IsAuthoritative: false, IsLowTrust: true),
            ["reddit.com"] = new("ugc_forum", -0.28d, IsAuthoritative: false, IsLowTrust: true),
            ["www.reddit.com"] = new("ugc_forum", -0.28d, IsAuthoritative: false, IsLowTrust: true),
            ["sec.gov"] = new("government_primary", 0.46d, IsAuthoritative: true, IsLowTrust: false),
            ["ftc.gov"] = new("government_primary", 0.46d, IsAuthoritative: true, IsLowTrust: false),
            ["irs.gov"] = new("government_primary", 0.46d, IsAuthoritative: true, IsLowTrust: false),
            ["weather.gov"] = new("government_primary", 0.46d, IsAuthoritative: true, IsLowTrust: false),
            ["reuters.com"] = new("major_newswire", 0.16d, IsAuthoritative: true, IsLowTrust: false),
            ["apnews.com"] = new("major_newswire", 0.16d, IsAuthoritative: true, IsLowTrust: false),
            ["bloomberg.com"] = new("major_business_news", 0.14d, IsAuthoritative: true, IsLowTrust: false),
            ["wsj.com"] = new("major_business_news", 0.14d, IsAuthoritative: true, IsLowTrust: false),
            ["nytimes.com"] = new("major_news", 0.12d, IsAuthoritative: true, IsLowTrust: false),
            ["coindesk.com"] = new("specialized_news", 0.10d, IsAuthoritative: true, IsLowTrust: false)
        };

    private static readonly string[] LowTrustTokens =
    {
        "coupon",
        "coupons",
        "promo",
        "deals",
        "deal",
        "casino",
        "bet",
        "bonus",
        "affiliate",
        "top10",
        "rankings"
    };

    private static readonly string[] LowTrustTlds =
    {
        ".click",
        ".top",
        ".xyz",
        ".biz",
        ".shop"
    };

    public static DomainTrustProfile Resolve(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return new DomainTrustProfile("unknown", 0d, IsAuthoritative: false, IsLowTrust: false);
        }

        var normalizedHost = host.Trim().ToLowerInvariant();
        if (ExactProfiles.TryGetValue(normalizedHost, out var exact))
        {
            return exact;
        }

        if (normalizedHost.EndsWith(".gov", StringComparison.OrdinalIgnoreCase))
        {
            return new DomainTrustProfile("government", 0.44d, IsAuthoritative: true, IsLowTrust: false);
        }

        if (normalizedHost.EndsWith(".europa.eu", StringComparison.OrdinalIgnoreCase))
        {
            return new DomainTrustProfile("government_primary", 0.44d, IsAuthoritative: true, IsLowTrust: false);
        }

        if (normalizedHost.EndsWith(".edu", StringComparison.OrdinalIgnoreCase))
        {
            return new DomainTrustProfile("academic", 0.24d, IsAuthoritative: true, IsLowTrust: false);
        }

        if (LooksLowTrust(normalizedHost))
        {
            return new DomainTrustProfile("low_trust_domain", -0.35d, IsAuthoritative: false, IsLowTrust: true);
        }

        if (normalizedHost.StartsWith("docs.", StringComparison.OrdinalIgnoreCase) ||
            normalizedHost.StartsWith("developer.", StringComparison.OrdinalIgnoreCase) ||
            normalizedHost.StartsWith("support.", StringComparison.OrdinalIgnoreCase))
        {
            return new DomainTrustProfile("reference_host", 0.12d, IsAuthoritative: false, IsLowTrust: false);
        }

        return new DomainTrustProfile("standard", 0d, IsAuthoritative: false, IsLowTrust: false);
    }

    public static bool LooksLowTrust(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalizedHost = host.Trim().ToLowerInvariant();
        if (LowTrustTlds.Any(normalizedHost.EndsWith))
        {
            return true;
        }

        var tokens = normalizedHost
            .Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(token => LowTrustTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
    }
}

