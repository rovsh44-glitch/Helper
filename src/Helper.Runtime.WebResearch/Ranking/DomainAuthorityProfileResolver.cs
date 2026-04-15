namespace Helper.Runtime.WebResearch.Ranking;

internal sealed record DomainAuthorityProfile(
    string Name,
    double StrongAuthorityFloor,
    int MinimumStrongCandidates,
    bool AllowAuthoritativeNewsBypass,
    IReadOnlyList<string> PreferredLabels,
    IReadOnlyList<string> WeakLabels,
    IReadOnlyList<string> StrongReasonMarkers);

internal static class DomainAuthorityProfileResolver
{
    private static readonly DomainAuthorityProfile DefaultProfile = new(
        "default",
        0d,
        0,
        false,
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>());

    private static readonly DomainAuthorityProfile MedicalEvidenceProfile = new(
        "medical_evidence",
        0.55d,
        2,
        false,
        new[]
        {
            "government_primary", "government_health_reference", "government_research", "global_public_health",
            "national_health_service", "clinical_guideline_body", "major_medical_reference", "major_medical_clinic",
            "clinical_reference", "clinical_reference_society", "medical_research_index", "medical_research_fulltext",
            "evidence_review", "academic_index", "multilateral_health"
        },
        new[]
        {
            "health_media_portal", "fitness_blog", "commercial_health_vendor", "ugc_answers", "ugc_forum",
            "medical_opinion_blog", "medical_wiki_reference", "secondary_research_aggregator"
        },
        new[]
        {
            "primary_medical_research_match", "medical_evidence_match"
        });

    private static readonly DomainAuthorityProfile MedicalConflictProfile = new(
        "medical_conflict",
        0.64d,
        2,
        false,
        new[]
        {
            "government_primary", "government_health_reference", "government_research", "global_public_health",
            "national_health_service", "clinical_guideline_body", "major_medical_reference", "major_medical_clinic",
            "clinical_reference", "clinical_reference_society", "medical_research_index", "medical_research_fulltext",
            "evidence_review", "academic_index", "multilateral_health"
        },
        new[]
        {
            "health_media_portal", "fitness_blog", "commercial_health_vendor", "standard", "major_news", "major_business_news", "specialized_news", "ugc_answers", "ugc_forum",
            "medical_opinion_blog", "medical_wiki_reference", "secondary_research_aggregator"
        },
        new[]
        {
            "primary_medical_research_match", "medical_evidence_match", "medical_conflict_authority_match"
        });

    private static readonly DomainAuthorityProfile LawRegulationProfile = new(
        "law_regulation",
        0.58d,
        2,
        true,
        new[]
        {
            "government_primary", "trusted_legal_reference", "legal_document_host", "official_reference_docs", "official_vendor_docs"
        },
        new[]
        {
            "standard", "health_media_portal", "fitness_blog", "ugc_answers", "ugc_forum", "source_repository"
        },
        new[]
        {
            "official_bias_match", "reference_document_match", "evidence_source_match"
        });

    private static readonly DomainAuthorityProfile FinanceMarketProfile = new(
        "finance_market",
        0.56d,
        2,
        true,
        new[]
        {
            "government_primary", "trusted_legal_reference", "major_business_news", "major_newswire", "specialized_news"
        },
        new[]
        {
            "standard", "health_media_portal", "fitness_blog", "ugc_answers", "ugc_forum"
        },
        new[]
        {
            "official_bias_match", "timely_source_match", "evidence_source_match"
        });

    private static readonly DomainAuthorityProfile ScienceReferenceProfile = new(
        "science_reference",
        0.54d,
        2,
        false,
        new[]
        {
            "academic", "academic_index", "source_repository", "official_reference_docs", "official_vendor_docs",
            "medical_research_index", "medical_research_fulltext", "evidence_review"
        },
        new[]
        {
            "standard", "major_news", "major_business_news", "specialized_news", "health_media_portal", "fitness_blog", "ugc_answers", "ugc_forum"
        },
        new[]
        {
            "reference_document_match", "evidence_source_match", "primary_medical_research_match"
        });

    private static readonly DomainAuthorityProfile CurrentEventsProfile = new(
        "current_events",
        0.52d,
        2,
        true,
        new[]
        {
            "government_primary", "global_public_health", "multilateral_news", "multilateral_health",
            "major_newswire", "major_news", "major_business_news", "specialized_news"
        },
        new[]
        {
            "standard", "health_media_portal", "fitness_blog", "ugc_answers", "ugc_forum", "source_repository"
        },
        new[]
        {
            "timely_source_match", "official_bias_match"
        });

