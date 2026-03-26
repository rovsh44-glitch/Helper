using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helper.GeneratedArtifactValidation.Contracts;

public enum FileRole
{
    Infrastructure,
    Model,
    Interface,
    ViewModel,
    View,
    Service,
    Logic,
    Contract,
    Configuration,
    Script,
    Resource,
    Test
}

public sealed class FileRoleJsonConverter : JsonConverter<FileRole>
{
    private static readonly Dictionary<string, FileRole> RoleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["infrastructure"] = FileRole.Infrastructure,
        ["infra"] = FileRole.Infrastructure,
        ["model"] = FileRole.Model,
        ["models"] = FileRole.Model,
        ["entity"] = FileRole.Model,
        ["interface"] = FileRole.Interface,
        ["interfaces"] = FileRole.Interface,
        ["contractinterface"] = FileRole.Interface,
        ["viewmodel"] = FileRole.ViewModel,
        ["viewmodels"] = FileRole.ViewModel,
        ["vm"] = FileRole.ViewModel,
        ["view"] = FileRole.View,
        ["views"] = FileRole.View,
        ["xaml"] = FileRole.View,
        ["service"] = FileRole.Service,
        ["services"] = FileRole.Service,
        ["logic"] = FileRole.Logic,
        ["businesslogic"] = FileRole.Logic,
        ["domainlogic"] = FileRole.Logic,
        ["contract"] = FileRole.Contract,
        ["contracts"] = FileRole.Contract,
        ["configuration"] = FileRole.Configuration,
        ["config"] = FileRole.Configuration,
        ["script"] = FileRole.Script,
        ["scripts"] = FileRole.Script,
        ["resource"] = FileRole.Resource,
        ["resources"] = FileRole.Resource,
        ["asset"] = FileRole.Resource,
        ["assets"] = FileRole.Resource,
        ["test"] = FileRole.Test,
        ["tests"] = FileRole.Test
    };

    public override FileRole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericRole))
        {
            if (Enum.IsDefined(typeof(FileRole), numericRole))
            {
                return (FileRole)numericRole;
            }

            return FileRole.Logic;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            return FileRole.Logic;
        }

        var raw = reader.GetString();
        return TryParse(raw, out var parsed) ? parsed : FileRole.Logic;
    }

    public override void Write(Utf8JsonWriter writer, FileRole value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());

    public static bool TryParse(string? raw, out FileRole role)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            role = FileRole.Logic;
            return false;
        }

        var normalized = new string(raw.Where(char.IsLetterOrDigit).ToArray());
        if (RoleAliases.TryGetValue(normalized, out role))
        {
            return true;
        }

        if (Enum.TryParse(raw, true, out role))
        {
            return true;
        }

        role = FileRole.Logic;
        return false;
    }
}

public sealed record ArbanMethodTask(string Name, string Signature, string Purpose, string ContextHints);

public sealed record SwarmFileDefinition(
    string Path,
    string Purpose,
    FileRole Role,
    List<string> Dependencies,
    List<ArbanMethodTask>? Methods = null);

public sealed record SwarmBlueprint(
    string ProjectName,
    string RootNamespace,
    List<SwarmFileDefinition> Files,
    List<string> NuGetPackages);

public sealed record GeneratedFile(string RelativePath, string Content, string Language = "csharp");

public sealed record BuildError(string File, int Line, string Code, string Message);

public sealed record PathSanitizationResult(
    bool IsValid,
    string? SanitizedPath,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record MethodSignatureValidationResult(
    bool IsValid,
    string? NormalizedSignature,
    IReadOnlyList<string> Errors);

public sealed record MethodSignatureNormalizationResult(
    bool IsValid,
    string? Signature,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record BlueprintValidationResult(
    bool IsValid,
    SwarmBlueprint? Blueprint,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record GeneratedFileValidationResult(
    bool IsValid,
    string SanitizedCode,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record GeneratedArtifactPlaceholderFinding(
    string RelativePath,
    int? LineNumber,
    string RuleId,
    string Evidence)
{
    public string ToDisplayString()
    {
        var location = LineNumber.HasValue
            ? $"{RelativePath}:{LineNumber.Value}"
            : RelativePath;
        return $"{location} [{RuleId}] {Evidence}";
    }
}

public sealed record ArtifactFixtureFile(
    string FixtureFile,
    string RelativePath,
    FileRole Role,
    string ExpectedNamespace,
    string ExpectedTypeName);

public sealed record ArtifactFixtureManifest(
    string RootNamespace,
    List<ArtifactFixtureFile> Files);

public sealed record ArtifactFileValidationResult(
    string RelativePath,
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<GeneratedArtifactPlaceholderFinding> PlaceholderFindings);

public sealed record ArtifactValidationReport(
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<GeneratedArtifactPlaceholderFinding> PlaceholderFindings,
    IReadOnlyList<ArtifactFileValidationResult> Files);

public sealed record CompileGateResult(
    bool Success,
    IReadOnlyList<BuildError> Errors,
    string CompileWorkspace);
