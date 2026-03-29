using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class ComplexityAnalyzerTests
{
    [Fact]
    public async Task AnalyzeComplexityAsync_LogsDegradedFallback_WhenClassifierFails()
    {
        var originalError = Console.Error;
        var errorOutput = new StringWriter();

        try
        {
            Console.SetError(errorOutput);

            var analyzer = new ComplexityAnalyzer(
                new ThrowingAiLink(),
                new HealthyMonitorStub());

            var result = await analyzer.AnalyzeComplexityAsync("Design a medium-complexity backend task.");

            Assert.Equal(TaskComplexity.Standard, result);
            Assert.Contains("Classifier failed", errorOutput.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private sealed class ThrowingAiLink : AILink
    {
        public ThrowingAiLink()
            : base(defaultModel: "test-model")
        {
        }

        public override Task<string> AskAsync(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null)
            => throw new InvalidOperationException("boom");
    }

    private sealed class HealthyMonitorStub : IHealthMonitor
    {
        public Task<HealthStatus> DiagnoseAsync(CancellationToken ct = default)
            => Task.FromResult(new HealthStatus(true, new List<string>(), 0.0, 8.0));
    }
}

