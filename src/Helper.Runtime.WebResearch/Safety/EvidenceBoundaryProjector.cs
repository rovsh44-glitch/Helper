namespace Helper.Runtime.WebResearch.Safety;

public sealed record EvidenceBoundaryProjection(
    ExtractedWebPage Page,
    IReadOnlyList<string> Trace);

public interface IEvidenceBoundaryProjector
{
    EvidenceBoundaryProjection Project(ExtractedWebPage page);
}

public sealed class EvidenceBoundaryProjector : IEvidenceBoundaryProjector
{
    private readonly IWebContentSafetyPolicy _safetyPolicy;
    private readonly IPromptInjectionSanitizer _sanitizer;

    public EvidenceBoundaryProjector(
        IWebContentSafetyPolicy? safetyPolicy = null,
        IPromptInjectionSanitizer? sanitizer = null)
    {
        _safetyPolicy = safetyPolicy ?? new WebContentSafetyPolicy();
        _sanitizer = sanitizer ?? new PromptInjectionSanitizer();
    }

    public EvidenceBoundaryProjection Project(ExtractedWebPage page)
    {
        var assessment = _safetyPolicy.Assess(page);
        var titleResult = _sanitizer.Sanitize(page.Title, assessment.Flags);
        var bodyResult = _sanitizer.Sanitize(page.Body, assessment.Flags);
        var sanitizedPassages = page.Passages
            .Select(passage =>
            {
                var passageFlags = assessment.PassageFlags.TryGetValue(passage.Ordinal, out var flags)
                    ? flags
                    : assessment.Flags;
                var sanitized = _sanitizer.Sanitize(passage.Text, passageFlags);
                return new ExtractedWebPassage(
                    passage.Ordinal,
                    sanitized.Text,
                    assessment.TrustLevel,
                    sanitized.WasSanitized,
                    sanitized.Flags);
            })
            .ToArray();

        var projectionFlags = assessment.Flags
            .Concat(titleResult.Flags)
            .Concat(bodyResult.Flags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static flag => flag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var wasSanitized = titleResult.WasSanitized ||
                           bodyResult.WasSanitized ||
                           sanitizedPassages.Any(static passage => passage.WasSanitized);
        var safeTitle = string.IsNullOrWhiteSpace(titleResult.Text) ? page.Title : titleResult.Text;
        var safeBody = string.IsNullOrWhiteSpace(bodyResult.Text) ? page.Body : bodyResult.Text;

        var projectedPage = new ExtractedWebPage(
            page.RequestedUrl,
            page.ResolvedUrl,
            page.CanonicalUrl,
            safeTitle,
            page.PublishedAt,
            safeBody,
            sanitizedPassages,
            page.ContentType,
            assessment.TrustLevel,
            wasSanitized,
            assessment.InjectionSignalsDetected,
            projectionFlags);

        var trace = new List<string>(assessment.Trace)
        {
            $"web_evidence_boundary.sanitized={(wasSanitized ? "yes" : "no")}",
            $"web_evidence_boundary.passages={sanitizedPassages.Length}"
        };

        if (projectionFlags.Length > 0)
        {
            trace.Add($"web_evidence_boundary.projection_flags={string.Join(",", projectionFlags)}");
        }

        return new EvidenceBoundaryProjection(projectedPage, trace);
    }
}

