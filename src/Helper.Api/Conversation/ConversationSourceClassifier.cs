using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal static class ConversationSourceClassifier
{
    public static bool HasWebSource(ChatTurnContext context)
    {
        return GetWebSources(context).Count > 0;
    }

    public static bool HasLocalSource(ChatTurnContext context)
    {
        return GetLocalSources(context).Count > 0;
    }

    public static IReadOnlyList<string> GetWebSources(ChatTurnContext context)
    {
        var sources = context.ResearchEvidenceItems.Count > 0
            ? context.ResearchEvidenceItems
                .Where(IsWebEvidence)
                .Select(FormatEvidenceSourceForDisplay)
            : context.Sources.Where(IsHttpSource);

        return sources
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> GetLocalSources(ChatTurnContext context)
    {
        var localFromEvidence = context.ResearchEvidenceItems
            .Where(IsLocalEvidence)
            .Select(FormatEvidenceSourceForDisplay);
        var localFromSources = context.Sources
            .Where(static source => !string.IsNullOrWhiteSpace(source) && !IsHttpSource(source));

        return localFromEvidence
            .Concat(localFromSources)
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool HasLiveWebAttempt(ChatTurnContext context)
    {
        return context.ToolCalls.Contains("research.search", StringComparer.OrdinalIgnoreCase) ||
               !string.Equals(context.ResolvedLiveWebRequirement, "no_web_needed", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsHttpSource(string? source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
               (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    public static string ResolveLayer(ResearchEvidenceItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourceLayer))
        {
            return item.SourceLayer.Trim().ToLowerInvariant();
        }

        if (string.Equals(item.EvidenceKind, "local_library_chunk", StringComparison.OrdinalIgnoreCase) ||
            !IsHttpSource(item.Url))
        {
            return "local_library";
        }

        return "web";
    }

    public static string ResolveFormat(ResearchEvidenceItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourceFormat))
        {
            return item.SourceFormat.Trim().TrimStart('.').ToLowerInvariant();
        }

        if (string.Equals(item.EvidenceKind, "fetched_document_pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "pdf";
        }

        if (Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.').Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return extension is "htm" ? "html" : extension;
            }
        }

        return IsHttpSource(item.Url) ? "html" : "unknown";
    }

    public static string FormatEvidenceSourceForDisplay(ResearchEvidenceItem item)
    {
        if (string.Equals(ResolveLayer(item), "local_library", StringComparison.OrdinalIgnoreCase))
        {
            var title = item.DisplayTitle ?? item.Title;
            var id = string.IsNullOrWhiteSpace(item.SourceId) ? "unknown" : item.SourceId.Trim();
            var format = ResolveFormat(item);
            var locator = string.IsNullOrWhiteSpace(item.Locator) ? null : item.Locator.Trim();
            return string.IsNullOrWhiteSpace(locator)
                ? $"{title} ({format}) | id={id}"
                : $"{title} ({format}) | {locator} | id={id}";
        }

        return string.IsNullOrWhiteSpace(item.Url) ? item.Title : item.Url.Trim();
    }

    public static bool IsWebEvidence(ResearchEvidenceItem item)
    {
        return string.Equals(ResolveLayer(item), "web", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLocalEvidence(ResearchEvidenceItem item)
    {
        return string.Equals(ResolveLayer(item), "local_library", StringComparison.OrdinalIgnoreCase);
    }
}
