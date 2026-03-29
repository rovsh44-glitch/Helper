using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Evolution;

public static class PersonaRuntimeFactory
{
    public static IPersonaOrchestrator Create(
        AILink ai,
        IVectorStore vectorStore,
        IWebSearcher webSearcher)
    {
        return new PersonaOrchestrator(ai, vectorStore, webSearcher);
    }
}
