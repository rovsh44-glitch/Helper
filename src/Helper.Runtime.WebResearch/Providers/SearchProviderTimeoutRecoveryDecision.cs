namespace Helper.Runtime.WebResearch.Providers;

public sealed record SearchProviderTimeoutRecoveryDecision(
    WebSearchPlan Plan,
    IReadOnlyList<string> Trace);
