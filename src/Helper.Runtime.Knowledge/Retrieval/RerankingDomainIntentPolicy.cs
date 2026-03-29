namespace Helper.Runtime.Knowledge.Retrieval;

internal readonly record struct RerankingDomainScoreContext(
    string RoutedCollection,
    string? RoutedDomain,
    int RoutingHintMatches,
    double RoutingAnchorScore,
    double TitleLexical,
    double SourceLexical,
    double ContentLexical,
    double ContentCoverage);

internal readonly record struct RerankingDomainScoreAdjustment(double Bonus, double Penalty);

internal static class RerankingDomainIntentPolicy
{
    private static readonly HashSet<string> ArtCultureIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("art_culture");
    private static readonly HashSet<string> AnalysisStrategyIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("analysis_strategy");
    private static readonly HashSet<string> MedicineIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("medicine");
    private static readonly HashSet<string> ClinicalMedicineIntentRoots = RetrievalTextProcessing.BuildIntentRootSet(new[] { "клини", "серде", "недос", "пневм", "бакте", "лечен", "терап", "диагн", "симпт", "болез", "призн", "heart", "failu", "pneum", "sympt", "clini" });
    private static readonly HashSet<string> ComputerScienceIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("computer_science");
    private static readonly HashSet<string> PhysicsIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("physics");
    private static readonly HashSet<string> MaxwellIntentRoots = RetrievalTextProcessing.BuildIntentRootSet(new[] { "максвелл", "максвелла", "maxwell" });
    private static readonly HashSet<string> AnatomyIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("anatomy");
    private static readonly HashSet<string> NeuroIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("neuro");
    private static readonly HashSet<string> ChemistryIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("chemistry");
    private static readonly HashSet<string> EnglishLitIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("english_lang_lit");
    private static readonly HashSet<string> EntomologyIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("entomology");
    private static readonly HashSet<string> HistoricalArchiveIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("historical_encyclopedias");
    private static readonly HashSet<string> HistoryIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("history");
    private static readonly HashSet<string> EncyclopediaIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("encyclopedias");
    private static readonly HashSet<string> LinguisticsIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("linguistics");
    private static readonly HashSet<string> MathIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("math");
    private static readonly HashSet<string> BiologyIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("biology");
    private static readonly HashSet<string> EconomicsIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("economics");
    private static readonly HashSet<string> GeologyIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("geology");
    private static readonly HashSet<string> MythologyIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("mythology_religion");
    private static readonly HashSet<string> SciFiIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("sci_fi_concepts");
    private static readonly HashSet<string> SocialSciencesIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("social_sciences");
    private static readonly HashSet<string> PsychologyIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("psychology");
    private static readonly HashSet<string> PhilosophyIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("philosophy");
    private static readonly HashSet<string> RoboticsIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("robotics");
    private static readonly HashSet<string> RussianLitIntentRoots = RetrievalDomainProfileCatalog.GetIntentRoots("russian_lang_lit");

