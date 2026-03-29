using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Providers;

namespace Helper.Runtime.Infrastructure
{
    public class SearxSearcher : WebSearcher
    {
        public SearxSearcher(string baseUrl = "http://localhost:8080")
            : base(new WebSearchProviderMux(new SearxSearchProvider(baseUrl)))
        {
        }
    }
}

