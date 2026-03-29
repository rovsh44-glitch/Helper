namespace Helper.Api.Conversation;

internal static class BenchmarkResponseFormatterFactory
{
    public static BenchmarkResponseFormatter CreateDefault()
    {
        var structurePolicy = new BenchmarkResponseStructurePolicy();
        var qualityPolicy = new BenchmarkDraftQualityPolicy(structurePolicy);
        var topicalBodyExtractor = new BenchmarkTopicalBodyExtractor(structurePolicy, qualityPolicy);
        var assessmentWriter = new BenchmarkResponseAssessmentWriter(qualityPolicy, structurePolicy);
        var sectionRenderer = new BenchmarkResponseSectionRenderer(qualityPolicy, assessmentWriter);
        return new BenchmarkResponseFormatter(structurePolicy, qualityPolicy, topicalBodyExtractor, sectionRenderer);
    }
}

