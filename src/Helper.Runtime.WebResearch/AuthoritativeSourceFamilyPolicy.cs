namespace Helper.Runtime.WebResearch;

internal sealed record AuthoritativeSourceFamilyDecision(
    IReadOnlyList<WebSearchDocument> Documents,
    IReadOnlyList<string> Trace);

internal interface IAuthoritativeSourceFamilyPolicy
{
    AuthoritativeSourceFamilyDecision Augment(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents);
}

internal sealed class AuthoritativeSourceFamilyPolicy : IAuthoritativeSourceFamilyPolicy
{
    private const string EuAiActServiceDeskUrl = "https://ai-act-service-desk.ec.europa.eu/en";
    private const string EuAiActGpaiProviderGuidelinesUrl = "https://digital-strategy.ec.europa.eu/en/policies/guidelines-gpai-providers";
    private const string EuAiActRegulatoryFrameworkUrl = "https://digital-strategy.ec.europa.eu/en/policies/regulatory-framework-ai";
    private const string CrossrefVersioningUrl = "https://crossref.org/documentation/principles-practices/best-practices/versioning";
    private const string CrossrefCrossmarkUrl = "https://www.crossref.org/services/crossmark/";
    private const string CopeExpressionsOfConcernUrl = "https://publicationethics.org/guidance/guideline/expressions-concern";
    private const string SherpaRomeoUrl = "https://v2.sherpa.ac.uk/romeo/";
    private const string ArxivLicenseUrl = "https://info.arxiv.org/help/license/index.html";
    private const string ArxivSubmitUrl = "https://info.arxiv.org/help/submit/index.html";
    private const string IeaHeatPumpsUrl = "https://www.iea.org/energy-system/buildings/heat-pumps";
    private const string IeaFutureHeatPumpsUrl = "https://www.iea.org/reports/the-future-of-heat-pumps";
    private const string EnergySaverHeatPumpsUrl = "https://www.energy.gov/energysaver/heat-pump-systems";
    private const string SoliqUrl = "https://soliq.uz/";
    private const string LexUzUrl = "https://lex.uz/";
    private const string MyGovUzUrl = "https://my.gov.uz/";
    private const string EasaCivilDronesUrl = "https://www.easa.europa.eu/en/domains/civil-drones";
    private const string EuCustomsProceduresUrl = "https://taxation-customs.ec.europa.eu/customs-4/customs-procedures-import-and-export_en";
    private const string EuDronesSectorUrl = "https://single-market-economy.ec.europa.eu/sectors/mechanical-engineering/drones_en";

    private static readonly WebSearchDocument EuAiActServiceDeskSource = new(
        EuAiActServiceDeskUrl,
        "AI Act Service Desk - AI Act Single Information Platform",
        "European Commission AI Act Service Desk single information platform with official guidance, FAQs and support for AI Act compliance, providers and obligations.");

    private static readonly WebSearchDocument EuAiActGpaiProviderGuidelinesSource = new(
        EuAiActGpaiProviderGuidelinesUrl,
        "Guidelines for providers of general-purpose AI models",
        "European Commission guidelines for providers of general-purpose AI models under the AI Act, clarifying scope of obligations and AI Office implementation guidance.");

    private static readonly WebSearchDocument EuAiActRegulatoryFrameworkSource = new(
        EuAiActRegulatoryFrameworkUrl,
        "AI Act | Shaping Europe's digital future",
        "European Commission Digital Strategy policy page for the EU AI Act regulatory framework, including AI Office implementation context, provider obligations, compliance guidance and updates relevant to software vendors.");

    private static readonly WebSearchDocument CrossrefVersioningSource = new(
        CrossrefVersioningUrl,
        "Version control, corrections, and retractions - Crossref",
        "Crossref guidance for article versioning, corrections, retractions, Crossmark updates, and publication status metadata.");

    private static readonly WebSearchDocument CrossrefCrossmarkSource = new(
        CrossrefCrossmarkUrl,
        "Crossmark - Crossref",
        "Crossref Crossmark service for checking whether scholarly content is current and whether updates, corrections, or retractions exist.");

