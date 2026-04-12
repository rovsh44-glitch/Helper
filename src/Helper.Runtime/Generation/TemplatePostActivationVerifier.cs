using System.Security.Cryptography;
using System.Text;
using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

internal sealed record TemplateTreeIntegritySnapshot(int FileCount, string Digest);

internal sealed record TemplatePostActivationVerificationResult(bool Passed, IReadOnlyList<string> Errors);

internal sealed class TemplatePostActivationVerifier
{
    private static readonly HashSet<string> IgnoredSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".compile_gate"
    };

    public async Task<TemplateTreeIntegritySnapshot> CaptureSnapshotAsync(string rootPath, CancellationToken ct)
    {
        var entries = new List<string>();
        foreach (var file in EnumerateRelevantFiles(rootPath))
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)).ToLowerInvariant();
            var relative = Path.GetRelativePath(rootPath, file).Replace(Path.DirectorySeparatorChar, '/');
            entries.Add($"{relative}|{new FileInfo(file).Length}|{hash}");
        }

        entries.Sort(StringComparer.Ordinal);
        var manifest = string.Join("\n", entries);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        return new TemplateTreeIntegritySnapshot(entries.Count, digest);
    }

    public async Task<TemplatePostActivationVerificationResult> VerifyAsync(
        ITemplateLifecycleService lifecycle,
        string templateId,
        string version,
        string publishedRoot,
        TemplateTreeIntegritySnapshot certifiedSnapshot,
        CancellationToken ct)
    {
        var errors = new List<string>();
        if (!Directory.Exists(publishedRoot))
        {
            errors.Add($"Published template root is missing: {publishedRoot}");
            return new TemplatePostActivationVerificationResult(false, errors);
        }

        var publishedSnapshot = await CaptureSnapshotAsync(publishedRoot, ct).ConfigureAwait(false);
        if (publishedSnapshot.FileCount != certifiedSnapshot.FileCount || !string.Equals(publishedSnapshot.Digest, certifiedSnapshot.Digest, StringComparison.Ordinal))
        {
            errors.Add("Published template tree no longer matches the candidate tree that passed certification.");
        }

        var status = TemplateCertificationStatusStore.TryRead(publishedRoot);
        if (status is null)
        {
            errors.Add("Published template is missing certification status.");
        }
        else
        {
            if (!status.Passed)
            {
                errors.Add("Published template certification status is not passed.");
            }

            if (string.IsNullOrWhiteSpace(status.ReportPath) || !File.Exists(status.ReportPath))
            {
                errors.Add("Published template certification report path is missing or unreadable.");
            }
        }

        var versions = await lifecycle.GetVersionsAsync(templateId, ct).ConfigureAwait(false);
        if (!versions.Any(x => x.IsActive && string.Equals(x.Version, version, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Lifecycle did not report '{templateId}:{version}' as active after activation.");
        }

        return new TemplatePostActivationVerificationResult(errors.Count == 0, errors);
    }

    private static IEnumerable<string> EnumerateRelevantFiles(string rootPath)
    {
        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(rootPath, file);
            var segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(IgnoredSegments.Contains))
            {
                continue;
            }

            yield return file;
        }
    }
}
