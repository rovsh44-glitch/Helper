using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Swarm.Agents;

namespace Helper.Runtime.Swarm;

internal sealed class TumenFileBatchService
{
    private readonly ArbanAgent _arban;
    private readonly ZuunAssembler _zuun;
    private readonly IIntentBcaster _bcaster;
    private readonly IGenerationPathSanitizer _pathSanitizer;
    private readonly IGeneratedFileAstValidator _fileAstValidator;
    private readonly TumenRuntimeSettings _runtimeSettings;
    private readonly TumenCritiqueRunner _critiqueRunner;
    private readonly TumenGeneratedFileStore _fileStore;

    public TumenFileBatchService(
        ArbanAgent arban,
        ZuunAssembler zuun,
        IIntentBcaster bcaster,
        IGenerationPathSanitizer pathSanitizer,
        IGeneratedFileAstValidator fileAstValidator,
        TumenRuntimeSettings runtimeSettings,
        TumenCritiqueRunner critiqueRunner,
        TumenGeneratedFileStore fileStore)
    {
        _arban = arban;
        _zuun = zuun;
        _bcaster = bcaster;
        _pathSanitizer = pathSanitizer;
        _fileAstValidator = fileAstValidator;
        _runtimeSettings = runtimeSettings;
        _critiqueRunner = critiqueRunner;
        _fileStore = fileStore;
    }

    public async Task<TumenFileBatchResult> GenerateAsync(TumenFileBatchRequest request, CancellationToken ct)
    {
        var files = new List<GeneratedFile>();
        var placeholderFindings = new List<GeneratedArtifactPlaceholderFinding>();
        var errors = new List<string>();
        var warnings = new List<string>();
        var methodCount = 0;
        var retryCount = 0;
        var knownSanitizedPaths = BuildKnownSanitizedPaths(request.FileDefinitions);

        foreach (var fileDef in request.FileDefinitions)
        {
            await _bcaster.BroadcastIntentAsync("Atomic File Generation", $"Generating and validating {fileDef.Path}.", request.OnProgress, ct);
            var pathResult = _pathSanitizer.SanitizeRelativePath(fileDef.Path);
            if (!pathResult.IsValid || string.IsNullOrWhiteSpace(pathResult.SanitizedPath))
            {
                errors.AddRange(pathResult.Errors.Select(x => $"{fileDef.Path}: {x}"));
                continue;
            }

            warnings.AddRange(pathResult.Warnings.Select(x => $"{fileDef.Path}: {x}"));
            var safeRelativePath = pathResult.SanitizedPath;
            request.OnProgress?.Invoke($"   🏹 [Zuun] Assembling {safeRelativePath}...");
            var className = _fileStore.ResolveClassName(safeRelativePath);

            if (!safeRelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                var nonCodeContent = TumenFallbackContentBuilder.BuildNonCodeFileContent(
                    request.RootNamespace,
                    safeRelativePath,
                    fileDef.Role,
                    knownSanitizedPaths,
                    _fileStore.ResolveClassName);
                placeholderFindings.AddRange(GeneratedArtifactPlaceholderScanner.ScanContent(safeRelativePath, nonCodeContent));
                _fileStore.SaveGeneratedFile(request.RawProjectRoot, safeRelativePath, nonCodeContent);
                files.Add(new GeneratedFile(safeRelativePath, nonCodeContent));
                continue;
            }

            var methods = PrepareMethods(fileDef, safeRelativePath, warnings);
            methodCount += methods.Count;

            var fileTask = new TumenFileTask(
                safeRelativePath,
                fileDef.Role,
                className,
                request.RootNamespace,
                methods,
                _fileStore.BuildUsings(request.RootNamespace, safeRelativePath, fileDef.Role, methods));

            var methodResults = await GenerateInitialMethodResultsAsync(fileTask, className, ct).ConfigureAwait(false);
            retryCount += methodResults.Sum(x => Math.Max(0, x.Attempts - 1));

            var code = _zuun.AssembleFile(fileTask, methodResults, out var assemblyDiagnostics);
            if (assemblyDiagnostics.Count > 0)
            {
                warnings.AddRange(assemblyDiagnostics.Select(x => $"{safeRelativePath}: {x}"));
            }

            var criticResult = await ApplyCriticAsync(request, fileDef, fileTask, className, safeRelativePath, code, warnings, ct).ConfigureAwait(false);
            code = criticResult.Code;
            retryCount += criticResult.RetryCount;

            var astValidation = _fileAstValidator.ValidateFile(
                safeRelativePath,
                code,
                fileDef.Role,
                request.RootNamespace,
                className);

            warnings.AddRange(astValidation.Warnings.Select(x => $"{safeRelativePath}: {x}"));
            if (!astValidation.IsValid)
            {
                errors.AddRange(astValidation.Errors.Select(x => $"{safeRelativePath}: {x}"));
                code = TumenFallbackContentBuilder.BuildFallbackFile(request.RootNamespace, className, fileDef.Role);
            }

            placeholderFindings.AddRange(GeneratedArtifactPlaceholderScanner.ScanContent(safeRelativePath, code));
            _fileStore.SaveGeneratedFile(request.RawProjectRoot, safeRelativePath, code);
            files.Add(new GeneratedFile(safeRelativePath, code));
        }

        return new TumenFileBatchResult(files, placeholderFindings, errors, warnings, methodCount, retryCount);
    }

