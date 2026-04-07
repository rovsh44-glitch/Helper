using System.Text.RegularExpressions;
using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.WebResearch.Quality;

public interface IWebDocumentQualityPolicy
{
    WebDocumentQualityDecision Evaluate(WebSearchDocument document, string stage, string? query = null);
}

public sealed record WebDocumentQualityDecision(
    bool Allowed,
    string Reason,
    IReadOnlyList<string> Signals,
    IReadOnlyList<string> Trace);

public sealed class WebDocumentQualityPolicy : IWebDocumentQualityPolicy
{
    private static readonly Regex StrongMachineErrorRegex = new(
        "(unexpected token|syntaxerror|not valid json|failed to fetch|application error|runtime error|parsererror)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HtmlShellRegex = new(
        "(!doctype html|<html\\b|<body\\b|<script\\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsRequirementRegex = new(
        "(javascript is required|enable javascript|please turn on javascript)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SiteChromeRegex = new(
        "(skip to content|sign in|sign up|enterprise platform|advanced security|pricing|marketplace|contact sales|cookie policy|privacy policy|terms of service|saved searches|search code|pull requests|issues|actions|repositories)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LowSignalInteractiveRegex = new(
        "(играть онлайн|яндекс игры|яндекс маркет|словарь|перевод|translate|dictionary|spelling|spell|как пишется|правописани|challenge|челлендж|question|q&a|ответы mail|ответы на вопросы|discussion|comments?|reddit|stackexchange|forum|community|otvet\\.mail\\.ru)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AntiBotInterstitialRegex = new(
        "(just a moment|unusual activity|security check|complete the security check|verification successful|checking your browser|verify you are human|captcha|waiting for .* to respond|cloudflare|ray id)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public WebDocumentQualityDecision Evaluate(WebSearchDocument document, string stage, string? query = null)
    {
        var signals = new List<string>();
        var corpus = BuildCorpus(document);
        var requestProfile = ResearchRequestProfileResolver.From(query);
        var queryProfile = SourceAuthorityScorer.BuildQueryProfile(query, null);
        var trustProfile = ResolveTrustProfile(document.Url);
        var evidenceSensitive = queryProfile.EvidenceHeavy || queryProfile.MedicalEvidenceHeavy;
        var freshnessOrOfficialSensitive = queryProfile.CurrentnessHeavy && queryProfile.OfficialBias;
        var regulationFreshnessSensitive = freshnessOrOfficialSensitive && LooksLikeRegulationFreshnessQuery(query);
        var strongMedicalEvidenceSource = queryProfile.MedicalEvidenceHeavy && LooksLikeStrongMedicalEvidenceSource(document, corpus);
        var strongFreshnessOrOfficialSource = freshnessOrOfficialSensitive && LooksLikeStrongFreshnessOrOfficialSource(document, corpus, trustProfile);

        if (string.IsNullOrWhiteSpace(corpus))
        {
            return Allow("empty_corpus", stage, document.Url, signals);
        }

        if (StrongMachineErrorRegex.IsMatch(corpus))
        {
            signals.Add("machine_error_markers");
        }

        if (HtmlShellRegex.IsMatch(corpus))
        {
            signals.Add("html_shell_markers");
        }

        if (JsRequirementRegex.IsMatch(corpus))
        {
            signals.Add("javascript_required");
        }

        if (SiteChromeRegex.IsMatch(corpus))
        {
            signals.Add("site_chrome_markers");
        }

        if (!requestProfile.IsDocumentAnalysis && HasLowQueryOverlap(query, corpus))
        {
            signals.Add("low_query_overlap");
        }

        if (evidenceSensitive && LowSignalInteractiveRegex.IsMatch($"{document.Url} {corpus}"))
        {
            signals.Add("interactive_or_ugc_for_evidence_query");
        }

        if (AntiBotInterstitialRegex.IsMatch(corpus))
        {
            signals.Add("anti_bot_interstitial");
        }

        if (freshnessOrOfficialSensitive &&
            trustProfile.IsLowTrust &&
            !strongFreshnessOrOfficialSource)
        {
            signals.Add("low_trust_for_freshness_or_official_query");
        }

        if (regulationFreshnessSensitive &&
            !LooksLikeStrongRegulatoryFreshnessSource(document, corpus, trustProfile))
        {
            signals.Add("regulatory_source_mismatch");
        }

        if (requestProfile.IsDocumentAnalysis &&
            signals.Contains("site_chrome_markers", StringComparer.Ordinal) &&
            !LooksLikeSubstantiveDocument(document))
        {
            signals.Add("document_analysis_without_document_content");
        }

        var reject = signals.Contains("machine_error_markers", StringComparer.Ordinal) ||
                     (signals.Contains("html_shell_markers", StringComparer.Ordinal) &&
                      signals.Contains("javascript_required", StringComparer.Ordinal)) ||
                     signals.Contains("document_analysis_without_document_content", StringComparer.Ordinal) ||
                     signals.Contains("interactive_or_ugc_for_evidence_query", StringComparer.Ordinal) ||
                     (evidenceSensitive &&
                     signals.Contains("low_query_overlap", StringComparer.Ordinal) &&
                     !strongMedicalEvidenceSource) ||
                     (freshnessOrOfficialSensitive &&
                      signals.Contains("low_query_overlap", StringComparer.Ordinal) &&
                      !strongFreshnessOrOfficialSource) ||
                     signals.Contains("low_trust_for_freshness_or_official_query", StringComparer.Ordinal) ||
                     signals.Contains("regulatory_source_mismatch", StringComparer.Ordinal) ||
                     signals.Contains("anti_bot_interstitial", StringComparer.Ordinal) ||
                     (signals.Contains("site_chrome_markers", StringComparer.Ordinal) &&
                      signals.Contains("low_query_overlap", StringComparer.Ordinal));

        return reject
            ? Reject("diagnostic_or_shell_content", stage, document.Url, signals)
            : Allow("usable_content", stage, document.Url, signals);
    }

    private static string BuildCorpus(WebSearchDocument document)
    {
        var segments = new List<string>(5);
        Append(segments, document.Title);
        Append(segments, document.Snippet);
        Append(segments, TryBuildDecodedUrlCorpus(document.Url));
        if (document.ExtractedPage is not null)
        {
            Append(segments, document.ExtractedPage.Title);
            Append(segments, document.ExtractedPage.Body.Length <= 1200
                ? document.ExtractedPage.Body
                : document.ExtractedPage.Body[..1200]);
        }

        return string.Join("\n", segments);
    }

    private static string? TryBuildDecodedUrlCorpus(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return $"{Uri.UnescapeDataString(uri.Host)} {Uri.UnescapeDataString(uri.AbsolutePath)}";
    }

    private static bool LooksLikeSubstantiveDocument(WebSearchDocument document)
    {
        if (document.ExtractedPage is { ContentType: not null } extractedPage &&
            extractedPage.ContentType.Contains("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var bodyLength = document.ExtractedPage?.Body?.Length ?? 0;
        var passageCount = document.ExtractedPage?.Passages.Count ?? 0;
        if (bodyLength >= 320 && passageCount > 0)
        {
            return true;
        }

        var snippet = document.Snippet ?? string.Empty;
        return snippet.Length >= 220 && !SiteChromeRegex.IsMatch(snippet);
    }

    private static bool HasLowQueryOverlap(string? query, string corpus)
    {
        var overlapRatio = SourceAuthorityScorer.ComputeQueryOverlapRatio(query, corpus);
        return overlapRatio < 0.12d;
    }

    private static bool LooksLikeStrongMedicalEvidenceSource(WebSearchDocument document, string corpus)
    {
        if (!Uri.TryCreate(document.Url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Host.Contains("pubmed.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("pmc.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("cochrane.org", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("who.int", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("cdc.gov", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("nih.gov", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("systematic review", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("meta-analysis", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("guideline", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("клиничес", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("рекомендац", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeStrongFreshnessOrOfficialSource(
        WebSearchDocument document,
        string corpus,
        DomainTrustProfile trustProfile)
    {
        if (!Uri.TryCreate(document.Url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // Freshness/official queries should not treat low-trust mirrors or aggregators
        // as strong sources purely because they repeat topical keywords.
        if (trustProfile.IsLowTrust)
        {
            return false;
        }

        var host = uri.Host;
        if (host.EndsWith(".gov", StringComparison.OrdinalIgnoreCase) ||
            host.Contains(".gov.", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".int", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("irs.gov", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("sec.gov", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("minzdrav", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("medelement.com", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("consultant.ru", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("garant.ru", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("nalog", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return corpus.Contains("official", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("guideline", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("recommendation", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("регламент", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("рекомендац", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("налог", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("deadline", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("срок", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeStrongRegulatoryFreshnessSource(
        WebSearchDocument document,
        string corpus,
        DomainTrustProfile trustProfile)
    {
        if (!Uri.TryCreate(document.Url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (trustProfile.IsAuthoritative ||
            trustProfile.Label is "trusted_legal_reference" or "legal_document_host" or "major_business_news" or "major_newswire" or "government_primary")
        {
            return ContainsAny(
                $"{uri.Host} {uri.AbsolutePath} {corpus}",
                "tax", "threshold", "deadline", "reporting", "filing", "irs", "sec",
                "налог", "порог", "лимит", "срок", "отчетност", "отчётност");
        }

        return false;
    }

    private static DomainTrustProfile ResolveTrustProfile(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return LowTrustDomainRegistry.Resolve(null);
        }

        return LowTrustDomainRegistry.Resolve(uri.Host);
    }

    private static bool LooksLikeRegulationFreshnessQuery(string? query)
    {
        return ContainsAny(
            query ?? string.Empty,
            "tax", "threshold", "deadline", "reporting", "filing", "regulation",
            "налог", "порог", "лимит", "срок", "отчетност", "отчётност", "регуляц");
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

    private static void Append(List<string> target, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add(value.Trim());
        }
    }

    private static WebDocumentQualityDecision Allow(string reason, string stage, string url, IReadOnlyList<string> signals)
    {
        return new WebDocumentQualityDecision(
            true,
            reason,
            signals,
            new[]
            {
                $"web_document_quality.allowed=yes stage={stage} reason={reason} url={url} signals={(signals.Count == 0 ? "none" : string.Join(",", signals))}"
            });
    }

    private static WebDocumentQualityDecision Reject(string reason, string stage, string url, IReadOnlyList<string> signals)
    {
        return new WebDocumentQualityDecision(
            false,
            reason,
            signals,
            new[]
            {
                $"web_document_quality.allowed=no stage={stage} reason={reason} url={url} signals={(signals.Count == 0 ? "none" : string.Join(",", signals))}"
            });
    }
}

