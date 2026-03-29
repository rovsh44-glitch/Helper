using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Chunking;

internal static class StructuredChunkCompactor
{
    private static readonly int MinChunkTokensBeforeMerge = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_COMPACT_MIN_CHUNK_TOKENS", 80, 20, 400);
    private static readonly int MaxLowSignalChunkChars = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_COMPACT_MAX_LOW_SIGNAL_CHARS", 180, 60, 1600);

    public static IReadOnlyList<StructuredChunk> Compact(IReadOnlyList<StructuredChunk> chunks, ChunkPlan plan)
    {
        if (chunks.Count <= 1)
        {
            return chunks;
        }

        var mergeThresholdTokens = Math.Max(MinChunkTokensBeforeMerge, Math.Max(40, plan.TargetChunkTokens / 3));
        var mergeBudgetTokens = Math.Max(plan.TargetChunkTokens, plan.MaxChunkTokens);
        var compacted = new List<StructuredChunk>(chunks.Count);

        foreach (var chunk in chunks)
        {
            if (compacted.Count == 0)
            {
                compacted.Add(ClearLinks(chunk));
                continue;
            }

            var previous = compacted[^1];
            if (CanMerge(previous, chunk, mergeThresholdTokens, mergeBudgetTokens))
            {
                compacted[^1] = Merge(previous, chunk);
                continue;
            }

            compacted.Add(ClearLinks(chunk));
        }

        return ReindexAndRelink(compacted);
    }

    private static bool CanMerge(StructuredChunk previous, StructuredChunk current, int mergeThresholdTokens, int mergeBudgetTokens)
    {
        if (previous.ChunkRole == ChunkRole.Parent || current.ChunkRole == ChunkRole.Parent)
        {
            return false;
        }

        if (previous.ChunkRole != current.ChunkRole)
        {
            return false;
        }

        if (!string.Equals(previous.DocumentId, current.DocumentId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(previous.ParentId, current.ParentId, StringComparison.Ordinal))
        {
            return false;
        }

        var combinedTokenCount = ChunkingHelpers.EstimateTokenCount($"{previous.Text} {current.Text}");
        if (combinedTokenCount > mergeBudgetTokens)
        {
            return false;
        }

        return IsMergeCandidate(previous, mergeThresholdTokens)
            || IsMergeCandidate(current, mergeThresholdTokens);
    }

    private static bool IsMergeCandidate(StructuredChunk chunk, int mergeThresholdTokens)
    {
        var normalized = StructuredParserUtilities.NormalizeWhitespace(chunk.Text);
        var wordCount = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
        var mergeThresholdWords = Math.Max(12, mergeThresholdTokens / 4);
        return wordCount <= mergeThresholdWords
            || LooksLowSignalChunk(normalized);
    }

    private static bool LooksLowSignalChunk(string? text)
    {
        var normalized = StructuredParserUtilities.NormalizeWhitespace(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        if (normalized.Length > MaxLowSignalChunkChars)
        {
            return false;
        }

        var distinctTerms = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => token.Trim('.', ',', ':', ';', '-', '(', ')', '[', ']', '"', '\'').ToLowerInvariant())
            .Where(static token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .Count();

        var words = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => token.Trim('.', ',', ':', ';', '-', '(', ')', '[', ']', '"', '\''))
            .Where(static token => token.Length > 0)
            .ToArray();

        if (words.Length == 0)
        {
            return true;
        }

        var uppercaseWords = words.Count(static word => word.Any(char.IsLetter) && word.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)));
        return (words.Length <= 8 && distinctTerms <= 4)
            || (words.Length <= 6 && uppercaseWords == words.Length);
    }

    private static StructuredChunk Merge(StructuredChunk previous, StructuredChunk current)
    {
        var mergedText = StructuredParserUtilities.NormalizeWhitespace($"{previous.Text}\n\n{current.Text}");
        return previous with
        {
            Text = mergedText,
            SectionPath = ChooseSectionPath(previous.SectionPath, current.SectionPath),
            PageStart = MinPage(previous.PageStart, current.PageStart),
            PageEnd = MaxPage(previous.PageEnd, current.PageEnd),
            ChunkTokenCount = ChunkingHelpers.EstimateTokenCount(mergedText),
            NextChunkId = current.NextChunkId
        };
    }

    private static StructuredChunk ClearLinks(StructuredChunk chunk)
        => chunk with { PrevChunkId = null, NextChunkId = null };

    private static IReadOnlyList<StructuredChunk> ReindexAndRelink(IReadOnlyList<StructuredChunk> chunks)
    {
        var reindexed = chunks
            .Select((chunk, index) => chunk with
            {
                ChunkIndex = index + 1,
                PrevChunkId = null,
                NextChunkId = null,
                ChunkTokenCount = ChunkingHelpers.EstimateTokenCount(chunk.Text)
            })
            .ToList();

        RelinkRole(reindexed, ChunkRole.Standalone);
        RelinkRole(reindexed, ChunkRole.Child);
        return reindexed;
    }

    private static void RelinkRole(List<StructuredChunk> chunks, ChunkRole role)
    {
        var indexes = chunks
            .Select((chunk, index) => new { chunk, index })
            .Where(entry => entry.chunk.ChunkRole == role)
            .Select(static entry => entry.index)
            .ToList();

        for (var position = 0; position < indexes.Count; position++)
        {
            var index = indexes[position];
            var previousId = position == 0 ? null : chunks[indexes[position - 1]].ChunkId;
            var nextId = position == indexes.Count - 1 ? null : chunks[indexes[position + 1]].ChunkId;
            chunks[index] = chunks[index] with { PrevChunkId = previousId, NextChunkId = nextId };
        }
    }

    private static string? ChooseSectionPath(string? previous, string? current)
    {
        if (string.IsNullOrWhiteSpace(previous))
        {
            return string.IsNullOrWhiteSpace(current) ? null : current;
        }

        if (string.IsNullOrWhiteSpace(current) || string.Equals(previous, current, StringComparison.Ordinal))
        {
            return previous;
        }

        if (current.StartsWith(previous + " > ", StringComparison.Ordinal))
        {
            return current;
        }

        if (previous.StartsWith(current + " > ", StringComparison.Ordinal))
        {
            return previous;
        }

        return previous;
    }

    private static int? MinPage(int? left, int? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return Math.Min(left.Value, right.Value);
    }

    private static int? MaxPage(int? left, int? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return Math.Max(left.Value, right.Value);
    }
}

