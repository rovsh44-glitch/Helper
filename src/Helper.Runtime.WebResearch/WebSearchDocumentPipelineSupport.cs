namespace Helper.Runtime.WebResearch;

internal static class WebSearchDocumentPipelineSupport
{
    public static string BuildSnippetFromPage(ExtractedWebPage extractedPage, string fallbackSnippet)
    {
        if (extractedPage.Passages.Count > 0)
        {
            var combined = string.Join(" ", extractedPage.Passages.Take(2).Select(static passage => passage.Text.Trim()));
            if (!string.IsNullOrWhiteSpace(combined))
            {
                return combined.Length <= 420
                    ? combined
                    : combined[..420].TrimEnd() + "...";
            }
        }

        if (!string.IsNullOrWhiteSpace(extractedPage.Body))
        {
            return extractedPage.Body.Length <= 420
                ? extractedPage.Body
                : extractedPage.Body[..420].TrimEnd() + "...";
        }

        return fallbackSnippet;
    }

    public static bool IsHttpUrl(string? candidate)
    {
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
               (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }
}