    private HashSet<string> BuildKnownSanitizedPaths(IReadOnlyList<SwarmFileDefinition> fileDefinitions)
    {
        var knownSanitizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileDef in fileDefinitions)
        {
            var knownPathResult = _pathSanitizer.SanitizeRelativePath(fileDef.Path);
            if (knownPathResult.IsValid && !string.IsNullOrWhiteSpace(knownPathResult.SanitizedPath))
            {
                knownSanitizedPaths.Add(knownPathResult.SanitizedPath);
            }
        }

        return knownSanitizedPaths;
    }

    private List<ArbanMethodTask> PrepareMethods(
        SwarmFileDefinition fileDef,
        string safeRelativePath,
        List<string> warnings)
    {
        var methods = fileDef.Methods ?? new List<ArbanMethodTask>
        {
            new("Execute", "public void Execute()", "Logic", string.Empty)
        };

        if (safeRelativePath.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase))
        {
            var filtered = methods
                .Where(m => !string.Equals(m.Name, "InitializeComponent", StringComparison.OrdinalIgnoreCase))
                .Where(m => m.Signature is null || !m.Signature.Contains("InitializeComponent(", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count != methods.Count)
            {
                warnings.Add($"{safeRelativePath}: removed InitializeComponent from code-behind methods to avoid duplicate member generation.");
            }

            methods = filtered;
        }

        if (methods.Count > _runtimeSettings.SmokeMaxMethodsPerFile)
        {
            warnings.Add($"Smoke profile truncated methods in '{safeRelativePath}' from {methods.Count} to {_runtimeSettings.SmokeMaxMethodsPerFile}.");
            methods = methods.Take(_runtimeSettings.SmokeMaxMethodsPerFile).ToList();
        }

        return methods;
    }

    private async Task<List<ArbanResult>> GenerateInitialMethodResultsAsync(TumenFileTask fileTask, string className, CancellationToken ct)
    {
        var arbanTasks = fileTask.Methods.Select(m => _arban.ImplementMethodAsync(m, className, ct));
        return (await Task.WhenAll(arbanTasks).ConfigureAwait(false)).ToList();
    }

    private async Task<(string Code, int RetryCount)> ApplyCriticAsync(
        TumenFileBatchRequest request,
        SwarmFileDefinition fileDef,
        TumenFileTask fileTask,
        string className,
        string safeRelativePath,
        string code,
        List<string> warnings,
        CancellationToken ct)
    {
        var retryCount = 0;
        var attempts = 0;
        var runCritic = TumenCriticPolicy.ShouldRunCriticFor(fileDef.Role, fileTask.Methods);
        var seenFeedback = new HashSet<string>(StringComparer.Ordinal);
        while (runCritic && attempts < _runtimeSettings.SmokeCriticAttempts)
        {
            var critique = await _critiqueRunner.RunAsync(
                $"Task: {fileDef.Purpose}. Methods planned: {string.Join(", ", fileTask.Methods.Select(m => m.Name))}\nPlatform: {request.PlatformOs}",
                code,
                $"File: {safeRelativePath} in Namespace: {request.RootNamespace}",
                ct);

            if (critique.IsApproved)
            {
                break;
            }

            var normalizedFeedback = TumenCriticPolicy.NormalizeFeedbackKey(critique.Feedback);
            if (!seenFeedback.Add(normalizedFeedback))
            {
                warnings.Add($"{safeRelativePath}: repeated critic feedback detected; keeping current draft.");
                break;
            }

            if (TumenCriticPolicy.IsNonActionableCritique(critique.Feedback, fileDef.Role))
            {
                warnings.Add($"{safeRelativePath}: critic feedback treated as advisory ('{TumenCriticPolicy.TrimForLog(critique.Feedback)}').");
                break;
            }

            await _bcaster.BroadcastIntentAsync("Atomic Fix", $"Criticism: {critique.Feedback}", request.OnProgress, ct);
            request.OnProgress?.Invoke($"   🩹 [Critic] Self-correction attempt {attempts + 1}: {critique.Feedback}");

            var boundedFeedback = TumenCriticPolicy.TrimForPrompt(critique.Feedback);
            var retryResults = new List<ArbanResult>();
            foreach (var method in fileTask.Methods)
            {
                var retryPrompt = string.IsNullOrWhiteSpace(boundedFeedback)
                    ? $"FOLLOW PREVIOUS TASK. Improve correctness and keep deterministic C# logic.\nOriginal Context: {method.Purpose}"
                    : $"FOLLOW PREVIOUS TASK BUT FIX CRITIC FEEDBACK: {boundedFeedback}\nOriginal Context: {method.Purpose}";
                var retry = await _arban.ImplementMethodAsync(method with { Purpose = retryPrompt }, className, ct);
                retryCount += Math.Max(0, retry.Attempts - 1);
                retryResults.Add(retry);
            }

            code = _zuun.AssembleFile(fileTask, retryResults, out var retryDiagnostics);
            if (retryDiagnostics.Count > 0)
            {
                warnings.AddRange(retryDiagnostics.Select(x => $"{safeRelativePath}: {x}"));
            }

            attempts++;
        }

        return (code, retryCount);
    }
}

