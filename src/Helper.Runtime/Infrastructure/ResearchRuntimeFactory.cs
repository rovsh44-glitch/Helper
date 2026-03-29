using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Providers;

namespace Helper.Runtime.Infrastructure;

public static class ResearchRuntimeFactory
{
    public static SimpleResearcher CreateSimpleResearcher(
        AILink ai,
        ICodeExecutor codeExecutor,
        IVectorStore vectorStore,
        IWebSearchSessionCoordinator searchSessionCoordinator,
        ILocalBaselineAnswerService? localBaselineAnswerService = null)
    {
        return new SimpleResearcher(ai, codeExecutor, vectorStore, searchSessionCoordinator, localBaselineAnswerService);
    }

    public static SimpleResearcher CreateSimpleResearcher(
        AILink ai,
        ICodeExecutor codeExecutor,
        IWebSearcher webSearcher,
        IVectorStore vectorStore)
    {
        return new SimpleResearcher(ai, codeExecutor, webSearcher, vectorStore);
    }

    public static IResearchEngine CreateResearchEngine(
        IResearchService researcher,
        ILibrarianAgent librarian,
        IGoalManager goalManager,
        IStrategicPlanner strategicPlanner,
        ICriticService criticService,
        AILink ai)
    {
        return new ResearchEngine(researcher, librarian, goalManager, strategicPlanner, criticService, ai);
    }
}