    private static readonly WebSearchDocument CopeExpressionsOfConcernSource = new(
        CopeExpressionsOfConcernUrl,
        "Expressions of concern | COPE: Committee on Publication Ethics",
        "Committee on Publication Ethics guidance for expressions of concern, corrections, retractions, and editorial publication status handling.");

    private static readonly WebSearchDocument SherpaRomeoSource = new(
        SherpaRomeoUrl,
        "Sherpa Romeo - publisher copyright and open access policies",
        "Sherpa Romeo registry for journal and publisher open access, self-archiving, repository, accepted manuscript, embargo, and copyright policies.");

    private static readonly WebSearchDocument ArxivLicenseSource = new(
        ArxivLicenseUrl,
        "arXiv license and copyright help",
        "arXiv help page covering license selection, copyright, distribution rights, and reuse policy for preprints.");

    private static readonly WebSearchDocument ArxivSubmitSource = new(
        ArxivSubmitUrl,
        "arXiv submit help",
        "arXiv submission help for preprints, repository submission workflow, authors, licensing, and publication-policy context.");

    private static readonly WebSearchDocument IeaHeatPumpsSource = new(
        IeaHeatPumpsUrl,
        "Heat Pumps - IEA",
        "International Energy Agency topic page on heat pumps, efficiency, buildings, deployment, energy performance, and current energy-system context.");

    private static readonly WebSearchDocument IeaFutureHeatPumpsSource = new(
        IeaFutureHeatPumpsUrl,
        "The Future of Heat Pumps - IEA",
        "International Energy Agency report on heat pump efficiency, adoption, policy, buildings, and energy-system evidence.");

    private static readonly WebSearchDocument EnergySaverHeatPumpsSource = new(
        EnergySaverHeatPumpsUrl,
        "Heat Pump Systems - Energy Saver",
        "US Department of Energy Energy Saver guidance on heat pump systems, efficiency, operation, and practical performance considerations.");

    private static readonly WebSearchDocument SoliqSource = new(
        SoliqUrl,
        "State Tax Committee of Uzbekistan",
        "Official Uzbekistan tax authority portal for tax reporting, filing, taxpayer services, invoices, foreign-client income context, and current tax guidance.");

    private static readonly WebSearchDocument LexUzSource = new(
        LexUzUrl,
        "LEX.UZ - National database of legislation of Uzbekistan",
        "Official Uzbekistan legislation database for tax code, reporting obligations, legal requirements, remote work, foreign clients, and filing rules.");

    private static readonly WebSearchDocument MyGovUzSource = new(
        MyGovUzUrl,
        "my.gov.uz - Interactive public services portal",
        "Official Uzbekistan public services portal for taxpayer services, e-government filings, reporting workflows, and individual entrepreneur obligations.");

    private static readonly WebSearchDocument EasaCivilDronesSource = new(
        EasaCivilDronesUrl,
        "Civil drones - EASA",
        "European Union Aviation Safety Agency civil drones page covering EU drone rules, UAS operations, categories, guidance, and official aviation requirements.");

    private static readonly WebSearchDocument EuCustomsProceduresSource = new(
        EuCustomsProceduresUrl,
        "Customs procedures for import and export - European Commission",
        "European Commission Taxation and Customs Union guidance on import and export customs procedures, customs controls, duties, VAT, and EU import formalities.");

    private static readonly WebSearchDocument EuDronesSectorSource = new(
        EuDronesSectorUrl,
        "Drones - European Commission single market",
        "European Commission single market page on drones, CE marking, product rules, market requirements, and regulatory context for drone products in the EU.");

