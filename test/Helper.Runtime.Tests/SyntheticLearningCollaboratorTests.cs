using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class SyntheticLearningCollaboratorTests
{
    [Fact]
    public void LearningPathPolicy_CanonicalizesRelativeTargetFile_AgainstLibraryRoot()
    {
        using var harness = new SyntheticLearningTestHarness(activeGenerationEnabled: false);

        var canonical = harness.Policy.CanonicalizeTargetFile(Path.Combine("docs", "ai", "guide.pdf"));

        Assert.Equal(Path.Combine(harness.LibraryRoot, "docs", "ai", "guide.pdf"), canonical);
    }

    [Fact]
    public async Task IndexingQueueStore_SyncWithLibrary_AddsSupportedFiles_AndRemovesStaleDocs()
    {
        using var harness = new SyntheticLearningTestHarness(activeGenerationEnabled: false);
        var indexedFile = Path.Combine(harness.LibraryDocsRoot, "ai", "guide.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(indexedFile)!);
        await File.WriteAllTextAsync(indexedFile, "content");
        await File.WriteAllTextAsync(Path.Combine(harness.LibraryDocsRoot, "ignore.txt"), "skip");

        var staleFile = Path.Combine(harness.LibraryDocsRoot, "obsolete.pdf");
        await harness.Store.SaveAsync(new Dictionary<string, string>
        {
            [staleFile] = LearningQueueStatus.Pending
        });

        await harness.Store.SyncWithLibraryAsync();
        var queue = await harness.Store.LoadAsync();

        Assert.Single(queue);
        Assert.True(queue.TryGetValue(indexedFile, out var status));
        Assert.Equal(LearningQueueStatus.Pending, status);
        Assert.DoesNotContain(queue.Keys, path => string.Equals(path, staleFile, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IndexingQueueStore_UpdateStatusAsync_CanonicalizesRelativeLibraryPath()
    {
        using var harness = new SyntheticLearningTestHarness(activeGenerationEnabled: false);

        await harness.Store.UpdateStatusAsync(Path.Combine("docs", "ai", "guide.pdf"), LearningQueueStatus.Done);
        var queue = await harness.Store.LoadAsync();

        Assert.True(queue.TryGetValue(Path.Combine(harness.LibraryRoot, "docs", "ai", "guide.pdf"), out var status));
        Assert.Equal(LearningQueueStatus.Done, status);
    }

    [Fact]
    public async Task LearningLifecycleController_Stop_ResetsTargets_AndCancelsRunningCycle()
    {
        var controller = new LearningLifecycleController();
        controller.SetTargetDomain("ai");
        controller.SetTargetFile("C:\\temp\\guide.pdf", singleFileOnly: true);
        controller.SetIndexingStatus(LearningStatus.Running);
        controller.SetEvolutionStatus(LearningStatus.Running);
        controller.SetCurrentFileProgress(42);

        Assert.True(controller.TryStartCycle(ct => Task.Delay(Timeout.Infinite, ct)));
        Assert.False(controller.TryStartCycle(ct => Task.Delay(Timeout.Infinite, ct)));

        var stop = controller.Stop();
        var snapshot = controller.Snapshot();

        Assert.Equal(LearningStatus.Idle, snapshot.IndexingStatus);
        Assert.Equal(LearningStatus.Idle, snapshot.EvolutionStatus);
        Assert.Null(snapshot.TargetDomain);
        Assert.Null(snapshot.TargetFile);
        Assert.False(snapshot.SingleFileOnly);
        Assert.Equal(0, snapshot.CurrentFileProgress);
        Assert.NotNull(stop.RunningTask);
        Assert.True(stop.CancellationSource?.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stop.RunningTask!);
        stop.CancellationSource?.Dispose();
    }

    [Fact]
    public async Task SyntheticTaskRunner_SkipsGraphExecution_WhenGenerationDisabled()
    {
        using var harness = new SyntheticLearningTestHarness(activeGenerationEnabled: false);
        var ai = new StubAiLink("stub-model", "ignored");
        var graph = new RecordingGraphOrchestrator();
        var runner = new SyntheticTaskRunner(ai, graph, harness.Policy);
        var thoughts = new List<string>();

        await runner.RunAsync(CancellationToken.None, message =>
        {
            thoughts.Add(message);
            return Task.CompletedTask;
        });

        Assert.Empty(graph.Prompts);
        Assert.Contains(thoughts, message => message.Contains("disabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SyntheticTaskRunner_GeneratesChallenge_AndExecutesGraph_WhenEnabled()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HELPER_MODEL_REASONING"] = null
        });

        using var harness = new SyntheticLearningTestHarness(activeGenerationEnabled: true);
        var ai = new StubAiLink("stub-model", "Design a robust .NET indexing challenge.");
        var graph = new RecordingGraphOrchestrator();
        var runner = new SyntheticTaskRunner(ai, graph, harness.Policy);

        await runner.RunAsync(CancellationToken.None);

        Assert.Single(graph.Prompts);
        Assert.Equal("Design a robust .NET indexing challenge.", graph.Prompts[0]);
        Assert.Equal(harness.Policy.ActiveLearningOutputPath, graph.OutputPaths[0]);
        Assert.Equal("stub-model", ai.LastOverrideModel);
        Assert.True(Directory.Exists(harness.Policy.ActiveLearningOutputPath));
    }

    private sealed class SyntheticLearningTestHarness : IDisposable
    {
        public SyntheticLearningTestHarness(bool activeGenerationEnabled)
        {
            Root = Path.Combine(Path.GetTempPath(), "helper-synthetic-tests", Guid.NewGuid().ToString("N"));
            LibraryRoot = Path.Combine(Root, "library");
            LibraryDocsRoot = Path.Combine(LibraryRoot, "docs");
            QueuePath = Path.Combine(Root, "data", "indexing_queue.json");
            ActiveLearningOutputPath = Path.Combine(Root, "runtime", "active_learning");
            Directory.CreateDirectory(LibraryDocsRoot);

            Policy = new LearningPathPolicy(
                Root,
                QueuePath,
                LibraryRoot,
                LibraryDocsRoot,
                ActiveLearningOutputPath,
                activeGenerationEnabled,
                indexedDocumentExtensions: new[] { ".pdf", ".md" });
            Store = new IndexingQueueStore(Policy);
        }

        public string Root { get; }
        public string LibraryRoot { get; }
        public string LibraryDocsRoot { get; }
        public string QueuePath { get; }
        public string ActiveLearningOutputPath { get; }
        public LearningPathPolicy Policy { get; }
        public IndexingQueueStore Store { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class StubAiLink(string defaultModel, string response) : AILink("http://localhost:11434", defaultModel)
    {
        public string? LastOverrideModel { get; private set; }

        public override Task<string> AskAsync(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null)
        {
            LastOverrideModel = overrideModel;
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingGraphOrchestrator : IGraphOrchestrator
    {
        public List<string> Prompts { get; } = new();
        public List<string> OutputPaths { get; } = new();

        public Task<GenerationResult> ExecuteGraphAsync(string prompt, string outputPath, Action<string>? onProgress = null, CancellationToken ct = default)
        {
            Prompts.Add(prompt);
            OutputPaths.Add(outputPath);
            return Task.FromResult(new GenerationResult(
                true,
                new List<GeneratedFile>(),
                outputPath,
                new List<BuildError>(),
                TimeSpan.Zero));
        }
    }
}

