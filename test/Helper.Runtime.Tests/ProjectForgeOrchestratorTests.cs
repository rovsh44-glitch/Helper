using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class ProjectForgeOrchestratorTests
{
    [Fact]
    public async Task ForgeProjectAsync_WhenTemplateBlockedByCertificationStatus_ReturnsBlockedErrorWithoutProcurement()
    {
        using var temp = new TempDirectoryScope();
        var templatesRoot = Path.Combine(temp.Path, "templates");
        var templateRoot = Path.Combine(templatesRoot, "Template_PdfEpubConverter");
        Directory.CreateDirectory(templateRoot);

        await File.WriteAllTextAsync(
            Path.Combine(templateRoot, "template.json"),
            """
{
  "Id": "Template_PdfEpubConverter",
  "Name": "PDF EPUB Converter",
  "Description": "Converter template",
  "Language": "csharp"
}
""");
        await TemplateCertificationStatusStore.WriteAsync(
            templateRoot,
            new TemplateCertificationStatus(
                DateTimeOffset.UtcNow,
                Passed: false,
                HasCriticalAlerts: true,
                CriticalAlerts: new[] { "Smoke[compile]: Compile gate failed." },
                ReportPath: "doc/report.md"));

        var templateManager = new ProjectTemplateManager(templatesRoot);
        var templateFactory = new Mock<ITemplateFactory>(MockBehavior.Strict);
        var orchestrator = new ProjectForgeOrchestrator(
            templateManager,
            templateFactory.Object,
            Mock.Of<IProjectPlanner>(),
            Mock.Of<ICodeGenerator>(),
            Mock.Of<IBuildValidator>(),
            Mock.Of<IForgeArtifactValidator>(),
            Mock.Of<IAutoHealer>(),
            new IntegrityAuditor(new AILink()),
            new AILink());

        var result = await orchestrator.ForgeProjectAsync(
            "Generate a PDF to EPUB and EPUB to PDF converter in C#.",
            "Template_PdfEpubConverter");

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal("TEMPLATE_BLOCKED_BY_CERTIFICATION_STATUS", error.Code);
        Assert.Contains("critical alerts", error.Message, StringComparison.OrdinalIgnoreCase);
        templateFactory.Verify(
            x => x.ProcureTemplateAsync(It.IsAny<string>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helper_forge_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}