    public AuthoritativeSourceFamilyDecision Augment(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents)
    {
        var sourceFamily =
            ResolveEuAiActSourceFamily(request, plan) ??
            ResolveRetractionStatusSourceFamily(request, plan) ??
            ResolveArxivPublisherPolicySourceFamily(request, plan) ??
            ResolveHeatPumpSourceFamily(request, plan) ??
            ResolveUzbekistanTaxSourceFamily(request, plan) ??
            ResolveEuDroneCustomsSourceFamily(request, plan);

        if (sourceFamily is null)
        {
            return new AuthoritativeSourceFamilyDecision(documents, Array.Empty<string>());
        }

        var sourceFamilyValue = sourceFamily.Value;
        if (documents.Any(document => SameUrl(document.Url, sourceFamilyValue.Document.Url)))
        {
            return new AuthoritativeSourceFamilyDecision(
                documents,
                new[] { $"web_search.authoritative_source_family added=no family={sourceFamilyValue.Family} reason=already_present" });
        }

        var augmented = documents.Concat(new[] { sourceFamilyValue.Document }).ToArray();
        return new AuthoritativeSourceFamilyDecision(
            augmented,
            new[]
            {
                $"web_search.authoritative_source_family added=yes family={sourceFamilyValue.Family} url={sourceFamilyValue.Document.Url}"
            });
    }

    private static (string Family, WebSearchDocument Document)? ResolveEuAiActSourceFamily(WebSearchRequest request, WebSearchPlan plan)
    {
        return ShouldAddEuAiActSourceFamily(request, plan)
            ? ResolveEuAiActSeed(plan.QueryKind)
            : null;
    }

    private static (string Family, WebSearchDocument Document)? ResolveRetractionStatusSourceFamily(WebSearchRequest request, WebSearchPlan plan)
    {
        if (!IsAllowedQueryKind(plan.QueryKind, "primary", "official", "freshness", "paper_focus", "evidence"))
        {
            return null;
        }

        var combined = $"{request.Query} {plan.Query}";
        if (!ContainsAny(combined, "retraction", "retracted", "correction", "erratum", "expression of concern", "crossmark", "отозван", "ретракц", "исправлен", "оспор"))
        {
            return null;
        }

        return plan.QueryKind.ToLowerInvariant() switch
        {
            "primary" => ("retraction_crossref_versioning", CrossrefVersioningSource),
            "official" => ("retraction_crossref_crossmark", CrossrefCrossmarkSource),
            "freshness" => ("retraction_cope_expression_of_concern", CopeExpressionsOfConcernSource),
            "paper_focus" => ("retraction_crossref_versioning", CrossrefVersioningSource),
            "evidence" => ("retraction_cope_expression_of_concern", CopeExpressionsOfConcernSource),
            _ => null
        };
    }

    private static (string Family, WebSearchDocument Document)? ResolveArxivPublisherPolicySourceFamily(WebSearchRequest request, WebSearchPlan plan)
    {
        if (!IsAllowedQueryKind(plan.QueryKind, "primary", "publisher_policy", "paper_focus", "official"))
        {
            return null;
        }

        var combined = $"{request.Query} {plan.Query}";
        if (!ContainsAny(combined, "arxiv", "preprint", "accepted manuscript", "self-archiving", "sherpa", "romeo", "open access", "embargo"))
        {
            return null;
        }

        return plan.QueryKind.ToLowerInvariant() switch
        {
            "primary" => ("arxiv_license_policy", ArxivLicenseSource),
            "publisher_policy" => ("sherpa_romeo_policy_registry", SherpaRomeoSource),
            "paper_focus" => ("arxiv_submit_policy", ArxivSubmitSource),
            "official" => ("sherpa_romeo_policy_registry", SherpaRomeoSource),
            _ => null
        };
    }

    private static (string Family, WebSearchDocument Document)? ResolveHeatPumpSourceFamily(WebSearchRequest request, WebSearchPlan plan)
    {
        if (!IsAllowedQueryKind(plan.QueryKind, "primary", "freshness", "paper_focus", "evidence"))
        {
            return null;
        }

        var combined = $"{request.Query} {plan.Query}";
        if (!ContainsAny(combined, "heat pump", "heat pumps", "теплов", "насос"))
        {
            return null;
        }

        return plan.QueryKind.ToLowerInvariant() switch
        {
            "primary" => ("iea_heat_pumps_topic", IeaHeatPumpsSource),
            "freshness" => ("iea_future_heat_pumps_report", IeaFutureHeatPumpsSource),
            "paper_focus" => ("doe_heat_pump_systems", EnergySaverHeatPumpsSource),
            "evidence" => ("iea_future_heat_pumps_report", IeaFutureHeatPumpsSource),
            _ => null
        };
    }

