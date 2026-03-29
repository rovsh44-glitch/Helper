using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Providers;

namespace Helper.Runtime.Tests;

public sealed class WebSearchProviderMuxTests
{
    [Fact]
    public async Task SearchAsync_UsesPrimaryProvider_WhenItReturnsResults()
    {
        var primary = new StubProvider(LocalSearchProvider.Id, new[]
        {
            new WebSearchDocument("https://primary.example/doc", "Primary", "Primary result.")
        });
        var secondary = new StubProvider(SearxSearchProvider.Id, new[]
        {
            new WebSearchDocument("https://secondary.example/doc", "Secondary", "Secondary result.")
        });
        var mux = new WebSearchProviderMux(primary, secondary);

        var response = await mux.SearchAsync(
            new WebSearchPlan("latest", 5, 1, "research", "standard", true),
            CancellationToken.None);

        Assert.Single(response.Documents);
        Assert.Equal("https://primary.example/doc", response.Documents[0].Url);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, secondary.CallCount);
        Assert.Contains(response.Trace, line => line.Contains("local", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_FallsBackToSecondary_WhenPrimaryReturnsEmpty()
    {
        var primary = new StubProvider(LocalSearchProvider.Id, Array.Empty<WebSearchDocument>());
        var secondary = new StubProvider(SearxSearchProvider.Id, new[]
        {
            new WebSearchDocument("https://secondary.example/doc", "Secondary", "Secondary result.")
        });
        var mux = new WebSearchProviderMux(primary, secondary);

        var response = await mux.SearchAsync(
            new WebSearchPlan("latest", 5, 1, "research", "standard", true),
            CancellationToken.None);

        Assert.Single(response.Documents);
        Assert.Equal("https://secondary.example/doc", response.Documents[0].Url);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, secondary.CallCount);
    }

    [Fact]
    public async Task SearchAsync_FallsBackToSecondary_WhenPrimaryThrows()
    {
        var primary = new StubProvider(LocalSearchProvider.Id, throwOnSearch: true);
        var secondary = new StubProvider(SearxSearchProvider.Id, new[]
        {
            new WebSearchDocument("https://secondary.example/doc", "Secondary", "Secondary result.")
        });
        var mux = new WebSearchProviderMux(primary, secondary);

        var response = await mux.SearchAsync(
            new WebSearchPlan("latest", 5, 1, "research", "standard", true),
            CancellationToken.None);

        Assert.Single(response.Documents);
        Assert.Equal("https://secondary.example/doc", response.Documents[0].Url);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, secondary.CallCount);
        Assert.Contains(response.Trace, line => line.Contains("local:error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_PreservesBlockedPrimaryTrace_WhenSecondarySucceeds()
    {
        var primary = new StubProvider(
            LocalSearchProvider.Id,
            trace: new[] { "local:web_fetch.blocked reason=private_or_loopback_address target=http://169.254.169.254" });
        var secondary = new StubProvider(SearxSearchProvider.Id, new[]
        {
            new WebSearchDocument("https://secondary.example/doc", "Secondary", "Secondary result.")
        });
        var mux = new WebSearchProviderMux(primary, secondary);

        var response = await mux.SearchAsync(
            new WebSearchPlan("latest", 5, 1, "research", "standard", true),
            CancellationToken.None);

        Assert.Single(response.Documents);
        Assert.Contains(response.Trace, line => line.Contains("private_or_loopback_address", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_SkipsProvider_InCooldownAndUsesSecondary()
    {
        var healthState = new WebProviderHealthState();
        healthState.Record(LocalSearchProvider.Id, WebProviderExecutionOutcome.Timeout, TimeSpan.FromSeconds(2));
        healthState.Record(LocalSearchProvider.Id, WebProviderExecutionOutcome.Timeout, TimeSpan.FromSeconds(2));

        var primary = new StubProvider(LocalSearchProvider.Id, new[]
        {
            new WebSearchDocument("https://primary.example/doc", "Primary", "Primary result.")
        });
        var secondary = new StubProvider(SearxSearchProvider.Id, new[]
        {
            new WebSearchDocument("https://secondary.example/doc", "Secondary", "Secondary result.")
        });
        var mux = new WebSearchProviderMux(
            new[] { primary, secondary },
            healthState,
            new SearchCostBudgetPolicy(),
            new TurnLatencyBudgetPolicy());

        var response = await mux.SearchAsync(
            new WebSearchPlan("latest", 5, 1, "research", "standard", true),
            CancellationToken.None);

        Assert.Single(response.Documents);
        Assert.Equal("https://secondary.example/doc", response.Documents[0].Url);
        Assert.Equal(0, primary.CallCount);
        Assert.Equal(1, secondary.CallCount);
        Assert.Contains(response.Trace, line => line.Contains("status=cooldown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_PrefersCheaperProvider_WhenConfiguredOrderStartsWithSearx()
    {
        var previousOrder = Environment.GetEnvironmentVariable("HELPER_WEB_SEARCH_PROVIDER_ORDER");
        Environment.SetEnvironmentVariable("HELPER_WEB_SEARCH_PROVIDER_ORDER", "searx,local");

        try
        {
            var primary = new StubProvider(SearxSearchProvider.Id, new[]
            {
                new WebSearchDocument("https://searx.example/doc", "Searx", "Searx result.")
            });
            var secondary = new StubProvider(LocalSearchProvider.Id, new[]
            {
                new WebSearchDocument("https://local.example/doc", "Local", "Local result.")
            });
            var mux = new WebSearchProviderMux(primary, secondary);

            var response = await mux.SearchAsync(
                new WebSearchPlan("latest", 5, 1, "research", "standard", true),
                CancellationToken.None);

            Assert.Single(response.Documents);
            Assert.Equal("https://local.example/doc", response.Documents[0].Url);
            Assert.Equal(0, primary.CallCount);
            Assert.Equal(1, secondary.CallCount);
            Assert.Contains(response.Trace, line => line.Contains("provider_governance.selection provider=local", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_WEB_SEARCH_PROVIDER_ORDER", previousOrder);
        }
    }

    [Fact]
    public async Task SearchAsync_ReportsDegradedMode_WhenGovernanceSkipsAllProviders()
    {
        var primary = new StubProvider(LocalSearchProvider.Id, new[]
        {
            new WebSearchDocument("https://primary.example/doc", "Primary", "Primary result.")
        });
        var secondary = new StubProvider(SearxSearchProvider.Id, new[]
        {
            new WebSearchDocument("https://secondary.example/doc", "Secondary", "Secondary result.")
        });
        var mux = new WebSearchProviderMux(
            new[] { primary, secondary },
            new WebProviderHealthState(),
            new ZeroBudgetCostPolicy(),
            new TurnLatencyBudgetPolicy());

        var response = await mux.SearchAsync(
            new WebSearchPlan("latest", 5, 1, "research", "standard", true),
            CancellationToken.None);

        Assert.Empty(response.Documents);
        Assert.Equal(0, primary.CallCount);
        Assert.Equal(0, secondary.CallCount);
        Assert.Contains(response.Trace, line => line.Contains("provider_governance.degraded_mode=yes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.Trace, line =>
            line.Contains("provider_governance.cost", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("allowed=no", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubProvider : IWebSearchProvider
    {
        private readonly IReadOnlyList<WebSearchDocument> _documents;
        private readonly IReadOnlyList<string> _trace;
        private readonly bool _throwOnSearch;

        public StubProvider(
            string providerId,
            IReadOnlyList<WebSearchDocument>? documents = null,
            IReadOnlyList<string>? trace = null,
            bool throwOnSearch = false)
        {
            ProviderId = providerId;
            _documents = documents ?? Array.Empty<WebSearchDocument>();
            _trace = trace ?? new[] { $"{providerId}:stub" };
            _throwOnSearch = throwOnSearch;
        }

        public string ProviderId { get; }

        public bool IsEnabled { get; set; } = true;

        public int CallCount { get; private set; }

        public Task<WebSearchProviderClientResponse> SearchAsync(WebSearchPlan plan, CancellationToken ct = default)
        {
            CallCount++;
            if (_throwOnSearch)
            {
                throw new InvalidOperationException($"{ProviderId} failed");
            }

            return Task.FromResult(new WebSearchProviderClientResponse(_documents, _trace));
        }
    }

    private sealed class ZeroBudgetCostPolicy : ISearchCostBudgetPolicy
    {
        public SearchCostBudget Resolve(WebSearchPlan plan)
        {
            return new SearchCostBudget(0, new[] { "provider_governance.cost_budget max_units=0 search_mode=standard query_kind=primary" });
        }

        public ProviderCostDecision Evaluate(string providerId, WebSearchPlan plan, int spentUnits, SearchCostBudget budget)
        {
            return new ProviderCostDecision(
                false,
                1,
                100,
                "cost_budget_exhausted",
                new[]
                {
                    $"provider_governance.cost provider={providerId} allowed=no reason=cost_budget_exhausted spent={spentUnits} cost_units=1 max_units={budget.MaxUnits}"
                });
        }
    }
}

