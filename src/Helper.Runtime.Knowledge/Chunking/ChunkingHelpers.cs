using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Chunking;

internal static class ChunkingHelpers
{
    public static int EstimateTokenCount(string text)
        => string.IsNullOrWhiteSpace(text) ? 0 : Math.Max(1, (int)Math.Ceiling(text.Length / 4d));

    public static IReadOnlyList<string> SplitRecursively(string text, int maxTokens)
    {
        var normalized = StructuredParserUtilities.NormalizeWhitespace(text);
        if (EstimateTokenCount(normalized) <= maxTokens)
        {
            return new[] { normalized };
        }

        foreach (var separator in new[] { "\n\n", "\n", ". ", "; ", ", " })
        {
            var parts = normalized.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length <= 1)
            {
                continue;
            }

            var chunks = MergeWithBudget(parts, separator, maxTokens);
            if (chunks.Count > 1)
            {
                return chunks;
            }
        }

        return SplitHard(normalized, maxTokens);
    }

    public static string ComputeOverlap(string text, int overlapTokens)
    {
        if (string.IsNullOrWhiteSpace(text) || overlapTokens <= 0)
        {
            return string.Empty;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length <= overlapTokens)
        {
            return text;
        }

        return string.Join(' ', words.Skip(Math.Max(0, words.Length - overlapTokens)));
    }

    public static StructuredChunk CreateChunk(
        string documentId,
        string text,
        int chunkIndex,
        ChunkRole role,
        string? sectionPath,
        int? pageStart,
        int? pageEnd,
        string? parentId = null,
        Dictionary<string, string>? metadata = null)
    {
        return new StructuredChunk(
            ChunkId: Guid.NewGuid().ToString(),
            DocumentId: documentId,
            Text: text,
            ChunkIndex: chunkIndex,
            ChunkRole: role,
            SectionPath: sectionPath,
            PageStart: pageStart,
            PageEnd: pageEnd,
            ParentId: parentId,
            ChunkTokenCount: EstimateTokenCount(text),
            Metadata: metadata ?? new Dictionary<string, string>());
    }

    public static IReadOnlyList<(string? Key, List<DocumentBlock> Blocks)> CoalesceSmallGroups(
        IEnumerable<IGrouping<string, DocumentBlock>> groups,
        int maxChunkTokens)
    {
        var orderedGroups = groups
            .Select(group => (Key: (string?)group.Key, Blocks: group.OrderBy(static block => block.ReadingOrder).ToList()))
            .Where(static entry => entry.Blocks.Count > 0)
            .ToList();

        if (orderedGroups.Count <= 1)
        {
            return orderedGroups;
        }

        var result = new List<(string? Key, List<DocumentBlock> Blocks)>(orderedGroups.Count);
        var pendingBlocks = new List<DocumentBlock>();
        string? pendingKey = null;
        var smallSectionTokenThreshold = Math.Max(20, Math.Min(40, maxChunkTokens / 8));

        foreach (var group in orderedGroups)
        {
            var currentText = string.Join(
                Environment.NewLine + Environment.NewLine,
                group.Blocks.Select(static block => block.Text).Where(static text => !string.IsNullOrWhiteSpace(text)));
            var currentTokens = EstimateTokenCount(currentText);
            var isSmallGroup = currentTokens <= smallSectionTokenThreshold;

            if (isSmallGroup)
            {
                if (result.Count > 0)
                {
                    var previous = result[^1];
                    var previousText = string.Join(
                        Environment.NewLine + Environment.NewLine,
                        previous.Blocks.Select(static block => block.Text).Where(static text => !string.IsNullOrWhiteSpace(text)));
                    if (EstimateTokenCount(previousText) + currentTokens <= maxChunkTokens)
                    {
                        previous.Blocks.AddRange(group.Blocks);
                        result[^1] = previous;
                        continue;
                    }
                }

                if (pendingBlocks.Count == 0)
                {
                    pendingKey = group.Key;
                }

                pendingBlocks.AddRange(group.Blocks);
                continue;
            }

            if (pendingBlocks.Count > 0)
            {
                var pendingText = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    pendingBlocks.Select(static block => block.Text).Where(static text => !string.IsNullOrWhiteSpace(text)));
                if (EstimateTokenCount(pendingText) + currentTokens <= maxChunkTokens)
                {
                    var mergedBlocks = new List<DocumentBlock>(pendingBlocks.Count + group.Blocks.Count);
                    mergedBlocks.AddRange(pendingBlocks);
                    mergedBlocks.AddRange(group.Blocks);
                    result.Add((group.Key ?? pendingKey, mergedBlocks));
                    pendingBlocks.Clear();
                    pendingKey = null;
                    continue;
                }

                result.Add((pendingKey, pendingBlocks.ToList()));
                pendingBlocks.Clear();
                pendingKey = null;
            }

            result.Add((group.Key, group.Blocks.ToList()));
        }

        if (pendingBlocks.Count > 0)
        {
            if (result.Count > 0)
            {
                var previous = result[^1];
                var previousText = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    previous.Blocks.Select(static block => block.Text).Where(static text => !string.IsNullOrWhiteSpace(text)));
                var pendingText = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    pendingBlocks.Select(static block => block.Text).Where(static text => !string.IsNullOrWhiteSpace(text)));
                if (EstimateTokenCount(previousText) + EstimateTokenCount(pendingText) <= maxChunkTokens)
                {
                    previous.Blocks.AddRange(pendingBlocks);
                    result[^1] = previous;
                }
                else
                {
                    result.Add((pendingKey, pendingBlocks.ToList()));
                }
            }
            else
            {
                result.Add((pendingKey, pendingBlocks.ToList()));
            }
        }

        return result;
    }

    private static IReadOnlyList<string> MergeWithBudget(IEnumerable<string> parts, string separator, int maxTokens)
    {
        var result = new List<string>();
        var buffer = new List<string>();
        var currentTokens = 0;

        foreach (var part in parts.Select(StructuredParserUtilities.NormalizeWhitespace).Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            var partTokens = EstimateTokenCount(part);
            if (buffer.Count > 0 && currentTokens + partTokens > maxTokens)
            {
                result.Add(string.Join(separator, buffer));
                buffer.Clear();
                currentTokens = 0;
            }

            if (partTokens > maxTokens)
            {
                result.AddRange(SplitHard(part, maxTokens));
                continue;
            }

            buffer.Add(part);
            currentTokens += partTokens;
        }

        if (buffer.Count > 0)
        {
            result.Add(string.Join(separator, buffer));
        }

        return result;
    }

    private static IReadOnlyList<string> SplitHard(string text, int maxTokens)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var maxWords = Math.Max(40, maxTokens * 3);
        var chunks = new List<string>();
        for (var index = 0; index < words.Length; index += maxWords)
        {
            chunks.Add(string.Join(' ', words.Skip(index).Take(maxWords)));
        }

        return chunks;
    }
}