    private static readonly string[] LawTokens =
    {
        "law", "laws", "legal", "regulation", "regulations", "regulatory", "rule", "rules", "compliance",
        "tax", "taxes", "act", "acts", "statute", "statutes", "gdpr", "privacy policy",
        "visa", "visas", "immigration", "migration", "relocation", "residence permit", "work permit", "blue card", "bluecard", "skilled worker",
        "filing", "reporting", "deadline", "deadlines", "invoice", "invoices", "foreign clients", "remote worker", "remote work",
        "закон", "законы", "право", "правовой", "регуляц", "регулирован", "правила", "норматив",
        "комплаенс", "налог", "налоги", "налого", "налогов", "отчетност", "отчётност", "срок", "сроки", "инвойс", "иностранн",
        "виза", "визы", "визовые", "миграц", "релокац", "внж", "вид на жительство", "разрешение на работу", "голубая карта",
        "удален", "удалён", "акт", "постановлен", "указ"
    };

    private static readonly string[] FinanceTokens =
    {
        "finance", "financial", "market", "markets", "stock", "stocks", "shares", "earnings", "forecast", "inflation",
        "interest rate", "bond", "bonds", "sec", "irs", "crypto", "bitcoin", "etf",
        "финанс", "рынок", "акции", "котиров", "прибыл", "прогноз", "инфляц", "ставк", "облигац", "крипт", "биткоин", "etf"
    };

    private static readonly string[] ScienceTokens =
    {
        "paper", "papers", "study", "studies", "research", "journal", "preprint", "arxiv", "abstract",
        "systematic review", "meta-analysis", "trial", "evidence", "publication", "doi",
        "статья", "статьи", "исследован", "научн", "журнал", "препринт", "arxiv", "аннотац", "абстракт", "doi"
    };

    private static readonly string[] CurrentEventsTokens =
    {
        "news", "breaking", "update", "updates", "today", "latest", "current", "announced", "statement",
        "outbreak", "election", "ceasefire", "sanctions",
        "новост", "сегодня", "послед", "текущ", "заявлен", "обновл", "вспышк", "выбор", "санкц"
    };

    public static DomainAuthorityProfile Resolve(string? requestQuery, WebSearchPlan plan)
    {
        var query = SearchQueryIntentProfileClassifier.NormalizeWhitespace(string.IsNullOrWhiteSpace(requestQuery) ? plan.Query : requestQuery);
        var queryProfile = SourceAuthorityScorer.BuildQueryProfile(query, plan.QueryKind);
        var intentProfile = SearchQueryIntentProfileClassifier.Classify(query, queryProfile);

        if (queryProfile.MedicalEvidenceHeavy)
        {
            return intentProfile.ComparisonSensitive || intentProfile.ContradictionSensitive || plan.QueryKind.Equals("contradiction", StringComparison.OrdinalIgnoreCase)
                ? MedicalConflictProfile
                : MedicalEvidenceProfile;
        }

        if (ContainsAny(query, LawTokens))
        {
            return LawRegulationProfile;
        }

        if (ContainsAny(query, FinanceTokens))
        {
            return FinanceMarketProfile;
        }

        if (intentProfile.PaperAnalysisLike || ContainsAny(query, ScienceTokens))
        {
            return ScienceReferenceProfile;
        }

        if (intentProfile.FreshnessSensitive || ContainsAny(query, CurrentEventsTokens))
        {
            return CurrentEventsProfile;
        }

        return DefaultProfile;
    }

    public static DomainAuthorityProfile ResolveByName(string? name)
    {
        return name switch
        {
            "medical_evidence" => MedicalEvidenceProfile,
            "medical_conflict" => MedicalConflictProfile,
            "law_regulation" => LawRegulationProfile,
            "finance_market" => FinanceMarketProfile,
            "science_reference" => ScienceReferenceProfile,
            "current_events" => CurrentEventsProfile,
            _ => DefaultProfile
        };
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalizedText = SearchQueryIntentProfileClassifier.NormalizeWhitespace(text);
        var normalizedTokens = SourceAuthorityScorer.TokenizeForRanking(normalizedText);
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (token.Contains(' ', StringComparison.Ordinal) &&
                normalizedText.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var markerTokens = SourceAuthorityScorer.TokenizeForRanking(token);
            if (markerTokens.Count == 0)
            {
                continue;
            }

            if (markerTokens.Any(marker => normalizedTokens.Contains(marker)))
            {
                return true;
            }
        }

        return false;
    }
}

