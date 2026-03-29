using Helper.Api.Conversation;

namespace Helper.Runtime.Tests;

public sealed class WebResearchParityEvalTests
{
    [Fact]
    [Trait("Category", "EvalV2")]
    public async Task WebResearchParityEvalService_PreparesPackage_WithRequiredCoverage()
    {
        using var packageRoot = EvalTestPackageFactory.CreateWebResearchParityPackage();
        var service = new WebResearchParityEvalService();

        var package = await service.PrepareAsync(
            packageRoot.RootPath,
            new WebResearchParityEvalOptions(MinPreparedRuns: 240, Seed: 19),
            CancellationToken.None);

        Assert.True(package.SeedScenarios.Count >= 120);
        Assert.Equal("pass", package.Summary.GateStatus);
        Assert.Empty(package.Summary.Alerts);
        Assert.True(package.Summary.ProviderFixtureCount >= 3);
        Assert.True(package.Summary.PageFixtureCount >= 3);
        Assert.Contains("ru", package.Summary.LanguageDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("en", package.Summary.LanguageDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("latest_release", package.Summary.KindDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("finance_quote", package.Summary.KindDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("blocked_fetch", package.Summary.KindDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("web_required", package.Summary.LabelDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("freshness", package.Summary.LabelDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("blocked_fetch", package.Summary.LabelDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("helper_web_research_avg_queries_per_turn", package.Rubric.RequiredMetrics, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "EvalV2")]
    public async Task WebResearchParityEvalService_ExportsJsonAndMarkdownReports()
    {
        using var packageRoot = EvalTestPackageFactory.CreateWebResearchParityPackage();
        var service = new WebResearchParityEvalService();
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"helper-web-research-eval-{Guid.NewGuid():N}");

        try
        {
            var result = await service.ExportAsync(
                packageRoot.RootPath,
                outputDirectory,
                new WebResearchParityEvalOptions(MinPreparedRuns: 240, Seed: 31),
                CancellationToken.None);

            Assert.True(File.Exists(result.JsonPath));
            Assert.True(File.Exists(result.MarkdownPath));

            var json = await File.ReadAllTextAsync(result.JsonPath);
            var markdown = await File.ReadAllTextAsync(result.MarkdownPath);

            Assert.Contains("\"GateStatus\": \"pass\"", json, StringComparison.Ordinal);
            Assert.Contains("\"rubricVersion\": \"2026-03-21\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Gate status: `pass`", markdown, StringComparison.Ordinal);
            Assert.Contains("`helper_web_research_avg_queries_per_turn`", markdown, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }
}

