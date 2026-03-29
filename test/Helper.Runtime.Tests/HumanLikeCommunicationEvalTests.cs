using Helper.Api.Conversation;

namespace Helper.Runtime.Tests;

public class HumanLikeCommunicationEvalTests
{
    [Fact]
    [Trait("Category", "EvalV2")]
    public async Task HumanLikeCommunicationEvalService_PreparesPackage_WithRequiredCoverage()
    {
        using var packageRoot = EvalTestPackageFactory.CreateHumanLikeCommunicationPackage();
        var service = new HumanLikeCommunicationEvalService();

        var package = await service.PrepareAsync(
            packageRoot.RootPath,
            new HumanLikeCommunicationEvalOptions(MinPreparedRuns: 240, Seed: 17),
            CancellationToken.None);

        Assert.True(package.SeedScenarios.Count >= 120);
        Assert.Equal("pass", package.Summary.GateStatus);
        Assert.Empty(package.Summary.Alerts);
        Assert.Contains("ru", package.Summary.LanguageDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("en", package.Summary.LanguageDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ru_only", package.Summary.LabelDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("en_only", package.Summary.LabelDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("simple_help", package.Summary.KindDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ambiguous", package.Summary.KindDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("memory_ack", package.Summary.KindDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("repair", package.Summary.KindDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("factual_cited", package.Summary.KindDistribution.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("research_synthesis", package.Summary.KindDistribution.Keys, StringComparer.OrdinalIgnoreCase);

        var dimensions = package.Rubric.Dimensions.Select(dimension => dimension.Key).ToArray();
        Assert.Contains("naturalness", dimensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("empathy_appropriateness", dimensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("anti_template_quality", dimensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("language_consistency", dimensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("clarification_helpfulness", dimensions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "EvalV2")]
    public async Task HumanLikeCommunicationEvalService_ExportsJsonAndMarkdownReports()
    {
        using var packageRoot = EvalTestPackageFactory.CreateHumanLikeCommunicationPackage();
        var service = new HumanLikeCommunicationEvalService();
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"helper-human-like-eval-{Guid.NewGuid():N}");

        try
        {
            var result = await service.ExportAsync(
                packageRoot.RootPath,
                outputDirectory,
                new HumanLikeCommunicationEvalOptions(MinPreparedRuns: 240, Seed: 23),
                CancellationToken.None);

            Assert.True(File.Exists(result.JsonPath));
            Assert.True(File.Exists(result.MarkdownPath));

            var json = await File.ReadAllTextAsync(result.JsonPath);
            var markdown = await File.ReadAllTextAsync(result.MarkdownPath);

            Assert.Contains("\"GateStatus\": \"pass\"", json, StringComparison.Ordinal);
            Assert.Contains("\"rubricVersion\": \"2026-03-20\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Gate status: `pass`", markdown, StringComparison.Ordinal);
            Assert.Contains("`naturalness`", markdown, StringComparison.Ordinal);
            Assert.Contains("`clarification_helpfulness`", markdown, StringComparison.Ordinal);
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

