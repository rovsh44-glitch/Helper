using System.Text.Json;
using Helper.Api.Conversation;

namespace Helper.Runtime.Tests;

internal static class EvalTestPackageFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static TemporaryEvalAsset CreateEndToEndDataset()
    {
        var asset = CreateAssetRoot("eval-runner");
        var datasetPath = Path.Combine(asset.RootPath, "human_level_parity_ru_en.jsonl");
        WriteJsonLines(datasetPath, BuildEndToEndSeedScenarios());
        return asset with { PrimaryPath = datasetPath };
    }

    public static TemporaryEvalAsset CreateHumanLikeCommunicationPackage()
    {
        var asset = CreateAssetRoot("human-like-eval");
        var root = asset.RootPath;

        WriteJsonLines(
            Path.Combine(root, "corpus.jsonl"),
            ExpandSeedCatalog(
                BuildHumanLikeSeedCatalog(),
                120));

        WriteJson(
            Path.Combine(root, "rubric.json"),
            new HumanLikeCommunicationEvalRubric(
                Version: "2026-03-20",
                MinimumSeedScenarios: 120,
                MinimumPreparedRuns: 240,
                RequiredKinds: new[]
                {
                    "simple_help",
                    "ambiguous",
                    "memory_ack",
                    "repair",
                    "factual_cited",
                    "research_synthesis"
                },
                RequiredLabels: new[]
                {
                    "ru_only",
                    "en_only"
                },
                Dimensions: new[]
                {
                    new HumanLikeCommunicationRubricDimension("naturalness", "Naturalness", "Response sounds natural.", "Prefer fluent conversational output."),
                    new HumanLikeCommunicationRubricDimension("empathy_appropriateness", "Empathy", "Response calibrates empathy to the request.", "Avoid robotic affect."),
                    new HumanLikeCommunicationRubricDimension("anti_template_quality", "Anti-template", "Response avoids canned boilerplate.", "Prefer direct tailored wording."),
                    new HumanLikeCommunicationRubricDimension("language_consistency", "Language consistency", "Response stays in the user's language.", "Do not drift between languages."),
                    new HumanLikeCommunicationRubricDimension("clarification_helpfulness", "Clarification helpfulness", "Clarification requests move the task forward.", "Ask only useful questions.")
                }));

        return asset;
    }

    public static TemporaryEvalAsset CreateWebResearchParityPackage()
    {
        var asset = CreateAssetRoot("web-research-eval");
        var root = asset.RootPath;
        var providerFixturesRoot = Path.Combine(root, "fixtures", "providers");
        var pageFixturesRoot = Path.Combine(root, "fixtures", "pages");

        Directory.CreateDirectory(providerFixturesRoot);
        Directory.CreateDirectory(pageFixturesRoot);

        WriteJsonLines(
            Path.Combine(root, "corpus.jsonl"),
            ExpandSeedCatalog(
                BuildWebResearchSeedCatalog(),
                120));

        WriteJson(
            Path.Combine(root, "rubric.json"),
            new WebResearchParityEvalRubric(
                Version: "2026-03-21",
                MinimumSeedScenarios: 120,
                MinimumPreparedRuns: 240,
                MinimumProviderFixtures: 3,
                MinimumPageFixtures: 3,
                RequiredKinds: new[]
                {
                    "latest_release",
                    "finance_quote",
                    "blocked_fetch"
                },
                RequiredLabels: new[]
                {
                    "web_required",
                    "freshness",
                    "blocked_fetch"
                },
                RequiredMetrics: new[]
                {
                    "helper_web_research_avg_queries_per_turn",
                    "helper_web_research_avg_fetched_pages_per_turn",
                    "helper_web_research_blocked_fetch_total"
                },
                Dimensions: new[]
                {
                    new WebResearchParityRubricDimension("freshness", "Freshness", "The package covers freshness-sensitive tasks."),
                    new WebResearchParityRubricDimension("coverage", "Coverage", "The package covers web-only retrieval paths.")
                }));

        File.WriteAllText(Path.Combine(providerFixturesRoot, "latest_release.json"), "{ \"fixture\": \"latest_release\" }");
        File.WriteAllText(Path.Combine(providerFixturesRoot, "finance_quote.json"), "{ \"fixture\": \"finance_quote\" }");
        File.WriteAllText(Path.Combine(providerFixturesRoot, "blocked_fetch.json"), "{ \"fixture\": \"blocked_fetch\" }");

        File.WriteAllText(Path.Combine(pageFixturesRoot, "latest_release.html"), "<html><body>latest release fixture</body></html>");
        File.WriteAllText(Path.Combine(pageFixturesRoot, "finance_quote.html"), "<html><body>finance quote fixture</body></html>");
        File.WriteAllText(Path.Combine(pageFixturesRoot, "blocked_fetch.html"), "<html><body>blocked fetch fixture</body></html>");

        return asset;
    }

    private static TemporaryEvalAsset CreateAssetRoot(string prefix)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"helper-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        return new TemporaryEvalAsset(rootPath, rootPath);
    }

    private static IReadOnlyList<EvalScenarioDefinition> BuildEndToEndSeedScenarios()
    {
        return new[]
        {
            new EvalScenarioDefinition("seed-001", "ru", "simple_help", "Pomogi sostavit plan dnia.", true, Labels: new[] { "ru_only", "planning" }),
            new EvalScenarioDefinition("seed-002", "en", "simple_help", "Help me draft a work summary.", true, Labels: new[] { "en_only", "planning" }),
            new EvalScenarioDefinition("seed-003", "ru", "ambiguous", "Ne znaiu, s chego nachat proekt.", true, Labels: new[] { "ru_only", "clarify" }),
            new EvalScenarioDefinition("seed-004", "en", "repair", "Fix the broken build checklist.", true, Labels: new[] { "en_only", "repair" }),
            new EvalScenarioDefinition("seed-005", "ru", "factual_cited", "Kratko sravni dva standarta.", true, Labels: new[] { "ru_only", "facts" }),
            new EvalScenarioDefinition("seed-006", "en", "research_synthesis", "Summarize the latest findings.", true, Labels: new[] { "en_only", "research" }),
            new EvalScenarioDefinition("seed-007", "ru", "memory_ack", "Zapomni moe predpochtenie yazyka.", false, Labels: new[] { "ru_only", "memory" }),
            new EvalScenarioDefinition("seed-008", "en", "simple_help", "Rewrite this sentence clearly.", false, Labels: new[] { "en_only", "editing" }),
            new EvalScenarioDefinition("seed-009", "ru", "repair", "Isprav strukturu dokumenta.", false, Labels: new[] { "ru_only", "repair" }),
            new EvalScenarioDefinition("seed-010", "en", "ambiguous", "I'm not sure what I should prioritize.", false, Labels: new[] { "en_only", "clarify" })
        };
    }

    private static IReadOnlyList<EvalScenarioDefinition> BuildHumanLikeSeedCatalog()
    {
        return new[]
        {
            new EvalScenarioDefinition("human-001", "ru", "simple_help", "Pomogi vezhlivo otvetit klientu.", true, Labels: new[] { "ru_only", "conversation" }),
            new EvalScenarioDefinition("human-002", "en", "simple_help", "Help me reply to a customer politely.", true, Labels: new[] { "en_only", "conversation" }),
            new EvalScenarioDefinition("human-003", "ru", "ambiguous", "Ia zaputalsia v zadache i ne znaiu sleduiushchii shag.", true, Labels: new[] { "ru_only", "clarify" }),
            new EvalScenarioDefinition("human-004", "en", "ambiguous", "I am stuck and need the next step.", true, Labels: new[] { "en_only", "clarify" }),
            new EvalScenarioDefinition("human-005", "ru", "memory_ack", "Zapomni chto ia predpochitaiu korotkie otvety.", true, Labels: new[] { "ru_only", "memory" }),
            new EvalScenarioDefinition("human-006", "en", "memory_ack", "Remember that I prefer short replies.", true, Labels: new[] { "en_only", "memory" }),
            new EvalScenarioDefinition("human-007", "ru", "repair", "Isprav grubuiu formulirovku pisma.", true, Labels: new[] { "ru_only", "repair" }),
            new EvalScenarioDefinition("human-008", "en", "repair", "Repair the tone of this message.", true, Labels: new[] { "en_only", "repair" }),
            new EvalScenarioDefinition("human-009", "ru", "factual_cited", "Kratko obiasni fakt i ukazhi istochnik.", true, Labels: new[] { "ru_only", "facts" }),
            new EvalScenarioDefinition("human-010", "en", "factual_cited", "Explain the fact and cite a source.", true, Labels: new[] { "en_only", "facts" }),
            new EvalScenarioDefinition("human-011", "ru", "research_synthesis", "Soberi kratkii sintez po teme.", true, Labels: new[] { "ru_only", "research" }),
            new EvalScenarioDefinition("human-012", "en", "research_synthesis", "Provide a concise research synthesis.", true, Labels: new[] { "en_only", "research" })
        };
    }

    private static IReadOnlyList<EvalScenarioDefinition> BuildWebResearchSeedCatalog()
    {
        return new[]
        {
            new EvalScenarioDefinition("web-001", "ru", "latest_release", "Naidi posledniuiu versiiu platformy.", true, Labels: new[] { "web_required", "freshness", "ru_only" }),
            new EvalScenarioDefinition("web-002", "en", "latest_release", "Find the latest platform release.", true, Labels: new[] { "web_required", "freshness", "en_only" }),
            new EvalScenarioDefinition("web-003", "ru", "finance_quote", "Prover aktualnuiu kotirovku aktiva.", true, Labels: new[] { "web_required", "freshness", "ru_only" }),
            new EvalScenarioDefinition("web-004", "en", "finance_quote", "Check the current asset quote.", true, Labels: new[] { "web_required", "freshness", "en_only" }),
            new EvalScenarioDefinition("web-005", "ru", "blocked_fetch", "Soobshchi chto stranitsa nedostupna i predlozhi obkhod.", true, Labels: new[] { "web_required", "blocked_fetch", "ru_only" }),
            new EvalScenarioDefinition("web-006", "en", "blocked_fetch", "Report that the page is blocked and suggest a fallback.", true, Labels: new[] { "web_required", "blocked_fetch", "en_only" })
        };
    }

    private static IReadOnlyList<EvalScenarioDefinition> ExpandSeedCatalog(
        IReadOnlyList<EvalScenarioDefinition> seedCatalog,
        int targetCount)
    {
        var results = new List<EvalScenarioDefinition>(targetCount);
        for (var index = 0; index < targetCount; index++)
        {
            var template = seedCatalog[index % seedCatalog.Count];
            results.Add(template with
            {
                Id = $"{template.Id}-{index:D3}",
                Prompt = $"{template.Prompt} Variant {index:D3}."
            });
        }

        return results;
    }

    private static void WriteJson(string path, object payload)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static void WriteJsonLines(string path, IEnumerable<EvalScenarioDefinition> scenarios)
    {
        var lines = scenarios.Select(scenario => JsonSerializer.Serialize(scenario));
        File.WriteAllLines(path, lines);
    }

    internal sealed record TemporaryEvalAsset(string RootPath, string PrimaryPath) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
