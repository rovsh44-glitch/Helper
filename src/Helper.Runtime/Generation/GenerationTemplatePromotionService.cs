using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed class GenerationTemplatePromotionService : IGenerationTemplatePromotionService
{
    private static readonly Dictionary<string, SemaphoreSlim> TemplateLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object TemplateLocksSync = new();

    private readonly ITemplateRoutingService _routing;
    private readonly ITemplateGeneralizer _generalizer;
    private readonly ITemplateLifecycleService _lifecycle;
    private readonly IGenerationCompileGate _compileGate;
    private readonly ITemplatePromotionFeatureProfileService _profileService;
    private readonly IGenerationMetricsService _metrics;
    private readonly ITemplateCertificationService _certificationService;
    private readonly string _templatesRoot;
    private readonly TemplatePromotionVersionPlanner _versionPlanner;
    private readonly TemplatePromotionMetadataNormalizer _metadataNormalizer;
    private readonly TemplatePromotionScaffoldService _scaffoldService;
    private readonly TemplatePromotionFormatRunner _formatRunner;
    private readonly TemplatePostActivationVerifier _postActivationVerifier;

    public GenerationTemplatePromotionService(
        ITemplateRoutingService routing,
        ITemplateGeneralizer generalizer,
        ITemplateLifecycleService lifecycle,
        IGenerationCompileGate compileGate,
        ITemplatePromotionFeatureProfileService profileService,
        IGenerationMetricsService metrics,
        ITemplateCertificationService certificationService,
        string? templatesRoot = null)
        : this(
            routing,
            generalizer,
            lifecycle,
            compileGate,
            profileService,
            metrics,
            certificationService,
            templatesRoot,
            null,
            null,
            null,
            null)
    {
    }

    internal GenerationTemplatePromotionService(
        ITemplateRoutingService routing,
        ITemplateGeneralizer generalizer,
        ITemplateLifecycleService lifecycle,
        IGenerationCompileGate compileGate,
        ITemplatePromotionFeatureProfileService profileService,
        IGenerationMetricsService metrics,
        ITemplateCertificationService certificationService,
        string? templatesRoot,
        TemplatePromotionVersionPlanner? versionPlanner,
        TemplatePromotionMetadataNormalizer? metadataNormalizer,
        TemplatePromotionScaffoldService? scaffoldService,
        TemplatePromotionFormatRunner? formatRunner,
        TemplatePostActivationVerifier? postActivationVerifier = null)
    {
        _routing = routing;
        _generalizer = generalizer;
        _lifecycle = lifecycle;
        _compileGate = compileGate;
        _profileService = profileService;
        _metrics = metrics;
        _certificationService = certificationService;
        _templatesRoot = HelperWorkspacePathResolver.ResolveTemplatesRoot(templatesRoot);
        _versionPlanner = versionPlanner ?? new TemplatePromotionVersionPlanner();
        _metadataNormalizer = metadataNormalizer ?? new TemplatePromotionMetadataNormalizer();
        _scaffoldService = scaffoldService ?? new TemplatePromotionScaffoldService();
        _formatRunner = formatRunner ?? new TemplatePromotionFormatRunner();
        _postActivationVerifier = postActivationVerifier ?? new TemplatePostActivationVerifier();
    }

    public async Task<TemplatePromotionOutcome> TryPromoteAsync(
        GenerationRequest request,
        GenerationResult result,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        var profile = _profileService.GetCurrent();
        if (!profile.RuntimePromotionEnabled)
        {
            return new TemplatePromotionOutcome(false, false, string.Empty, null, "Template runtime promotion is disabled.", Array.Empty<string>());
        }

        if (!result.Success || !Directory.Exists(result.ProjectPath))
        {
            return new TemplatePromotionOutcome(false, false, string.Empty, null, "Generation result is not eligible for promotion.", Array.Empty<string>());
        }

        var route = await _routing.RouteAsync(request.Prompt, ct);
        var templateId = route.Matched && !string.IsNullOrWhiteSpace(route.TemplateId)
            ? route.TemplateId!
            : _versionPlanner.BuildAutoTemplateId(request.Prompt);
        var templateLock = GetTemplateLock(templateId);
        await templateLock.WaitAsync(ct);
        try
        {
            return await PromoteInternalAsync(templateId, request, result, profile, onProgress, ct);
        }
        finally
        {
            templateLock.Release();
        }
    }

    private async Task<TemplatePromotionOutcome> PromoteInternalAsync(
        string templateId,
        GenerationRequest request,
        GenerationResult result,
        TemplatePromotionFeatureProfile profile,
        Action<string>? onProgress,
        CancellationToken ct)
    {
        var version = await _versionPlanner.ResolveNextVersionAsync(_lifecycle, _templatesRoot, templateId, ct);
        var candidateRoot = Path.Combine(_templatesRoot, templateId, "candidates", version);
        var relativeTarget = Path.GetRelativePath(_templatesRoot, candidateRoot).Replace(Path.DirectorySeparatorChar, '/');
        onProgress?.Invoke($"📦 [TemplatePromotion] Preparing candidate {templateId}:{version}...");

        if (Directory.Exists(candidateRoot))
        {
            TryDeleteDirectory(candidateRoot);
        }

        var generalized = await _generalizer.GeneralizeProjectAsync(result.ProjectPath, relativeTarget, ct);
        if (generalized is null || !Directory.Exists(generalized.RootPath))
        {
            _metrics.RecordTemplatePromotionAttempt(false, "generalize_null");
            return BuildFailedOutcome(templateId, version, "Template generalization failed.", new[] { "Generalizer returned null template." });
        }

        await _metadataNormalizer.NormalizeAsync(generalized.RootPath, templateId, version, request, ct);
        await _scaffoldService.EnsureTemplateProjectFileAsync(generalized.RootPath, templateId, ct);
        var placeholderFindings = await GeneratedArtifactPlaceholderScanner.ScanDirectoryAsync(generalized.RootPath, ct);
        if (placeholderFindings.Count > 0)
        {
            _metrics.RecordTemplatePromotionAttempt(false, "placeholder_scan");
            return BuildFailedOutcome(
                templateId,
                version,
                "Template placeholder scan failed.",
                placeholderFindings.Select(x => $"PlaceholderScan: {x.ToDisplayString()}").ToArray());
        }

        var compile = await RunCompileGateAsync(generalized.RootPath, profile, onProgress, ct);
        if (!compile.Success)
        {
            _metrics.RecordTemplatePromotionAttempt(false, "compile_gate");
            return BuildFailedOutcome(
                templateId,
                version,
                "Template quality checks failed.",
                compile.Errors.Select(x => $"[{x.Code}] {x.Message}").ToArray());
        }

        await _scaffoldService.EnsureCertificationScaffoldAsync(generalized.RootPath, ct);
        var certification = await _certificationService.CertifyAsync(
            templateId,
            version,
            reportPath: null,
            templatePath: generalized.RootPath,
            ct: ct);
        if (!certification.Passed)
        {
            _metrics.RecordTemplatePromotionAttempt(false, "certification");
            return BuildFailedOutcome(templateId, version, "Template certification failed.", certification.Errors);
        }

        var certifiedSnapshot = await _postActivationVerifier.CaptureSnapshotAsync(generalized.RootPath, ct);
        var publishedRoot = Path.Combine(_templatesRoot, templateId, version);
        Directory.CreateDirectory(Path.Combine(_templatesRoot, templateId));
        if (Directory.Exists(publishedRoot))
        {
            _metrics.RecordTemplatePromotionAttempt(false, "version_conflict");
            return BuildFailedOutcome(templateId, version, "Template version already exists.", new[] { $"Path already exists: {publishedRoot}" });
        }

        Directory.Move(generalized.RootPath, publishedRoot);

        if (profile.AutoActivateEnabled)
        {
            var activationOutcome = await TryActivateAsync(templateId, version, publishedRoot, profile, certifiedSnapshot, ct);
            if (activationOutcome is not null)
            {
                return activationOutcome;
            }
        }

        _metrics.RecordTemplatePromotionAttempt(true);
        _metrics.RecordPromoted();
        onProgress?.Invoke($"✅ [TemplatePromotion] Candidate promoted: {templateId}:{version}.");
        return new TemplatePromotionOutcome(true, true, templateId, version, "Template promotion completed.", Array.Empty<string>());
    }

    private async Task<CompileGateResult> RunCompileGateAsync(
        string rootPath,
        TemplatePromotionFeatureProfile profile,
        Action<string>? onProgress,
        CancellationToken ct)
    {
        var compile = await _compileGate.ValidateAsync(rootPath, ct);
        TryDeleteDirectory(Path.Combine(rootPath, ".compile_gate"));
        var formatFixOutcomeRecorded = false;

        if (!compile.Success &&
            profile.FormatMode == CompileGateFormatMode.Strict &&
            _formatRunner.IsFormattingOnlyFailure(compile.Errors))
        {
            onProgress?.Invoke("🧹 [TemplatePromotion] Trying auto-format before final compile verdict...");
            var formatted = await _formatRunner.TryRunDotnetFormatAsync(rootPath, ct);
            _metrics.RecordTemplatePromotionFormatFixResult(formatted);
            formatFixOutcomeRecorded = true;
            if (formatted)
            {
                compile = await _compileGate.ValidateAsync(rootPath, ct);
                TryDeleteDirectory(Path.Combine(rootPath, ".compile_gate"));
                if (!compile.Success && _formatRunner.IsFormattingOnlyFailure(compile.Errors))
                {
                    _metrics.RecordTemplatePromotionFormatFixResult(false);
                }
            }
        }

        if (!formatFixOutcomeRecorded &&
            !compile.Success &&
            profile.FormatMode == CompileGateFormatMode.Strict &&
            _formatRunner.IsFormattingOnlyFailure(compile.Errors))
        {
            _metrics.RecordTemplatePromotionFormatFixResult(false);
        }

        return compile;
    }

    private async Task<TemplatePromotionOutcome?> TryActivateAsync(
        string templateId,
        string version,
        string publishedRoot,
        TemplatePromotionFeatureProfile profile,
        TemplateTreeIntegritySnapshot certifiedSnapshot,
        CancellationToken ct)
    {
        var previousActive = await _versionPlanner.GetCurrentActiveVersionAsync(_lifecycle, templateId, ct);
        var activated = await _lifecycle.ActivateVersionAsync(templateId, version, ct);
        if (!activated.Success)
        {
            TryDeleteDirectory(publishedRoot);
            if (!string.IsNullOrWhiteSpace(previousActive))
            {
                await _lifecycle.ActivateVersionAsync(templateId, previousActive, ct);
            }

            _metrics.RecordTemplatePromotionAttempt(false, "activation");
            return BuildFailedOutcome(templateId, version, "Template activation failed.", new[] { activated.Message });
        }

        if (profile.PostActivationFullRecertifyEnabled)
        {
            var activeCertification = await _certificationService.CertifyAsync(
                templateId,
                version,
                reportPath: null,
                templatePath: publishedRoot,
                ct: ct);
            if (activeCertification.Passed)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(previousActive))
            {
                await _lifecycle.ActivateVersionAsync(templateId, previousActive, ct);
            }

            TryDeleteDirectory(publishedRoot);
            _metrics.RecordTemplatePromotionAttempt(false, "post_activation_certification");
            return BuildFailedOutcome(templateId, version, "Post-activation certification failed.", activeCertification.Errors);
        }

        var activeVerification = await _postActivationVerifier.VerifyAsync(
            _lifecycle,
            templateId,
            version,
            publishedRoot,
            certifiedSnapshot,
            ct);
        if (activeVerification.Passed)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(previousActive))
        {
            await _lifecycle.ActivateVersionAsync(templateId, previousActive, ct);
        }

        TryDeleteDirectory(publishedRoot);
        _metrics.RecordTemplatePromotionAttempt(false, "post_activation_verification");
        return BuildFailedOutcome(templateId, version, "Post-activation verification failed.", activeVerification.Errors);
    }

    private static SemaphoreSlim GetTemplateLock(string templateId)
    {
        lock (TemplateLocksSync)
        {
            if (TemplateLocks.TryGetValue(templateId, out var existing))
            {
                return existing;
            }

            var created = new SemaphoreSlim(1, 1);
            TemplateLocks[templateId] = created;
            return created;
        }
    }

    private static TemplatePromotionOutcome BuildFailedOutcome(
        string templateId,
        string version,
        string message,
        params IEnumerable<string>[] errorCollections)
    {
        var errors = errorCollections
            .Where(x => x is not null)
            .SelectMany(x => x)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        return new TemplatePromotionOutcome(
            Attempted: true,
            Success: false,
            TemplateId: templateId,
            Version: version,
            Message: message,
            Errors: errors);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // keep diagnostics artifacts if cleanup fails
        }
    }
}

