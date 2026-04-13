using Helper.Api.Conversation;
using Helper.Api.Backend.Application;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.WebResearch;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Helper.Runtime.Tests;

public partial class ConversationRuntimeTests
{
    [Fact]
    public void InMemoryConversationStore_PreservesConversationState()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate(null);
        state.LongTermMemoryEnabled = true;

        store.AddMessage(state, new ChatMessageDto("user", "remember: answer in russian", DateTimeOffset.UtcNow));
        store.AddMessage(state, new ChatMessageDto("assistant", "ok", DateTimeOffset.UtcNow));
        store.AddMessage(state, new ChatMessageDto("user", "нужно закончить refactoring", DateTimeOffset.UtcNow));

        var same = store.GetOrCreate(state.Id);
        Assert.Equal(state.Id, same.Id);
        Assert.Contains("answer in russian", same.Preferences);
        Assert.NotEmpty(same.OpenTasks);
    }

    [Fact]
    public void InMemoryConversationStore_CapturesRememberDirective_WithBracketPrefix()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("prefixed-memory");
        state.LongTermMemoryEnabled = true;

        store.AddMessage(state, new ChatMessageDto("user", "[1] remember: answer concise", DateTimeOffset.UtcNow, "t-prefixed"));

        Assert.Contains("answer concise", state.Preferences);
        Assert.Contains(state.MemoryItems, item => item.Content.Equals("answer concise", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InMemoryConversationStore_PersistsConversationState_WhenPathConfigured()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"helper-conversations-{Guid.NewGuid():N}.json");
        try
        {
            using (var store = new InMemoryConversationStore(tempPath))
            {
                var state = store.GetOrCreate("persisted-conversation");
                state.Formality = "formal";
                state.DomainFamiliarity = "expert";
                state.PreferredStructure = "step_by_step";
                state.Warmth = "warm";
                state.Enthusiasm = "high";
                state.Directness = "direct";
                state.DefaultAnswerShape = "bullets";
                state.SearchLocalityHint = "Tashkent";
                state.PersonalMemoryConsentGranted = true;
                state.PersonalMemoryConsentAt = DateTimeOffset.UtcNow;
                state.SessionMemoryTtlMinutes = 300;
                state.TaskMemoryTtlHours = 48;
                state.LongTermMemoryTtlDays = 30;
                state.LongTermMemoryEnabled = true;
                state.SearchSessions["main"] = new SearchSessionState(
                    BranchId: "main",
                    RootQuery: "latest climate pact announcement",
                    LastUserQuery: "what about Reuters coverage?",
                    LastEffectiveQuery: "latest climate pact announcement. Follow-up: what about Reuters coverage?",
                    LastTurnId: "turn-2",
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    CategoryHint: "news",
                    SourceUrls: new[] { "https://reuters.com/world/climate-pact" },
                    CitationLineage: new[]
                    {
                        new CitationLineageEntry(
                            "lin_1",
                            "reuters.com/world/climate-pact|fetched_page|passage:p1",
                            "1:p1",
                            "https://reuters.com/world/climate-pact",
                            "Leaders sign climate pact - Reuters",
                            "fetched_page",
                            "2026-03-21",
                            "p1",
                            1,
                            "turn-1",
                            "turn-2",
                            2)
                    },
                    EvidenceMemory: new[]
                    {
                        new SelectiveEvidenceMemoryEntry(
                            "mem_1",
                            "reuters.com/world/climate-pact|verified_passage|passage:p1",
                            "https://reuters.com/world/climate-pact",
                            "Leaders sign climate pact - Reuters",
                            "verified_passage",
                            "Negotiators agreed on a climate pact after overnight talks.",
                            "p1",
                            1,
                            "untrusted_web_content",
                            "turn-1",
                            "turn-2",
                            2)
                    },
                    ContinuationDepth: 1,
                    LastReuseReason: "citation_reference",
                    LastInputMode: "voice");
                store.AddMessage(state, new ChatMessageDto("user", "remember: prefer concise answers", DateTimeOffset.UtcNow, "turn-1"));
                store.AddMessage(state, new ChatMessageDto("assistant", "ack", DateTimeOffset.UtcNow, "turn-1"));
            }

            var restoredStore = new InMemoryConversationStore(tempPath);
            var found = restoredStore.TryGet("persisted-conversation", out var restoredState);

            Assert.True(found);
            Assert.NotNull(restoredState);
            Assert.True(restoredState.Messages.Count >= 2);
            Assert.Equal("formal", restoredState.Formality);
            Assert.Equal("expert", restoredState.DomainFamiliarity);
            Assert.Equal("step_by_step", restoredState.PreferredStructure);
            Assert.Equal("warm", restoredState.Warmth);
            Assert.Equal("high", restoredState.Enthusiasm);
            Assert.Equal("direct", restoredState.Directness);
            Assert.Equal("bullets", restoredState.DefaultAnswerShape);
            Assert.Equal("Tashkent", restoredState.SearchLocalityHint);
            Assert.True(restoredState.PersonalMemoryConsentGranted);
            Assert.Equal(300, restoredState.SessionMemoryTtlMinutes);
            Assert.Equal(48, restoredState.TaskMemoryTtlHours);
            Assert.Equal(30, restoredState.LongTermMemoryTtlDays);
            Assert.NotEmpty(restoredState.MemoryItems);
            Assert.True(restoredState.SearchSessions.TryGetValue("main", out var restoredSearchSession));
            Assert.NotNull(restoredSearchSession);
            Assert.Equal("latest climate pact announcement", restoredSearchSession.RootQuery);
            Assert.Single(restoredSearchSession.SourceUrls);
            Assert.Single(restoredSearchSession.CitationLineage);
            Assert.Single(restoredSearchSession.EffectiveEvidenceMemory);
            Assert.Equal("voice", restoredSearchSession.LastInputMode);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void InMemoryConversationStore_LoadsLegacySnapshot_AndMigratesToCurrentSchema()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"helper-legacy-conversations-{Guid.NewGuid():N}.json");
        try
        {
            var legacy = new[]
            {
                new
                {
                    Id = "legacy-conversation",
                    Messages = new[] { new ChatMessageDto("user", "hello", DateTimeOffset.UtcNow, "turn-1", BranchId: "main") },
                    UpdatedAt = DateTimeOffset.UtcNow,
                    RollingSummary = (string?)null,
                    Preferences = new[] { "prefer concise" },
                    OpenTasks = Array.Empty<string>(),
                    LongTermMemoryEnabled = true,
                    PreferredLanguage = "ru",
                    DetailLevel = "balanced",
                    ActiveTurnId = (string?)null,
                    ActiveTurnUserMessage = (string?)null,
                    ActiveTurnStartedAt = (DateTimeOffset?)null,
                    ActiveBranchId = "main",
                    Branches = new[] { new BranchDescriptor("main", null, null, DateTimeOffset.UtcNow) }
                }
            };
            File.WriteAllText(tempPath, JsonSerializer.Serialize(legacy));

            var store = new InMemoryConversationStore(tempPath);
            var loaded = store.TryGet("legacy-conversation", out var state);

            Assert.True(loaded);
            Assert.NotNull(state);
            Assert.True(state.Messages.Count >= 1);
            Assert.Equal("neutral", state.Formality);
            Assert.Equal("intermediate", state.DomainFamiliarity);
            Assert.Equal("auto", state.PreferredStructure);
            Assert.Equal("balanced", state.Warmth);
            Assert.Equal("balanced", state.Enthusiasm);
            Assert.Equal("balanced", state.Directness);
            Assert.Equal("auto", state.DefaultAnswerShape);
            Assert.NotEmpty(state.MemoryItems);

            var migratedRaw = File.ReadAllText(tempPath);
            using var doc = JsonDocument.Parse(migratedRaw);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.Equal(6, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

}


