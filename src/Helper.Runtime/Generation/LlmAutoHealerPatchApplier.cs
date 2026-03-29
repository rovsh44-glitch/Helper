using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed class LlmAutoHealerPatchApplier : IFixPatchApplier
{
    private readonly IAutoHealer _autoHealer;

    public LlmAutoHealerPatchApplier(IAutoHealer autoHealer)
    {
        _autoHealer = autoHealer;
    }

    public FixStrategyKind Strategy => FixStrategyKind.LlmAutoHealer;

    public async Task<FixPatchApplyResult> ApplyAsync(
        FixPatchApplyContext context,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(context.CurrentResult.ProjectPath))
        {
            return new FixPatchApplyResult(
                Applied: false,
                Success: false,
                Errors: context.CurrentResult.Errors,
                ChangedFiles: Array.Empty<string>(),
                Notes: "Project path does not exist for LLM auto-heal.");
        }

        var before = SnapshotHashes(context.CurrentResult.ProjectPath);
        var remainingErrors = await _autoHealer.HealAsync(
            context.CurrentResult.ProjectPath,
            context.CurrentResult.Errors.ToList(),
            context.OnProgress,
            ct);
        var after = SnapshotHashes(context.CurrentResult.ProjectPath);
        var changedFiles = GetChangedFiles(before, after);

        var success = remainingErrors.Count == 0;
        var notes = success
            ? "LLM auto-healer resolved all reported errors."
            : $"LLM auto-healer finished with {remainingErrors.Count} remaining error(s).";

        return new FixPatchApplyResult(
            Applied: changedFiles.Count > 0,
            Success: success,
            Errors: remainingErrors,
            ChangedFiles: changedFiles,
            Notes: notes);
    }

    private static Dictionary<string, string> SnapshotHashes(string root)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var extension = Path.GetExtension(file);
            if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var content = File.ReadAllText(file);
            hashes[relative] = content;
        }

        return hashes;
    }

    private static IReadOnlyList<string> GetChangedFiles(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        var changed = new List<string>();
        var allKeys = new HashSet<string>(before.Keys, StringComparer.OrdinalIgnoreCase);
        allKeys.UnionWith(after.Keys);

        foreach (var key in allKeys)
        {
            before.TryGetValue(key, out var beforeContent);
            after.TryGetValue(key, out var afterContent);
            if (!string.Equals(beforeContent, afterContent, StringComparison.Ordinal))
            {
                changed.Add(key);
            }
        }

        changed.Sort(StringComparer.OrdinalIgnoreCase);
        return changed;
    }
}

