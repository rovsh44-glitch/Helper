namespace Helper.Runtime.WebResearch.Extraction;

internal sealed record WebSourceTypeProfile(
    string Kind,
    bool DenseEvidencePreferred,
    bool DocumentLike,
    bool StrictChromeRejection,
    int MinimumUsefulLineLength,
    int MinimumUsefulWordCount,
    int PassageTargetLength,
    int MaxPassages);

internal static class WebSourceTypeClassifier
{
    public static WebSourceTypeProfile Classify(Uri requestedUri, Uri resolvedUri, string? contentType)
    {
        var corpus = $"{requestedUri} {resolvedUri} {contentType ?? string.Empty}";

        if (LooksLikePdf(requestedUri, resolvedUri, contentType))
        {
            return new WebSourceTypeProfile(
                "document_pdf",
                DenseEvidencePreferred: true,
                DocumentLike: true,
                StrictChromeRejection: true,
                MinimumUsefulLineLength: 28,
                MinimumUsefulWordCount: 4,
                PassageTargetLength: 240,
                MaxPassages: 5);
        }

        if (LooksLikeClinicalGuidance(corpus))
        {
            return new WebSourceTypeProfile(
                "clinical_guidance",
                DenseEvidencePreferred: true,
                DocumentLike: true,
                StrictChromeRejection: true,
                MinimumUsefulLineLength: 28,
                MinimumUsefulWordCount: 4,
                PassageTargetLength: 240,
                MaxPassages: 5);
        }

        if (LooksLikeAcademicPaper(corpus))
        {
            return new WebSourceTypeProfile(
                "academic_paper",
                DenseEvidencePreferred: true,
                DocumentLike: true,
                StrictChromeRejection: true,
                MinimumUsefulLineLength: 28,
                MinimumUsefulWordCount: 4,
                PassageTargetLength: 240,
                MaxPassages: 5);
        }

        if (LooksLikeOfficialDocument(corpus))
        {
            return new WebSourceTypeProfile(
                "official_document",
                DenseEvidencePreferred: true,
                DocumentLike: true,
                StrictChromeRejection: true,
                MinimumUsefulLineLength: 28,
                MinimumUsefulWordCount: 4,
                PassageTargetLength: 260,
                MaxPassages: 5);
        }

        if (LooksLikeInteractiveShell(corpus))
        {
            return new WebSourceTypeProfile(
                "interactive_shell",
                DenseEvidencePreferred: false,
                DocumentLike: false,
                StrictChromeRejection: true,
                MinimumUsefulLineLength: 48,
                MinimumUsefulWordCount: 6,
                PassageTargetLength: 320,
                MaxPassages: 4);
        }

        return new WebSourceTypeProfile(
            "general_article",
            DenseEvidencePreferred: false,
            DocumentLike: false,
            StrictChromeRejection: false,
            MinimumUsefulLineLength: 48,
            MinimumUsefulWordCount: 6,
            PassageTargetLength: 320,
            MaxPassages: 4);
    }

    private static bool LooksLikePdf(Uri requestedUri, Uri resolvedUri, string? contentType)
    {
        return requestedUri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
               resolvedUri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(contentType) &&
                contentType.Contains("application/pdf", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeClinicalGuidance(string corpus)
    {
        return ContainsAny(
            corpus,
            "guideline", "guidelines", "clinical", "recommendation", "recommendations",
            "migraine", "measles", "treatment", "prevention", "fact-sheet", "fact sheet",
            "клиничес", "рекомендац", "мигрен", "корь", "лечение", "профилактик");
    }

    private static bool LooksLikeAcademicPaper(string corpus)
    {
        return ContainsAny(
            corpus,
            "arxiv", "abstract", "paper", "journal", "systematic review", "meta-analysis", "pubmed", "pmc",
            "link.springer.com/article/", "springermedicine.com/", "sciencedirect.com/science/article/", "doi.org/",
            "статья", "исследован", "обзор", "мета-анализ", "систематичес");
    }

    private static bool LooksLikeOfficialDocument(string corpus)
    {
        return ContainsAny(
            corpus,
            ".gov", "who.int", "nhs.uk", "nice.org.uk", "consultant.ru", "garant.ru", "policy", "regulation", "official",
            "официаль", "закон", "регуляц", "документ", "minzdrav", "rosminzdrav");
    }

    private static bool LooksLikeInteractiveShell(string corpus)
    {
        return ContainsAny(
            corpus,
            "github.com", "gitlab.com", "/blob/", "saved searches", "pull requests",
            "q&a", "forum", "community", "comments", "discussion", "otvet.mail.ru");
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

