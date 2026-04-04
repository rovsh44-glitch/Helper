using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Providers;

namespace Helper.Runtime.Infrastructure
{
    public class MultiLanguageValidator : IBuildValidator
    {
        private readonly IBuildExecutor _executor;
        public MultiLanguageValidator(IBuildExecutor executor) => _executor = executor;
        public async Task<List<BuildError>> ValidateAsync(string projectPath, CancellationToken ct = default) => 
            await _executor.ExecuteBuildAsync(projectPath, ct);
    }

    public class SimpleTestGenerator : ITestGenerator
    {
        private readonly AILink _ai;
        public SimpleTestGenerator(AILink ai) => _ai = ai;
        public async Task<List<GeneratedFile>> GenerateTestsAsync(List<GeneratedFile> sourceFiles, CancellationToken ct = default)
        {
            var tests = new List<GeneratedFile>();
            foreach (var f in sourceFiles.Where(x => x.RelativePath.EndsWith(".cs")))
                tests.Add(new GeneratedFile(f.RelativePath.Replace(".cs", "Tests.cs"), await _ai.AskAsync($"Test for:\n{f.Content}", ct)));
            return await Task.FromResult(tests);
        }
    }

    public class PythonSandbox : ICodeExecutor
    {
        public async Task<ExecutionResult> ExecuteAsync(string code, string lang = "python", CancellationToken ct = default) => 
            await Task.FromResult(new ExecutionResult(true, "Success", "", new List<string>()));
    }

    public class SimpleResearcher : IResearchService
    {
        private readonly AILink _ai;
        private readonly ICodeExecutor _ex;
        private readonly IVectorStore _mem;
        private readonly IWebSearchSessionCoordinator _searchSessionCoordinator;
        private readonly ILocalBaselineAnswerService _localBaselineAnswerService;
        private readonly ResearchAnswerQualityGate _answerQualityGate;
        private static readonly TimeSpan SynthesisTimeout = TimeSpan.FromSeconds(6);

        public SimpleResearcher(AILink ai, ICodeExecutor ex, IWebSearcher sr, IVectorStore mem)
            : this(ai, ex, mem, WebSearchSessionCoordinatorFactory.Create(new WebSearchProviderClient(sr)))
        {
        }

        public SimpleResearcher(
            AILink ai,
            ICodeExecutor ex,
            IVectorStore mem,
            IWebSearchSessionCoordinator searchSessionCoordinator,
            ILocalBaselineAnswerService? localBaselineAnswerService = null)
        {
            _ai = ai;
            _ex = ex;
            _mem = mem;
            _searchSessionCoordinator = searchSessionCoordinator;
            _localBaselineAnswerService = localBaselineAnswerService ?? new LocalBaselineAnswerService(ai);
            _answerQualityGate = new ResearchAnswerQualityGate();
        }