    private static (string Family, WebSearchDocument Document)? ResolveUzbekistanTaxSourceFamily(WebSearchRequest request, WebSearchPlan plan)
    {
        if (!IsAllowedQueryKind(plan.QueryKind, "primary", "official", "freshness"))
        {
            return null;
        }

        var combined = $"{request.Query} {plan.Query}";
        if (!ContainsAny(combined, "uzbekistan", "узбекистан", "soliq", "lex.uz", "my.gov.uz"))
        {
            return null;
        }

        if (!ContainsAny(combined, "tax", "налог", "инвойс", "invoice", "remote worker", "filing", "отчетност"))
        {
            return null;
        }

        return plan.QueryKind.ToLowerInvariant() switch
        {
            "primary" => ("uzbekistan_tax_authority", SoliqSource),
            "official" => ("uzbekistan_legislation_database", LexUzSource),
            "freshness" => ("uzbekistan_public_services", MyGovUzSource),
            _ => null
        };
    }

    private static (string Family, WebSearchDocument Document)? ResolveEuDroneCustomsSourceFamily(WebSearchRequest request, WebSearchPlan plan)
    {
        if (!IsAllowedQueryKind(plan.QueryKind, "primary", "official", "freshness"))
        {
            return null;
        }

        var combined = $"{request.Query} {plan.Query}";
        if (!ContainsAny(combined, "european union", "eu ", " e.u.", "ес", "евросою", "easa"))
        {
            return null;
        }

        if (!ContainsAny(combined, "drone", "drones", "uas", "дрон", "customs", "import", "ce marking", "battery", "batteries", "тамож", "ввоз", "батар"))
        {
            return null;
        }

        return plan.QueryKind.ToLowerInvariant() switch
        {
            "primary" => ("eu_easa_civil_drones", EasaCivilDronesSource),
            "official" => ("eu_customs_import_export", EuCustomsProceduresSource),
            "freshness" => ("eu_single_market_drones", EuDronesSectorSource),
            _ => null
        };
    }

    private static bool ShouldAddEuAiActSourceFamily(WebSearchRequest request, WebSearchPlan plan)
    {
        if (!plan.QueryKind.Equals("primary", StringComparison.OrdinalIgnoreCase) &&
            !plan.QueryKind.Equals("official", StringComparison.OrdinalIgnoreCase) &&
            !plan.QueryKind.Equals("freshness", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var combined = $"{request.Query} {plan.Query}";
        return ContainsAny(
                   combined,
                   "ai act",
                   "artificial intelligence act",
                   "регулирование ии",
                   "регулировании ии",
                   "искусственного интеллекта") &&
               ContainsAny(
                   combined,
                   "european union",
                   "european commission",
                   "ai office",
                   "software vendor",
                   "provider obligations",
                   "ес",
                   "евросою");
    }

    private static (string Family, WebSearchDocument Document)? ResolveEuAiActSeed(string queryKind)
    {
        return queryKind.ToLowerInvariant() switch
        {
            "primary" => ("eu_ai_act_service_desk", EuAiActServiceDeskSource),
            "official" => ("eu_ai_act_gpai_provider_guidelines", EuAiActGpaiProviderGuidelinesSource),
            "freshness" => ("eu_ai_act_regulatory_framework", EuAiActRegulatoryFrameworkSource),
            _ => null
        };
    }

    private static bool SameUrl(string? left, string right)
    {
        return string.Equals(NormalizeUrl(left), NormalizeUrl(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedQueryKind(string queryKind, params string[] allowed)
    {
        return allowed.Any(kind => queryKind.Equals(kind, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().TrimEnd('/');
    }

    private static bool ContainsAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