    public static RerankingDomainScoreAdjustment ComputeAdjustment(
        RerankingPolicy.PreparedQuery query,
        RerankingDomainScoreContext context)
    {
        var routedCollection = context.RoutedCollection;
        var routedDomain = context.RoutedDomain;
        var routingHintMatches = context.RoutingHintMatches;
        var routingAnchorScore = context.RoutingAnchorScore;
        var titleLexical = context.TitleLexical;
        var sourceLexical = context.SourceLexical;
        var contentLexical = context.ContentLexical;
        var contentCoverage = context.ContentCoverage;

        var domainPenalty = 0d;
        var domainBonus = 0d;
        if (string.Equals(routedDomain, "analysis_strategy", StringComparison.OrdinalIgnoreCase) && routingHintMatches == 0)
        {
            domainPenalty += 2.1;
            if (contentCoverage < 0.34 && (titleLexical + sourceLexical) > Math.Max(contentLexical * 1.3, 1.4))
            {
                domainPenalty += 1.25;
            }
        }

        if (KnowledgeCollectionNaming.IsHistoricalArchiveCollection(routedCollection) && routingHintMatches == 0 && routingAnchorScore < 4.5d && contentCoverage < 0.34)
        {
            domainPenalty += 1.4;
        }

        if (string.Equals(routedDomain, "chemistry", StringComparison.OrdinalIgnoreCase) && routingHintMatches == 0)
        {
            domainPenalty += 1.35;
            if (contentCoverage < 0.34)
            {
                domainPenalty += 0.55;
            }
        }

        var artCultureIntentMatches = RerankingQueryModel.CountIntentMatches(query, ArtCultureIntentRoots);
        if (artCultureIntentMatches >= 2)
        {
            if (string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase) && routingHintMatches <= 1)
            {
                domainPenalty += artCultureIntentMatches >= 3 ? 2.9 : 2.2;
            }
            else if ((string.Equals(routedDomain, "philosophy", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(routedDomain, "chemistry", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase))
                      && routingHintMatches == 0)
            {
                domainPenalty += artCultureIntentMatches >= 3 ? 1.95 : 1.45;
            }
        }

        var analysisStrategyIntentMatches = RerankingQueryModel.CountIntentMatches(query, AnalysisStrategyIntentRoots);
        if (analysisStrategyIntentMatches >= 2
            && string.Equals(routedDomain, "analysis_strategy", StringComparison.OrdinalIgnoreCase))
        {
            domainBonus += analysisStrategyIntentMatches >= 3 ? 1.05 : 0.75;
        }

        if (analysisStrategyIntentMatches >= 2
            && (string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "chemistry", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "economics", StringComparison.OrdinalIgnoreCase))
            && routingHintMatches == 0)
        {
            domainPenalty += 2.35;
        }

        var medicineIntentMatches = RerankingQueryModel.CountIntentMatches(query, MedicineIntentRoots);
        var clinicalMedicineIntentMatches = RerankingQueryModel.CountIntentMatches(query, ClinicalMedicineIntentRoots);
        if (medicineIntentMatches >= 2
            && string.Equals(routedDomain, "medicine", StringComparison.OrdinalIgnoreCase))
        {
            domainBonus += medicineIntentMatches >= 3 ? 0.95 : 0.7;
        }

        if (medicineIntentMatches >= 2
            && !string.Equals(routedDomain, "medicine", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(routedDomain, "anatomy", StringComparison.OrdinalIgnoreCase)
            && routingHintMatches == 0)
        {
            domainPenalty += string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "chemistry", StringComparison.OrdinalIgnoreCase)
                ? 2.35
                : 1.7;
        }

        if (clinicalMedicineIntentMatches >= 2
            && string.Equals(routedDomain, "virology", StringComparison.OrdinalIgnoreCase)
            && routingHintMatches <= 1)
        {
            domainPenalty += clinicalMedicineIntentMatches >= 3 ? 4.2 : 3.4;
        }

        var computerScienceIntentMatches = RerankingQueryModel.CountIntentMatches(query, ComputerScienceIntentRoots);
        if (computerScienceIntentMatches >= 2
            && KnowledgeCollectionNaming.IsHistoricalArchiveCollection(routedCollection)
            && routingHintMatches == 0)
        {
            domainPenalty += computerScienceIntentMatches >= 3 ? 2.8 : 2.2;
        }

