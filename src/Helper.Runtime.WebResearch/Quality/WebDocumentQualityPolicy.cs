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
        var evidenceSensitive = queryProfile.EvidenceHeavy || queryProfile.MedicalEvidenceHeavy;
        var strongMedicalEvidenceSource = queryProfile.MedicalEvidenceHeavy && LooksLikeStrongMedicalEvidenceSource(document, corpus);

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

