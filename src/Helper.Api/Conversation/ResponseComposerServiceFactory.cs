namespace Helper.Api.Conversation;

internal static class ResponseComposerServiceFactory
{
    public static ResponseComposerService CreateDefault()
    {
        var variationPolicy = new ConversationVariationPolicy();
        return new ResponseComposerService(
            new DialogActPlanner(),
            variationPolicy,
            new ResponseTextDeduplicator(),
            new AnswerShapePolicy(),
            new NextStepComposer(variationPolicy),
            new ComposerLocalizationResolver(new TurnLanguageResolver()),
            BenchmarkResponseFormatterFactory.CreateDefault());
    }
}

