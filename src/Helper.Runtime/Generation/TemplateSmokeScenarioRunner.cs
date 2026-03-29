using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

internal static class TemplateSmokeScenarioRunner
{
    public static async Task<IReadOnlyList<TemplateCertificationSmokeScenario>> EvaluateAsync(
        string templatePath,
        TemplateMetadataModel? metadata,
        bool compilePassed,
        bool artifactPassed,
        CancellationToken ct)
    {
        var profile = PolyglotCompileGateValidator.DetectProfile(templatePath);
        var requestedScenarios = TemplateSmokeScenarioCatalog.ResolveScenarioIds(templatePath, metadata, profile);
        var scenarios = new List<TemplateCertificationSmokeScenario>(requestedScenarios.Count);
        foreach (var scenarioId in requestedScenarios)
        {
            ct.ThrowIfCancellationRequested();
            scenarios.Add(await TemplateSmokeScenarioCatalog.EvaluateScenarioAsync(
                templatePath,
                metadata,
                profile,
                scenarioId,
                compilePassed,
                artifactPassed,
                ct).ConfigureAwait(false));
        }

        return scenarios;
    }
}

