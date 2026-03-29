using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Moq;

namespace Helper.Runtime.Tests;

public class CriticServiceTests
{
    [Fact]
    public async Task CritiqueAsync_FailsOpen_WhenResponseIsNotJson()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        ai.Setup(x => x.AskAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>()))
            .ReturnsAsync("non-json response");

        var personas = BuildPersonaMock();
        var critic = new LlmCritic(ai.Object, personas.Object);

        var result = await critic.CritiqueAsync("src", "draft", "ctx", CancellationToken.None);

        Assert.True(result.IsApproved);
        Assert.Contains("parsing failed", result.Feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CritiqueAsync_ParsesFencedJson()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        ai.Setup(x => x.AskAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>()))
            .ReturnsAsync("""
```json
{"IsApproved":false,"Feedback":"Mismatch","CorrectedContent":null}
```
""");

        var personas = BuildPersonaMock();
        var critic = new LlmCritic(ai.Object, personas.Object);

        var result = await critic.CritiqueAsync("src", "draft", "ctx", CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal("Mismatch", result.Feedback);
    }

    [Fact]
    public async Task AnalyzeAsync_ParsesInlineJsonObject()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        ai.Setup(x => x.AskAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>()))
            .ReturnsAsync("Result: {\"IsApproved\":true,\"Feedback\":\"ok\",\"CorrectedContent\":null}");

        var personas = BuildPersonaMock();
        var critic = new LlmCritic(ai.Object, personas.Object);

        var result = await critic.AnalyzeAsync("content", CancellationToken.None);

        Assert.True(result.IsApproved);
        Assert.Equal("ok", result.Feedback);
    }

    private static Mock<IPersonaOrchestrator> BuildPersonaMock()
    {
        var personas = new Mock<IPersonaOrchestrator>();
        personas.Setup(x => x.ConductRoundtableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShadowRoundtableReport(
                "proposal",
                new List<PersonaOpinion>(),
                "advice",
                0));
        return personas;
    }
}

