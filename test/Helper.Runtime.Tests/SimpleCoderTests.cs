using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class SimpleCoderTests
{
    [Fact]
    public async Task GenerateFileAsync_DoesNotInjectWpfSpecificUsings_ForPlainCSharpFiles()
    {
        var ai = new CapturingAiLink("namespace Billing { public class InvoiceService { } }");
        var coder = new SimpleCoder(ai, new CodeSanitizer());

        await coder.GenerateFileAsync(
            new FileTask("Services/InvoiceService.cs", "Service", new List<string>(), "Provide invoice operations."),
            new ProjectPlan("Billing Platform", new List<FileTask>()),
            ct: CancellationToken.None);

        Assert.NotNull(ai.LastPrompt);
        Assert.DoesNotContain("using System.Windows;", ai.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("using CommunityToolkit", ai.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ALWAYS INCLUDE these using directives", ai.LastPrompt, StringComparison.Ordinal);
        Assert.Contains("Include ONLY the using/import directives actually required", ai.LastPrompt, StringComparison.Ordinal);
    }

    private sealed class CapturingAiLink : AILink
    {
        private readonly string _response;

        public CapturingAiLink(string response)
            : base("http://localhost:11434", "qwen2.5-coder:14b")
        {
            _response = response;
        }

        public string? LastPrompt { get; private set; }

        public override Task<string> AskAsync(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null)
        {
            LastPrompt = prompt;
            return Task.FromResult(_response);
        }
    }
}
