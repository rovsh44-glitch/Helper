namespace Helper.Runtime.WebResearch.Normalization;

public interface IDocumentSourceNormalizationPolicy
{
    DocumentSourceNormalizationDecision Normalize(Uri sourceUri);
}

public sealed record DocumentSourceNormalizationDecision(
    Uri EffectiveUri,
    string SourceKind,
    bool WasNormalized,
    IReadOnlyList<string> Trace);

public sealed class DocumentSourceNormalizationPolicy : IDocumentSourceNormalizationPolicy
{
    public DocumentSourceNormalizationDecision Normalize(Uri sourceUri)
    {
        if (!sourceUri.IsAbsoluteUri)
        {
            return Unchanged(sourceUri, "invalid_source");
        }

        if (TryNormalizeGitHubBlob(sourceUri, out var gitHubRaw))
        {
            return Normalized(sourceUri, gitHubRaw, "github_blob_document");
        }

        if (TryNormalizeGitLabBlob(sourceUri, out var gitLabRaw))
        {
            return Normalized(sourceUri, gitLabRaw, "gitlab_blob_document");
        }

        if (TryNormalizeArxivAbstract(sourceUri, out var arxivPdf))
        {
            return Normalized(sourceUri, arxivPdf, "arxiv_pdf");
        }

        if (LooksLikeDirectDocument(sourceUri))
        {
            return Unchanged(sourceUri, "direct_document");
        }

        return Unchanged(sourceUri, "web_page");
    }

    private static bool TryNormalizeGitHubBlob(Uri sourceUri, out Uri normalizedUri)
    {
        normalizedUri = sourceUri;
        if (!string.Equals(sourceUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = sourceUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 5 || !string.Equals(segments[2], "blob", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!LooksLikeDocumentPath(segments[^1]))
        {
            return false;
        }

        var owner = segments[0];
        var repo = segments[1];
        var branch = segments[3];
        var relativePath = string.Join('/', segments.Skip(4));
        normalizedUri = new Uri($"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{relativePath}");
        return true;
    }

    private static bool TryNormalizeGitLabBlob(Uri sourceUri, out Uri normalizedUri)
    {
        normalizedUri = sourceUri;
        if (!string.Equals(sourceUri.Host, "gitlab.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var marker = "/-/blob/";
        var absolutePath = sourceUri.AbsolutePath;
        var markerIndex = absolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var suffix = absolutePath[(markerIndex + marker.Length)..];
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return false;
        }

        var suffixSegments = suffix.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (suffixSegments.Length < 2 || !LooksLikeDocumentPath(suffixSegments[^1]))
        {
            return false;
        }

        var prefix = absolutePath[..markerIndex];
        normalizedUri = new Uri($"{sourceUri.Scheme}://{sourceUri.Host}{prefix}/-/raw/{suffix}");
        return true;
    }

    private static bool TryNormalizeArxivAbstract(Uri sourceUri, out Uri normalizedUri)
    {
        normalizedUri = sourceUri;
        if (!sourceUri.Host.Contains("arxiv.org", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!sourceUri.AbsolutePath.StartsWith("/abs/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var identifier = sourceUri.AbsolutePath["/abs/".Length..].Trim('/');
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        normalizedUri = new Uri($"https://arxiv.org/pdf/{identifier}.pdf");
        return true;
    }

    private static bool LooksLikeDirectDocument(Uri sourceUri)
        => LooksLikeDocumentPath(sourceUri.AbsolutePath);

    private static bool LooksLikeDocumentPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static DocumentSourceNormalizationDecision Unchanged(Uri sourceUri, string sourceKind)
    {
        return new DocumentSourceNormalizationDecision(
            sourceUri,
            sourceKind,
            WasNormalized: false,
            new[]
            {
                $"web_source.normalized=no kind={sourceKind} original={sourceUri} effective={sourceUri}"
            });
    }

    private static DocumentSourceNormalizationDecision Normalized(Uri originalUri, Uri effectiveUri, string sourceKind)
    {
        return new DocumentSourceNormalizationDecision(
            effectiveUri,
            sourceKind,
            WasNormalized: true,
            new[]
            {
                $"web_source.normalized=yes kind={sourceKind} original={originalUri} effective={effectiveUri}"
            });
    }
}

