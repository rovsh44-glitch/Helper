namespace Helper.Runtime.Knowledge.Chunking;

public sealed class SemanticChunkBoundaryService
{
    private const int MergeThresholdTokens = 40;
    private readonly bool _enabled;

    public SemanticChunkBoundaryService()
    {
        _enabled = IsFeatureEnabled();
    }

    public bool Enabled => _enabled;

    public IReadOnlyList<string> Refine(IReadOnlyList<string> chunks, bool enabled)
    {
        if (!enabled || chunks.Count <= 1)
        {
            return chunks;
        }

        var normalized = chunks
            .Select(StructuredParserUtilities.NormalizeWhitespace)
            .Where(static chunk => !string.IsNullOrWhiteSpace(chunk))
            .ToList();

        if (normalized.Count <= 1)
        {
            return normalized;
        }

        var refined = new List<string>(normalized.Count);
        foreach (var chunk in normalized)
        {
            if (refined.Count == 0)
            {
                refined.Add(chunk);
                continue;
            }

            if (ShouldMergeIntoPrevious(chunk))
            {
                refined[^1] = $"{refined[^1]} {chunk}".Trim();
                continue;
            }

            refined.Add(chunk);
        }

        if (refined.Count > 1 && ShouldMergeIntoPrevious(refined[0]))
        {
            refined[1] = $"{refined[0]} {refined[1]}".Trim();
            refined.RemoveAt(0);
        }

        return refined;
    }

    public static bool IsFeatureEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_FF_CHUNK_SEMANTIC_REFINEMENT");
        return !bool.TryParse(raw, out var parsed) || parsed;
    }

    private static bool ShouldMergeIntoPrevious(string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk))
        {
            return false;
        }

        if (ChunkingHelpers.EstimateTokenCount(chunk) <= MergeThresholdTokens)
        {
            return true;
        }

        var lettersOrDigits = chunk.Count(char.IsLetterOrDigit);
        var distinctTerms = chunk
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => token.Trim('.', ',', ':', ';', '-', '(', ')', '[', ']', '"', '\'').ToLowerInvariant())
            .Where(static token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Count();

        return lettersOrDigits > 0 && distinctTerms > 0 && distinctTerms <= 4 && chunk.Length < 180;
    }
}

