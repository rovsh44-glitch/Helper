using Helper.GeneratedArtifactValidation.Contracts;

namespace Helper.GeneratedArtifactValidation.Core;

public sealed class BlueprintContractValidator
{
    private readonly GenerationPathSanitizer _pathSanitizer;
    private readonly IdentifierSanitizer _identifierSanitizer;
    private readonly MethodSignatureNormalizer _signatureNormalizer;

    public BlueprintContractValidator()
        : this(
            new GenerationPathSanitizer(),
            new IdentifierSanitizer(),
            new MethodSignatureNormalizer(new MethodSignatureValidator()))
    {
    }

    internal BlueprintContractValidator(
        GenerationPathSanitizer pathSanitizer,
        IdentifierSanitizer identifierSanitizer,
        MethodSignatureNormalizer signatureNormalizer)
    {
        _pathSanitizer = pathSanitizer;
        _identifierSanitizer = identifierSanitizer;
        _signatureNormalizer = signatureNormalizer;
    }

    public BlueprintValidationResult ValidateAndNormalize(SwarmBlueprint blueprint)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var projectName = _identifierSanitizer.SanitizeProjectName(blueprint.ProjectName);
        if (!string.Equals(projectName, blueprint.ProjectName, StringComparison.Ordinal))
        {
            warnings.Add($"ProjectName sanitized from '{blueprint.ProjectName}' to '{projectName}'.");
        }

        var rootNamespace = _identifierSanitizer.SanitizeNamespace(blueprint.RootNamespace);
        if (!string.Equals(rootNamespace, blueprint.RootNamespace, StringComparison.Ordinal))
        {
            warnings.Add($"RootNamespace sanitized from '{blueprint.RootNamespace}' to '{rootNamespace}'.");
        }

        if (blueprint.Files is null || blueprint.Files.Count == 0)
        {
            errors.Add("Blueprint must contain at least one file.");
            return new BlueprintValidationResult(false, null, errors, warnings);
        }

        var sanitizedFiles = new List<SwarmFileDefinition>(blueprint.Files.Count);
        foreach (var file in blueprint.Files)
        {
            var sanitizedPath = _pathSanitizer.SanitizeRelativePath(file.Path);
            if (!sanitizedPath.IsValid || string.IsNullOrWhiteSpace(sanitizedPath.SanitizedPath))
            {
                errors.AddRange(sanitizedPath.Errors.Select(e => $"File '{file.Path}': {e}"));
                continue;
            }

            warnings.AddRange(sanitizedPath.Warnings.Select(w => $"File '{file.Path}': {w}"));
            if (!seenPaths.Add(sanitizedPath.SanitizedPath))
            {
                errors.Add($"Duplicate file path after normalization: '{sanitizedPath.SanitizedPath}'.");
                continue;
            }

            if (!Enum.IsDefined(typeof(FileRole), file.Role))
            {
                errors.Add($"File '{file.Path}' has unsupported role '{file.Role}'.");
                continue;
            }

            var methods = NormalizeMethods(file, sanitizedPath.SanitizedPath, errors, warnings);
            var dependencies = (file.Dependencies ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            sanitizedFiles.Add(new SwarmFileDefinition(
                sanitizedPath.SanitizedPath,
                string.IsNullOrWhiteSpace(file.Purpose) ? "Generated file" : file.Purpose.Trim(),
                file.Role,
                dependencies,
                methods));
        }

        if (errors.Count > 0)
        {
            return new BlueprintValidationResult(false, null, errors, warnings);
        }

        var packages = (blueprint.NuGetPackages ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var normalizedBlueprint = new SwarmBlueprint(projectName, rootNamespace, sanitizedFiles, packages);
        return new BlueprintValidationResult(true, normalizedBlueprint, errors, warnings);
    }

    private List<ArbanMethodTask>? NormalizeMethods(
        SwarmFileDefinition file,
        string sanitizedPath,
        List<string> errors,
        List<string> warnings)
    {
        if (!sanitizedPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var methods = file.Methods ?? new List<ArbanMethodTask>();
        if (methods.Count == 0 && file.Role != FileRole.Interface)
        {
            warnings.Add($"File '{sanitizedPath}' had no methods; default Execute() method added.");
            methods =
            [
                new ArbanMethodTask("Execute", "public void Execute()", "Default execution entry point", string.Empty)
            ];
        }

        var normalizedMethods = new List<ArbanMethodTask>(methods.Count);
        var seenSignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            var methodName = _identifierSanitizer.SanitizeMethodName(method.Name);
            if (!string.Equals(methodName, method.Name, StringComparison.Ordinal))
            {
                warnings.Add($"Method name '{method.Name}' sanitized to '{methodName}' in '{sanitizedPath}'.");
            }

            var normalization = _signatureNormalizer.Normalize(method.Signature, file.Role, methodName);
            warnings.AddRange(normalization.Warnings.Select(x => $"File '{sanitizedPath}', method '{method.Name}': {x}"));
            if (!normalization.IsValid || string.IsNullOrWhiteSpace(normalization.Signature))
            {
                errors.AddRange(normalization.Errors.Select(x => $"File '{sanitizedPath}', method '{method.Name}': {x}"));
                continue;
            }

            var normalizedSignature = normalization.Signature!;
            if (!seenSignatures.Add(normalizedSignature))
            {
                warnings.Add($"File '{sanitizedPath}' duplicate signature dropped: '{normalizedSignature}'.");
                continue;
            }

            normalizedMethods.Add(new ArbanMethodTask(
                methodName,
                normalizedSignature,
                string.IsNullOrWhiteSpace(method.Purpose) ? "Generated method" : method.Purpose.Trim(),
                method.ContextHints ?? string.Empty));
        }

        if (normalizedMethods.Count == 0 && file.Role != FileRole.Interface)
        {
            warnings.Add($"File '{sanitizedPath}' had no valid methods after normalization. Injected default Execute().");
            normalizedMethods.Add(new ArbanMethodTask(
                "Execute",
                "public void Execute()",
                "Deterministic fallback method",
                string.Empty));
        }

        return normalizedMethods;
    }
}