        var anatomyIntentMatches = RerankingQueryModel.CountIntentMatches(query, AnatomyIntentRoots);
        if (anatomyIntentMatches >= 2
            && routingHintMatches == 0)
        {
            if (string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "historical_encyclopedias", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase))
            {
                domainPenalty += anatomyIntentMatches >= 3 ? 2.7 : 2.35;
            }
        }

        var neuroIntentMatches = RerankingQueryModel.CountIntentMatches(query, NeuroIntentRoots);
        if (neuroIntentMatches >= 2
            && (string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "philosophy", StringComparison.OrdinalIgnoreCase))
            && routingHintMatches == 0)
        {
            domainPenalty += neuroIntentMatches >= 3 ? 2.55 : 2.1;
        }

        var chemistryIntentMatches = RerankingQueryModel.CountIntentMatches(query, ChemistryIntentRoots);
        if (chemistryIntentMatches >= 2
            && (string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "art_culture", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "anatomy", StringComparison.OrdinalIgnoreCase))
            && routingHintMatches == 0)
        {
            domainPenalty += chemistryIntentMatches >= 3 ? 2.55 : 2.05;
        }

        var biologyIntentMatches = RerankingQueryModel.CountIntentMatches(query, BiologyIntentRoots);
        if (biologyIntentMatches >= 2)
        {
            if ((string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "philosophy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "russian_lang_lit", StringComparison.OrdinalIgnoreCase))
                && routingHintMatches == 0)
            {
                domainPenalty += 2.25;
            }

            if (string.Equals(routedDomain, "neuro", StringComparison.OrdinalIgnoreCase)
                && routingHintMatches == 0
                && neuroIntentMatches <= 1)
            {
                domainPenalty += biologyIntentMatches >= 3 ? 2.15 : 1.75;
            }
        }

        var economicsIntentMatches = RerankingQueryModel.CountIntentMatches(query, EconomicsIntentRoots);
        if (economicsIntentMatches >= 2)
        {
            if ((string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "philosophy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase))
                && routingHintMatches == 0)
            {
                domainPenalty += 2.2;
            }
        }

        var englishLitIntentMatches = RerankingQueryModel.CountIntentMatches(query, EnglishLitIntentRoots);
        if (englishLitIntentMatches >= 2)
        {
            if ((string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "russian_lang_lit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase))
                && routingHintMatches == 0)
            {
                domainPenalty += englishLitIntentMatches >= 3 ? 2.3 : 1.85;
            }
        }

        var entomologyIntentMatches = RerankingQueryModel.CountIntentMatches(query, EntomologyIntentRoots);
        if (entomologyIntentMatches >= 2)
        {
            if ((string.Equals(routedDomain, "biology", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase))
                && routingHintMatches == 0)
            {
                domainPenalty += entomologyIntentMatches >= 3 ? 2.45 : 1.95;
            }
        }

        var historicalArchiveIntentMatches = RerankingQueryModel.CountIntentMatches(query, HistoricalArchiveIntentRoots);
        if (historicalArchiveIntentMatches >= 1
            && string.Equals(routedDomain, "historical_encyclopedias", StringComparison.OrdinalIgnoreCase))
        {
            domainBonus += historicalArchiveIntentMatches >= 2 ? 0.95 : 0.65;
        }

        if (historicalArchiveIntentMatches >= 1)
        {
            if (string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase) && routingHintMatches <= 1)
            {
                domainPenalty += historicalArchiveIntentMatches >= 3 ? 2.65 : historicalArchiveIntentMatches == 2 ? 2.05 : 1.55;
            }
            else if (string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase) && routingHintMatches == 0)
            {
                domainPenalty += historicalArchiveIntentMatches >= 3 ? 1.8 : historicalArchiveIntentMatches == 2 ? 1.35 : 1.1;
            }
        }

        var historyIntentMatches = RerankingQueryModel.CountIntentMatches(query, HistoryIntentRoots);
        if (historyIntentMatches >= 2
            && string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase))
        {
            domainBonus += historyIntentMatches >= 3 ? 1.0 : 0.75;
        }

        if (historyIntentMatches >= 2)
        {
            if ((string.Equals(routedDomain, "analysis_strategy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "philosophy", StringComparison.OrdinalIgnoreCase))
                && routingHintMatches == 0)
            {
                domainPenalty += historyIntentMatches >= 3 ? 2.6 : 2.1;
            }
        }

        var encyclopediaIntentMatches = RerankingQueryModel.CountIntentMatches(query, EncyclopediaIntentRoots);
        if (encyclopediaIntentMatches >= 2)
        {
            if ((string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "economics", StringComparison.OrdinalIgnoreCase))
                && routingHintMatches == 0)
            {
                domainPenalty += encyclopediaIntentMatches >= 3 ? 2.25 : 1.7;
            }

            if (string.Equals(routedDomain, "historical_encyclopedias", StringComparison.OrdinalIgnoreCase)
                && historicalArchiveIntentMatches == 0
                && routingHintMatches <= 1)
            {
                domainPenalty += encyclopediaIntentMatches >= 3 ? 2.45 : 1.95;
            }
        }

        var linguisticsIntentMatches = RerankingQueryModel.CountIntentMatches(query, LinguisticsIntentRoots);
        if (linguisticsIntentMatches >= 2
            && string.Equals(routedDomain, "linguistics", StringComparison.OrdinalIgnoreCase))
        {
            domainBonus += linguisticsIntentMatches >= 3 ? 0.95 : 0.7;
        }

        if (linguisticsIntentMatches >= 2
            && (string.Equals(routedDomain, "math", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "russian_lang_lit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "philosophy", StringComparison.OrdinalIgnoreCase))
            && routingHintMatches == 0)
        {
            domainPenalty += string.Equals(routedDomain, "philosophy", StringComparison.OrdinalIgnoreCase)
                ? (linguisticsIntentMatches >= 3 ? 2.8 : 2.25)
                : (linguisticsIntentMatches >= 3 ? 2.35 : 1.9);
        }

        var mathIntentMatches = RerankingQueryModel.CountIntentMatches(query, MathIntentRoots);
        if (mathIntentMatches >= 2
            && (string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "philosophy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase))
            && routingHintMatches == 0)
        {
            domainPenalty += mathIntentMatches >= 3 ? 2.35 : 1.9;
        }

        var geologyIntentMatches = RerankingQueryModel.CountIntentMatches(query, GeologyIntentRoots);
        if (geologyIntentMatches >= 2)
        {
            if ((string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "chemistry", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "philosophy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "russian_lang_lit", StringComparison.OrdinalIgnoreCase))
                && routingHintMatches == 0)
            {
                domainPenalty += 2.25;
            }
        }

        var mythologyIntentMatches = RerankingQueryModel.CountIntentMatches(query, MythologyIntentRoots);
        if (mythologyIntentMatches >= 2)
        {
            if ((string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase))
                && routingHintMatches == 0)
            {
                domainPenalty += 2.1;
            }
        }

        var sciFiIntentMatches = RerankingQueryModel.CountIntentMatches(query, SciFiIntentRoots);
        if (sciFiIntentMatches >= 2)
        {
            if ((string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "chemistry", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase))
                && routingHintMatches == 0)
            {
                domainPenalty += 2.2;
            }
        }

        var socialSciencesIntentMatches = RerankingQueryModel.CountIntentMatches(query, SocialSciencesIntentRoots);
        if (socialSciencesIntentMatches >= 2
            && string.Equals(routedDomain, "social_sciences", StringComparison.OrdinalIgnoreCase))
        {
            domainBonus += socialSciencesIntentMatches >= 3 ? 0.95 : 0.7;
        }

        if (socialSciencesIntentMatches >= 2
            && (string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase))
            && routingHintMatches == 0)
        {
            domainPenalty += 2.1;
        }

        var psychologyIntentMatches = RerankingQueryModel.CountIntentMatches(query, PsychologyIntentRoots);
        if (psychologyIntentMatches >= 2
            && (string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "russian_lang_lit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase))
            && routingHintMatches == 0)
        {
            domainPenalty += psychologyIntentMatches >= 3 ? 2.35 : 2.1;
        }

        var philosophyIntentMatches = RerankingQueryModel.CountIntentMatches(query, PhilosophyIntentRoots);
        if (philosophyIntentMatches >= 1
            && (string.Equals(routedDomain, "chemistry", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase))
            && routingHintMatches == 0)
        {
            domainPenalty += philosophyIntentMatches >= 2 ? 2.35 : 1.9;
        }

        var physicsIntentMatches = RerankingQueryModel.CountIntentMatches(query, PhysicsIntentRoots);
        var maxwellIntentMatches = RerankingQueryModel.CountIntentMatches(query, MaxwellIntentRoots);
        if (physicsIntentMatches >= 2
            && string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase))
        {
            domainBonus += physicsIntentMatches >= 3 ? 0.95 : 0.7;
        }

        if (maxwellIntentMatches >= 1
            && string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase))
        {
            domainBonus += 1.1;
        }

        if (physicsIntentMatches >= 2
            && string.Equals(routedDomain, "analysis_strategy", StringComparison.OrdinalIgnoreCase)
            && routingHintMatches == 0)
        {
            domainPenalty += 2.2;
        }

        if (physicsIntentMatches >= 2
            && string.Equals(routedDomain, "math", StringComparison.OrdinalIgnoreCase)
            && routingHintMatches == 0)
        {
            domainPenalty += physicsIntentMatches >= 3 ? 2.35 : 1.95;
        }

        if (maxwellIntentMatches >= 1
            && string.Equals(routedDomain, "math", StringComparison.OrdinalIgnoreCase))
        {
            domainPenalty += routingHintMatches <= 1 ? 2.45 : 1.95;
        }

        var roboticsIntentMatches = RerankingQueryModel.CountIntentMatches(query, RoboticsIntentRoots);
        if (roboticsIntentMatches >= 2
            && string.Equals(routedDomain, "robotics", StringComparison.OrdinalIgnoreCase))
        {
            domainBonus += roboticsIntentMatches >= 3 ? 0.95 : 0.7;
        }

        if (roboticsIntentMatches >= 2
            && (string.Equals(routedDomain, "computer_science", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "physics", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "encyclopedias", StringComparison.OrdinalIgnoreCase))
            && routingHintMatches == 0)
        {
            domainPenalty += roboticsIntentMatches >= 3 ? 2.45 : 1.95;
        }

        var russianLitIntentMatches = RerankingQueryModel.CountIntentMatches(query, RussianLitIntentRoots);
        if (russianLitIntentMatches >= 2
            && string.Equals(routedDomain, "russian_lang_lit", StringComparison.OrdinalIgnoreCase))
        {
            domainBonus += russianLitIntentMatches >= 3 ? 0.95 : 0.7;
        }

        if (russianLitIntentMatches >= 2
            && (string.Equals(routedDomain, "philosophy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(routedDomain, "history", StringComparison.OrdinalIgnoreCase))
            && routingHintMatches == 0)
        {
            domainPenalty += russianLitIntentMatches >= 3 ? 2.45 : 1.95;
        }

        if (sciFiIntentMatches >= 2
            && string.Equals(routedDomain, "sci_fi_concepts", StringComparison.OrdinalIgnoreCase))
        {
            domainBonus += sciFiIntentMatches >= 3 ? 0.95 : 0.7;
        }

        if (sciFiIntentMatches >= 2
            && string.Equals(routedDomain, "neuro", StringComparison.OrdinalIgnoreCase)
            && routingHintMatches == 0)
        {
            domainPenalty += sciFiIntentMatches >= 3 ? 2.35 : 1.95;
        }

        if (sciFiIntentMatches >= 2
            && string.Equals(routedDomain, "anatomy", StringComparison.OrdinalIgnoreCase)
            && routingHintMatches == 0)
        {
            domainPenalty += sciFiIntentMatches >= 3 ? 2.15 : 1.75;
        }

        return new RerankingDomainScoreAdjustment(domainBonus, domainPenalty);
    }
}

