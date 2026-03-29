using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed partial class TemplateCertificationService : ITemplateCertificationService
{
    private readonly ITemplateManager _templateManager;
    private readonly ITemplateLifecycleService _lifecycle;
    private readonly IGenerationCompileGate _compileGate;
    private readonly IBuildValidator _buildValidator;
    private readonly IForgeArtifactValidator _artifactValidator;
    private readonly string _templatesRoot;
    private readonly string _workspaceRoot;

    public TemplateCertificationService(
        ITemplateManager templateManager,
        ITemplateLifecycleService lifecycle,
        IGenerationCompileGate compileGate,
        IBuildValidator buildValidator,
        IForgeArtifactValidator artifactValidator,
        string templatesRoot,
        string? workspaceRoot = null)
    {
        _templateManager = templateManager;
        _lifecycle = lifecycle;
        _compileGate = compileGate;
        _buildValidator = buildValidator;
        _artifactValidator = artifactValidator;
        _templatesRoot = templatesRoot;
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? HelperWorkspacePathResolver.ResolveHelperRoot()
            : Path.GetFullPath(workspaceRoot);
    }

    public async Task<TemplateCertificationReport> CertifyAsync(
        string templateId,
        string version,
        string? reportPath = null,
        string? templatePath = null,
        CancellationToken ct = default)
    {
        var resolvedTemplatePath = string.IsNullOrWhiteSpace(templatePath)
            ? Path.Combine(_templatesRoot, templateId, version)
            : Path.GetFullPath(templatePath);
        if (!Directory.Exists(resolvedTemplatePath))
        {
            throw new DirectoryNotFoundException($"Template version path not found: {resolvedTemplatePath}");
        }

        var errors = new List<string>();
        var metadata = await TemplateMetadataReader.TryLoadAsync(resolvedTemplatePath, ct);
        var metadataPassed = ValidateMetadata(metadata, templateId, version, errors);

        var compile = await _compileGate.ValidateAsync(resolvedTemplatePath, ct);
        TryDeleteDirectory(compile.CompileWorkspace);
        if (!compile.Success)
        {
            errors.AddRange(compile.Errors.Select(x => $"CompileGate[{x.Code}]: {x.Message}"));
        }

        var buildErrors = await _buildValidator.ValidateAsync(resolvedTemplatePath, ct);
        var artifact = await _artifactValidator.ValidateAsync(resolvedTemplatePath, buildErrors, ct);
        if (!artifact.Success)
        {
            errors.Add($"ArtifactValidation: {artifact.Reason}");
        }

        var smokeScenarios = await TemplateSmokeScenarioRunner.EvaluateAsync(
            resolvedTemplatePath,
            metadata,
            compile.Success,
            artifact.Success,
            ct);
        var smokePassed = smokeScenarios.All(x => x.Passed);
        if (!smokePassed)
        {
            errors.AddRange(smokeScenarios.Where(x => !x.Passed).Select(x => $"Smoke[{x.Id}]: {x.Details}"));
        }

        var safetyFindings = await RunSafetyScanAsync(resolvedTemplatePath, ct);
        var safetyPassed = safetyFindings.Count == 0;
        if (!safetyPassed)
        {
            errors.AddRange(safetyFindings.Select(x => $"SafetyScan: {x}"));
        }

        var placeholderFindings = await GeneratedArtifactPlaceholderScanner.ScanDirectoryAsync(resolvedTemplatePath, ct);
        var placeholderPassed = placeholderFindings.Count == 0;
        if (!placeholderPassed)
        {
            errors.AddRange(placeholderFindings.Select(x => $"PlaceholderScan: {x.ToDisplayString()}"));
        }

        var passed = metadataPassed && compile.Success && artifact.Success && smokePassed && safetyPassed && placeholderPassed;
        var resolvedPath = ResolveReportPath(reportPath, templateId, version);
        try
        {
            var status = new TemplateCertificationStatus(
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                Passed: passed,
                HasCriticalAlerts: !passed,
                CriticalAlerts: passed ? Array.Empty<string>() : errors.Take(20).ToArray(),
                ReportPath: resolvedPath);
            await TemplateCertificationStatusStore.WriteAsync(resolvedTemplatePath, status, ct);
        }
        catch (Exception ex)
        {
            errors.Add($"CertificationStatus: {ex.Message}");
            passed = false;
        }

        await WriteReportAsync(
            resolvedPath,
            templateId,
            version,
            resolvedTemplatePath,
            metadataPassed,
            compile.Success,
            artifact.Success,
            smokePassed,
            safetyPassed,
            placeholderPassed,
            passed,
            errors,
            smokeScenarios,
            placeholderFindings.Select(x => x.ToDisplayString()).ToArray(),
            ct);

        return new TemplateCertificationReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            TemplateId: templateId,
            Version: version,
            TemplatePath: resolvedTemplatePath,
            MetadataSchemaPassed: metadataPassed,
            CompileGatePassed: compile.Success,
            ArtifactValidationPassed: artifact.Success,
            SmokePassed: smokePassed,
            SafetyScanPassed: safetyPassed,
            Passed: passed,
            Errors: errors,
            SmokeScenarios: smokeScenarios,
            ReportPath: resolvedPath,
            PlaceholderScanPassed: placeholderPassed,
            PlaceholderFindings: placeholderFindings.Select(x => x.ToDisplayString()).ToArray());
    }

    public async Task<TemplateCertificationGateReport> EvaluateGateAsync(
        string? reportPath = null,
        CancellationToken ct = default)
    {
        var violations = new List<string>();
        var reports = new List<TemplateCertificationReport>();
        var templateIds = Directory.Exists(_templatesRoot)
            ? Directory.GetDirectories(_templatesRoot).Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();

        foreach (var templateId in templateIds)
        {
            ct.ThrowIfCancellationRequested();
            var versions = await _lifecycle.GetVersionsAsync(templateId, ct);
            var active = versions.FirstOrDefault(x => x.IsActive) ?? versions.OrderByDescending(x => x.Version, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (active is null)
            {
                violations.Add($"{templateId}: no active/template versions found.");
                continue;
            }

            var report = await CertifyAsync(templateId, active.Version, reportPath: null, templatePath: null, ct: ct);
            reports.Add(report);
            if (!report.Passed)
            {
                violations.Add($"{templateId}:{active.Version} failed certification.");
            }
        }

        var passed = violations.Count == 0;
        var resolvedPath = ResolveGateReportPath(reportPath);
        await WriteGateReportAsync(resolvedPath, passed, reports, violations, ct);
        return new TemplateCertificationGateReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Passed: passed,
            ReportPath: resolvedPath,
            CertifiedCount: reports.Count(x => x.Passed),
            FailedCount: reports.Count(x => !x.Passed) + Math.Max(0, templateIds.Count - reports.Count),
            Violations: violations,
            Templates: reports);
    }

}

