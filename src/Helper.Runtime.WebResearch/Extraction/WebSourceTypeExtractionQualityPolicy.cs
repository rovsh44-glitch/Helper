namespace Helper.Runtime.WebResearch.Extraction;

internal sealed record WebSourceTypeExtractionQualityDecision(
    bool Allowed,
    string ReasonCode,
    IReadOnlyList<string> Trace);

internal sealed class WebSourceTypeExtractionQualityPolicy
{
    public WebSourceTypeExtractionQualityDecision Evaluate(WebSourceTypeExtractionRequest request, ExtractedWebPage extractedPage)
    {
        var bodyWordCount = CountWords(extractedPage.Body);
        var chromeMatches = WebChromePatternCatalog.CountMatches($"{extractedPage.Title}\n{extractedPage.Body}");
        var trace = new List<string>
        {
            $"web_extract.quality kind={request.SourceType.Kind} words={bodyWordCount} passages={extractedPage.Passages.Count} chrome_matches={chromeMatches}"
        };

        if (request.SourceType.Kind.Equals("interactive_shell", StringComparison.Ordinal))
        {
            if (chromeMatches >= 1 || bodyWordCount < 70)
            {
                trace.Add("web_extract.quality_rejected reason=interactive_shell_contamination");
                return new WebSourceTypeExtractionQualityDecision(false, "interactive_shell_contamination", trace);
            }
        }
        else if (request.SourceType.DocumentLike)
        {
            if (chromeMatches >= 2 && bodyWordCount < 120)
            {
                trace.Add("web_extract.quality_rejected reason=document_like_shell_contamination");
                return new WebSourceTypeExtractionQualityDecision(false, "document_like_shell_contamination", trace);
            }
        }
        else if (request.SourceType.Kind.Equals("general_article", StringComparison.Ordinal) &&
                 chromeMatches >= 3 &&
                 bodyWordCount < 90)
        {
            trace.Add("web_extract.quality_rejected reason=general_shell_contamination");
            return new WebSourceTypeExtractionQualityDecision(false, "general_shell_contamination", trace);
        }

        trace.Add("web_extract.quality_allowed=yes");
        return new WebSourceTypeExtractionQualityDecision(true, "allowed", trace);
    }

    private static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return text
            .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }
}

