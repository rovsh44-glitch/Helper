namespace Helper.Api.Conversation;

internal interface IBenchmarkResponseFormatter
{
    bool TryComposeLocalFirstBenchmarkResponse(ChatTurnContext context, string solution, out string formatted);
}

internal sealed class BenchmarkResponseFormatter : IBenchmarkResponseFormatter
{
    private readonly IBenchmarkResponseStructurePolicy _structurePolicy;
    private readonly IBenchmarkDraftQualityPolicy _qualityPolicy;
    private readonly IBenchmarkTopicalBodyExtractor _topicalBodyExtractor;
    private readonly IBenchmarkResponseSectionRenderer _sectionRenderer;

    public BenchmarkResponseFormatter(
        IBenchmarkResponseStructurePolicy structurePolicy,
        IBenchmarkDraftQualityPolicy qualityPolicy,
        IBenchmarkTopicalBodyExtractor topicalBodyExtractor,
        IBenchmarkResponseSectionRenderer sectionRenderer)
    {
        _structurePolicy = structurePolicy;
        _qualityPolicy = qualityPolicy;
        _topicalBodyExtractor = topicalBodyExtractor;
        _sectionRenderer = sectionRenderer;
    }

    public bool TryComposeLocalFirstBenchmarkResponse(ChatTurnContext context, string solution, out string formatted)
    {
        formatted = string.Empty;
        if (!_structurePolicy.RequiresLocalFirstBenchmarkSections(context.Request.SystemInstruction))
        {
            return false;
        }

        var normalized = _structurePolicy.StripFollowUpTail(solution).TrimEnd();
        var sanitized = _qualityPolicy.StripMetaFallbackContent(normalized).Trim();
        var hasHeadings = _structurePolicy.ContainsAllSections(normalized);
        var rawFallback = _qualityPolicy.LooksLikeResearchFallback(normalized);
        var rawLowQuality = _qualityPolicy.LooksLowQualityBenchmarkDraft(context, normalized);
        var topicalBody = _topicalBodyExtractor.TryExtract(context, sanitized, out var extractedBody)
            ? extractedBody
            : null;
        if (string.IsNullOrWhiteSpace(topicalBody) &&
            BenchmarkEvidenceFallbackSummaryBuilder.TryBuild(context, out var evidenceBody))
        {
            topicalBody = evidenceBody;
        }

        if (hasHeadings)
        {
            if (!rawLowQuality && !rawFallback)
            {
                formatted = normalized;
                return true;
            }

            formatted = _sectionRenderer.BuildResponse(context, normalized, isFallback: true, topicalBody: null);
            return true;
        }

        var isFallback = rawFallback || rawLowQuality || _qualityPolicy.ContainsMetaUncertainty(normalized);
        formatted = _sectionRenderer.BuildResponse(context, normalized, isFallback, topicalBody);
        return true;
    }




}

