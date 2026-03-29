using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Moq;

namespace Helper.Runtime.Tests;

public class ReflectionServiceTests
{
    [Fact]
    public async Task IngestLessonAsync_PersistsStructuredMetadata()
    {
        KnowledgeChunk? captured = null;
        var store = new Mock<IVectorStore>(MockBehavior.Strict);
        store.Setup(x => x.UpsertAsync(It.IsAny<KnowledgeChunk>(), It.IsAny<CancellationToken>()))
            .Callback<KnowledgeChunk, CancellationToken>((chunk, _) => captured = chunk)
            .Returns(Task.CompletedTask);

        var service = new ReflectionService(
            new AILink(),
            store.Object,
            embedAsync: (_, _) => Task.FromResult(new[] { 0.1f, 0.2f }));

        await service.IngestLessonAsync(
            new EngineeringLesson(
                "Build retry loop missing backoff",
                "dotnet background worker",
                "Add bounded exponential backoff with cancellation support",
                "Retry loops must be bounded and observable",
                new DateTime(2026, 03, 19, 0, 0, 0, DateTimeKind.Utc)));

        Assert.NotNull(captured);
        Assert.Equal("engineering_lesson", captured!.Metadata["type"]);
        Assert.Equal("Build retry loop missing backoff", captured.Metadata["error_pattern"]);
        Assert.Equal("dotnet background worker", captured.Metadata["context"]);
        Assert.Contains("bounded exponential backoff", captured.Metadata["solution"]);
        Assert.Contains("observable", captured.Metadata["principle"]);
    }

    [Fact]
    public async Task SearchLessonsAsync_RestoresStructuredAndLegacyLessons()
    {
        var structuredChunk = new KnowledgeChunk(
            "structured",
            "[LESSON] Background retry loop",
            new[] { 0.1f },
            new Dictionary<string, string>
            {
                ["created_at"] = "2026-03-19T00:00:00.0000000Z",
                ["error_pattern"] = "Background retry loop",
                ["context"] = "worker",
                ["solution"] = "Restart the worker only after queue drain.",
                ["principle"] = "Preserve in-flight work before restart."
            },
            "engineering_lessons");

        var legacyChunk = new KnowledgeChunk(
            "legacy",
            "[LESSON] Legacy parser failure\nContext: parser\nSolution: Normalize section headers first.\nPrinciple: Prefer deterministic pre-normalization.",
            new[] { 0.2f },
            new Dictionary<string, string>
            {
                ["created_at"] = "2026-03-18T00:00:00.0000000Z",
                ["context"] = "parser"
            },
            "engineering_lessons");

        var store = new Mock<IVectorStore>(MockBehavior.Strict);
        store.Setup(x => x.SearchAsync(It.IsAny<float[]>(), "engineering_lessons", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { structuredChunk, legacyChunk });

        var service = new ReflectionService(
            new AILink(),
            store.Object,
            embedAsync: (_, _) => Task.FromResult(new[] { 0.1f, 0.2f }));

        var lessons = await service.SearchLessonsAsync("retry worker", 2);

        Assert.Equal(2, lessons.Count);
        Assert.Equal("Background retry loop", lessons[0].ErrorPattern);
        Assert.Equal("Restart the worker only after queue drain.", lessons[0].Solution);
        Assert.Equal("Legacy parser failure", lessons[1].ErrorPattern);
        Assert.Equal("Normalize section headers first.", lessons[1].Solution);
        Assert.Equal("Prefer deterministic pre-normalization.", lessons[1].Principle);
    }
}