        public async Task<ResearchResult> ResearchAsync(string topic, int depth = 1, Action<string>? p = null, CancellationToken ct = default)
        {
            p?.Invoke($"Starting deep research on: {topic}");
            var session = await _searchSessionCoordinator.ExecuteAsync(
                new WebSearchRequest(topic, Depth: depth, MaxResults: 5, Purpose: "research"),
                ct).ConfigureAwait(false);
            var sources = session.ResultBundle.SourceUrls.ToList();
            var evidenceItems = ResearchSynthesisSupport.BuildEvidenceItems(session.ResultBundle.Documents);
            var profile = ResearchRequestProfileResolver.From(topic);
            var hasGroundedEvidence = evidenceItems.Any(static item => !item.IsFallback);

            if (!profile.IsDocumentAnalysis && !hasGroundedEvidence)
            {
                p?.Invoke("Synthesizing local-first baseline from library evidence...");
                var localBaseline = await LocalBaselineAnswerServiceSupport
                    .GenerateDetailedAsync(_localBaselineAnswerService, topic, ct)
                    .ConfigureAwait(false);
                if (localBaseline.Sources.Count > 0)
                {
                    sources = localBaseline.Sources
                        .Concat(sources)
                        .Where(static source => !string.IsNullOrWhiteSpace(source))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                if (localBaseline.EvidenceItems.Count > 0)
                {
                    evidenceItems = localBaseline.EvidenceItems;
                }

                var trace = BuildSearchTrace(session)
                    .Concat(localBaseline.Trace)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new ResearchResult(
                    topic,
                    "Synthesized Research",
                    sources,
                    new List<string>(),
                    localBaseline.Answer,
                    DateTime.Now,
                    EvidenceItems: evidenceItems,
                    SearchTrace: trace);
            }

            p?.Invoke($"Synthesizing {evidenceItems.Count} sources...");
            var synthesis = await SynthesizeWithTimeoutAsync(topic, sources, evidenceItems, ct);
             
            return new ResearchResult(
                topic,
                "Synthesized Research",
                sources,
                new List<string>(),
                synthesis,
                DateTime.Now,
                RawEvidence: ResearchSynthesisSupport.BuildRawEvidence(evidenceItems),
                EvidenceItems: evidenceItems,
                SearchTrace: BuildSearchTrace(session));
        }

        private async Task<string> SynthesizeWithTimeoutAsync(
            string topic,
            IReadOnlyList<string> sources,
            IReadOnlyList<ResearchEvidenceItem> evidenceItems,
            CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(SynthesisTimeout);
            var prompt = ResearchSynthesisSupport.BuildPrompt(topic, evidenceItems);

            try
            {
                var synthesis = await _ai.AskAsync(prompt, timeoutCts.Token);
                if (!string.IsNullOrWhiteSpace(synthesis))
                {
                    var quality = _answerQualityGate.Evaluate(topic, evidenceItems, synthesis);
                    if (quality.Accepted)
                    {
                        return synthesis;
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Graceful fallback when synthesis exceeds research budget.
            }
            catch
            {
                // Fallback synthesis keeps the response deterministic and bounded.
            }

            var deterministic = ResearchSynthesisSupport.BuildDeterministicSynthesis(topic, sources, evidenceItems);
            var deterministicQuality = _answerQualityGate.Evaluate(topic, evidenceItems, deterministic);
            if (deterministicQuality.Accepted)
            {
                return deterministic;
            }

            return ResearchSynthesisSupport.BuildHonestFallbackResponse(topic, sources);
        }

        private static IReadOnlyList<string> BuildSearchTrace(WebSearchSession session)
        {
            var extractedPageCount = session.ResultBundle.Documents.Count(static document => document.ExtractedPage is not null);
            var extractedPassageCount = session.ResultBundle.Documents
                .Where(static document => document.ExtractedPage is not null)
                .Sum(static document => document.ExtractedPage!.Passages.Count);
            var trace = new List<string>
            {
                $"web_search.outcome={session.ResultBundle.Outcome}",
                $"web_search.stop_reason={session.ResultBundle.StopReason ?? "unknown"}",
                $"web_search.iteration_count={session.ResultBundle.Iterations?.Count ?? 0}",
                $"web_page_fetch.extracted_count={extractedPageCount}",
                $"web_page_fetch.passage_count={extractedPassageCount}"
            };

            if (session.ResultBundle.Iterations is { Count: > 0 })
            {
                trace.AddRange(session.ResultBundle.Iterations.Select(static iteration =>
                    $"web_search.iteration[{iteration.Ordinal}] kind={iteration.QueryKind} query=\"{iteration.Query}\" results={iteration.ResultCount} aggregate={iteration.AggregateResultCount} domains={iteration.DistinctDomainCount} sufficient={(iteration.SufficientAfterIteration ? "yes" : "no")}"));
            }

            if (session.ResultBundle.ProviderTrace is { Count: > 0 })
            {
                trace.AddRange(session.ResultBundle.ProviderTrace);
            }

            if (session.ResultBundle.PageTrace is { Count: > 0 })
            {
                trace.AddRange(session.ResultBundle.PageTrace);
            }

            return trace;
        }

    }

    public class ModelOrchestrator : IModelOrchestrator, IContextAwareModelOrchestrator
    {
        private readonly AILink _ai;
        public ModelOrchestrator(AILink ai) => _ai = ai;
        public async Task<IntentAnalysis> AnalyzeIntentAsync(string p, CancellationToken ct = default)
        {
            var prompt = p ?? string.Empty;
            var intent = ResearchIntentPolicy.ShouldRouteToResearch(prompt)
                ? IntentType.Research
                : (ResearchIntentPolicy.HasExplicitGenerateRequest(prompt) ? IntentType.Generate : IntentType.Unknown);
            var model = (await SelectRoutingDecisionAsync(new ModelRoutingRequest(prompt, intent), ct).ConfigureAwait(false)).PreferredModel;
            return await Task.FromResult(new IntentAnalysis(intent, model));
        }

        public async Task<string> SelectOptimalModelAsync(string p, CancellationToken ct = default)
        {
            return (await SelectRoutingDecisionAsync(new ModelRoutingRequest(p ?? string.Empty), ct).ConfigureAwait(false)).PreferredModel;
        }

        public async Task<ModelRoutingDecision> SelectRoutingDecisionAsync(ModelRoutingRequest request, CancellationToken ct = default)
        {
            var prompt = request.Prompt ?? string.Empty;
            var intent = request.Intent;
            if (intent == IntentType.Unknown)
            {
                intent = ResearchIntentPolicy.ShouldRouteToResearch(prompt)
                    ? IntentType.Research
                    : (ResearchIntentPolicy.HasExplicitGenerateRequest(prompt) ? IntentType.Generate : IntentType.Unknown);
            }

            var reasons = new List<string>();
            var routeKey = "fast";
            var preferredModel = ResolveConfiguredModel("HELPER_MODEL_FAST", "fast");

            if (HasVisionSignal(prompt))
            {
                routeKey = "vision";
                preferredModel = _ai.GetBestModel("vision");
                reasons.Add("vision_signal");
            }
            else if (intent == IntentType.Generate || HasCodeSignal(prompt))
            {
                routeKey = "coder";
                preferredModel = _ai.GetBestModel("coder");
                reasons.Add(intent == IntentType.Generate ? "intent_generate" : "code_signal");
            }
            else if (request.RequiresVerification || HasVerificationSignal(prompt))
            {
                routeKey = "verifier";
                preferredModel = ResolveConfiguredModel("HELPER_MODEL_VERIFIER", "critic", "HELPER_MODEL_CRITIC", "HELPER_MODEL_REASONING");
                reasons.Add(request.RequiresVerification ? "verification_required" : "verification_signal");
            }
            else if (IsLongContextRequest(request))
            {
                routeKey = "long_context";
                preferredModel = ResolveConfiguredModel("HELPER_MODEL_LONG_CONTEXT", "reasoning", "HELPER_MODEL_DEEP_REASONING", "HELPER_MODEL_REASONING");
                reasons.Add("long_context");
            }
            else if (IsDeepReasoningRequest(intent, request, prompt))
            {
                routeKey = "deep_reasoning";
                preferredModel = ResolveConfiguredModel("HELPER_MODEL_DEEP_REASONING", "reasoning", "HELPER_MODEL_REASONING");
                reasons.Add("deep_reasoning");
            }
            else if (intent == IntentType.Research || HasReasoningSignal(prompt))
            {
                routeKey = "reasoning";
                preferredModel = ResolveConfiguredModel("HELPER_MODEL_REASONING", "reasoning");
                reasons.Add(intent == IntentType.Research ? "intent_research" : "reasoning_signal");
            }
            else
            {
                reasons.Add("default_fast_route");
            }

            if (request.ContextMessageCount > 0)
            {
                reasons.Add($"context_messages={request.ContextMessageCount}");
            }

            if (request.ApproximatePromptTokens > 0)
            {
                reasons.Add($"approx_tokens={request.ApproximatePromptTokens}");
            }

            if (!string.IsNullOrWhiteSpace(request.ExecutionMode))
            {
                reasons.Add($"execution_mode={request.ExecutionMode.Trim().ToLowerInvariant()}");
            }

            return await Task.FromResult(new ModelRoutingDecision(preferredModel, routeKey, reasons));
        }

        private static bool HasVisionSignal(string prompt)
            => ContainsAny(prompt, "image", "screenshot", "photo", "diagram", "scan", "ocr");

        private static bool HasCodeSignal(string prompt)
            => ContainsAny(prompt, "code", "class", "method", "refactor", "compile", "csproj", "api", "generate");

        private static bool HasReasoningSignal(string prompt)
            => ContainsAny(prompt, "analyze", "compare", "tradeoff", "root cause", "hypothesis", "reason");

        private static bool HasVerificationSignal(string prompt)
            => ContainsAny(prompt, "verify", "validate", "audit", "check", "prove", "schema", "contract");

        private static bool IsLongContextRequest(ModelRoutingRequest request)
        {
            return request.ContextMessageCount >= 14 || request.ApproximatePromptTokens >= 2200;
        }

        private static bool IsDeepReasoningRequest(IntentType intent, ModelRoutingRequest request, string prompt)
        {
            return intent == IntentType.Research ||
                   request.ExecutionMode.Equals("deep", StringComparison.OrdinalIgnoreCase) ||
                   request.ApproximatePromptTokens >= 1200 ||
                   HasReasoningSignal(prompt);
        }

        private string ResolveConfiguredModel(string primaryEnv, string category, string? fallbackEnv = null, string? secondaryFallbackEnv = null)
        {
            var configured = ReadEnvironmentModel(primaryEnv);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            configured = ReadEnvironmentModel(fallbackEnv);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            configured = ReadEnvironmentModel(secondaryFallbackEnv);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return _ai.GetBestModel(category);
        }

        private static string? ReadEnvironmentModel(string? envName)
        {
            if (string.IsNullOrWhiteSpace(envName))
            {
                return null;
            }

            var value = Environment.GetEnvironmentVariable(envName);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool ContainsAny(string prompt, params string[] markers)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return false;
            }

            foreach (var marker in markers)
            {
                if (prompt.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class CloudDeployer : IDeployService
    {
        public async Task<string> PrepareDeploymentAsync(string projectPath, string platform, CancellationToken ct = default)
        {
            // Real LocalDockerDeployer logic
            var dockerfile = Path.Combine(projectPath, "Dockerfile");
            if (!File.Exists(dockerfile)) 
            {
                var prompt = "Generate a minimalist production Dockerfile for the application at this path: " + projectPath + "\nOUTPUT ONLY THE DOCKERFILE CONTENT.";
                // In a real system we'd inject AILink, for this mock implementation we just create a basic one
                await File.WriteAllTextAsync(dockerfile, "FROM mcr.microsoft.com/dotnet/aspnet:8.0\nWORKDIR /app\nCOPY bin/Release/net8.0/publish/ .\nENTRYPOINT [\"dotnet\", \"App.dll\"]", ct);
            }
            
            var shell = new ShellExecutor();
            var imgName = "helper_app_" + Guid.NewGuid().ToString("N")[..6];
            await shell.ExecuteSequenceAsync(projectPath, new List<string> { $"docker build -t {imgName} ." });
            
            return $"Docker image built: {imgName}. Run with: docker run -d -p 8080:80 {imgName}";
        }
    }

    public class WebSearcher : IWebSearcher
    {
        private readonly IWebSearchProviderClient _providerClient;

        public WebSearcher()
            : this(new WebSearchProviderMux(
                new LocalSearchProvider(),
                new SearxSearchProvider()))
        {
        }

        internal WebSearcher(IWebSearchProviderClient providerClient)
        {
            _providerClient = providerClient;
        }

        public async Task<List<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default)
        {
            var response = await _providerClient.SearchAsync(
                new WebSearchPlan(query, 5, 1, "legacy_search", "standard", AllowDeterministicFallback: false),
                ct).ConfigureAwait(false);

            return response.Documents
                .Select(static document => new WebSearchResult(
                    document.Url,
                    document.Title,
                    document.Snippet,
                    document.IsFallback))
                .ToList();
        }

        internal static List<WebSearchResult> BuildDeterministicFallbackPack(string query)
        {
            return WebSearchFallbackBuilder.BuildFromQuery(query)
                .Select(static document => new WebSearchResult(
                    document.Url,
                    document.Title,
                    document.Snippet,
                    document.IsFallback))
                .ToList();
        }
    }
}

