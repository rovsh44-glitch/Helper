using System.Net;
using System.Net.Http;
using System.Text;
using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Providers;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class LocalSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_RetriesWithCompactedMigraineQuery_AfterTimeout()
    {
        var handler = new StubHandler(request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            if (query.Contains("профилактику мигрени последние клинических рекомендациях", StringComparison.OrdinalIgnoreCase))
            {
                throw new OperationCanceledException("Simulated provider timeout.");
            }

            Assert.Contains("мигрень профилактика клинические рекомендации", query, StringComparison.OrdinalIgnoreCase);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"results":[{"url":"https://example.org/migraine-guideline","title":"Migraine guideline","content":"Guideline summary"}]}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var provider = new LocalSearchProvider(
            "http://localhost:8080",
            securityPolicy: null,
            redirectGuard: null,
            timeoutCompactionPolicy: new LocalSearchTimeoutCompactionPolicy(),
            handler: handler);

        var response = await provider.SearchAsync(
            new WebSearchPlan("профилактику мигрени последние клинических рекомендациях", 5, 1, "research", "standard", true),
            CancellationToken.None);

        var document = Assert.Single(response.Documents);
        Assert.Equal("https://example.org/migraine-guideline", document.Url);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains(response.Trace, line => line.Contains("local:timeout_retry_compaction", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compact_PrediabetesNutritionPrompt_ToMedicalNutritionCore()
    {
        var policy = new LocalSearchTimeoutCompactionPolicy();

        var decision = policy.Compact(
            new WebSearchPlan(
                "Оцени мой дневной рацион при преддиабете и проверь его по свежим официальным рекомендациям: сладкий йогурт утром, рис на обед, фрукты вечером, сок перед сном.",
                5,
                1,
                "research",
                "freshness",
                true,
                "freshness"));

        Assert.True(decision.Applied);
        Assert.Contains("преддиабет", decision.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("питание", decision.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("официальные", decision.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("йогурт", decision.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("сок", decision.Query, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _resolver;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> resolver)
        {
            _resolver = resolver;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_resolver(request));
        }
    }
}
