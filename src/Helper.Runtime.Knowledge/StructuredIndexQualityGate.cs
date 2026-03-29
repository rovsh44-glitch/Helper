using Helper.Runtime.Core;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;

namespace Helper.Runtime.Knowledge;

internal static class StructuredIndexQualityGate
{
    private static readonly int SmallFileMinChars = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_TEXT_CHARS_SMALL", 400, 100, 20000);
    private static readonly int MediumFileMinChars = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_TEXT_CHARS_MEDIUM", 1500, 200, 50000);
    private static readonly int LargeFileMinChars = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_TEXT_CHARS_LARGE", 4000, 500, 100000);
    private static readonly int HugeFileMinChars = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_TEXT_CHARS_HUGE", 8000, 1000, 200000);
    private static readonly int SmallFileMinChunks = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_CHUNKS_SMALL", 1, 1, 200);
    private static readonly int MediumFileMinChunks = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_CHUNKS_MEDIUM", 3, 1, 400);
    private static readonly int LargeFileMinChunks = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_CHUNKS_LARGE", 6, 1, 800);
    private static readonly int HugeFileMinChunks = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_CHUNKS_HUGE", 12, 1, 1600);
    private static readonly int MinCharsPerPage = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_PAGE_TEXT_CHARS", 32, 8, 2000);
    private static readonly int MinChunkTokens = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_CHUNK_TOKENS", 20, 5, 200);
    private static readonly int MaxTinyChunkPercent = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MAX_TINY_CHUNK_PERCENT", 8, 1, 50);
    private static readonly int MaxLowSignalChunkPercent = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MAX_LOW_SIGNAL_CHUNK_PERCENT", 6, 1, 50);
    private static readonly int MaxLowSignalScanChunks = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MAX_LOW_SIGNAL_SCAN_CHUNKS", 768, 64, 10000);
    private static readonly int MinWhitespaceRatioPercent = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_WHITESPACE_RATIO_PERCENT", 5, 1, 30);
    private static readonly int MinCharsForWhitespaceHeuristic = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MIN_SPACE_POOR_CHARS", 120, 32, 10000);
    private static readonly int MaxSpacePoorChunkPercent = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MAX_SPACE_POOR_CHUNK_PERCENT", 20, 1, 80);
    private static readonly int MaxWordLength = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MAX_WORD_LENGTH", 80, 20, 10000);
    private static readonly int MaxLongWordChunkPercent = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MAX_LONG_WORD_CHUNK_PERCENT", 12, 1, 80);

    public static void EnsureAccepted(
        string filePath,
        DocumentParseResult document,
        IReadOnlyList<StructuredChunk> chunks,
        string flatContent,
        bool wasTruncated = false,
        int? totalChunksBeforeCap = null)
    {
        var reasons = new List<string>();
        var fileSizeBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 0L;
        var fileSizeMb = fileSizeBytes / (1024d * 1024d);
        var textChars = CountMeaningfulChars(flatContent);
        var nonEmptyBlocks = document.Blocks.Count(block => CountMeaningfulChars(block.Text) >= MinCharsPerPage);
        var nonEmptyPages = document.Pages.Count(page => CountMeaningfulChars(page.RawText) >= MinCharsPerPage);
        var minChars = ResolveMinChars(fileSizeMb);
        var minChunks = ResolveMinChunks(fileSizeMb);

        if (document.Blocks.Count == 0)
        {
            reasons.Add("no blocks extracted");
        }

        if (nonEmptyBlocks == 0)
        {
            reasons.Add("no non-empty blocks");
        }

        if (textChars < minChars)
        {
            reasons.Add($"text_chars={textChars} < min_expected={minChars}");
        }

        if (chunks.Count < minChunks)
        {
            reasons.Add($"chunk_count={chunks.Count} < min_expected={minChunks}");
        }

        if (wasTruncated)
        {
            reasons.Add($"chunk_cap_reached={chunks.Count} of {Math.Max(chunks.Count, totalChunksBeforeCap ?? chunks.Count)}");
        }

        if (chunks.Count >= 20)
        {
            var tinyChunks = chunks.Count(chunk => chunk.ChunkTokenCount < MinChunkTokens);
            var lowSignalChunks = CountLowSignalChunks(chunks);
            var spacePoorChunks = CountSpacePoorChunks(chunks);
            var (longWordChunks, observedMaxWordLength) = CountLongWordChunks(chunks);
            var maxTinyChunks = Math.Max(12, (int)Math.Ceiling(chunks.Count * (MaxTinyChunkPercent / 100d)));
            var maxLowSignalChunks = Math.Max(8, (int)Math.Ceiling(chunks.Count * (MaxLowSignalChunkPercent / 100d)));
            var maxSpacePoorChunks = Math.Max(8, (int)Math.Ceiling(chunks.Count * (MaxSpacePoorChunkPercent / 100d)));
            var maxLongWordChunks = Math.Max(6, (int)Math.Ceiling(chunks.Count * (MaxLongWordChunkPercent / 100d)));
            if (tinyChunks > maxTinyChunks)
            {
                reasons.Add($"tiny_chunks={tinyChunks} > limit={maxTinyChunks}");
            }

            if (lowSignalChunks > maxLowSignalChunks)
            {
                reasons.Add($"low_signal_chunks={lowSignalChunks} > limit={maxLowSignalChunks}");
            }

            if (spacePoorChunks > maxSpacePoorChunks)
            {
                reasons.Add($"space_poor_chunks={spacePoorChunks} > limit={maxSpacePoorChunks}");
            }

            if (longWordChunks > maxLongWordChunks)
            {
                reasons.Add($"max_word_len={observedMaxWordLength} > limit={MaxWordLength} in {longWordChunks} chunks");
            }
        }

        var fatalWarnings = (document.Warnings ?? Array.Empty<string>())
            .Where(IsFatalWarning)
            .Take(5)
            .ToArray();
        if (fatalWarnings.Length > 0)
        {
            reasons.Add($"fatal_warnings={string.Join(", ", fatalWarnings)}");
        }

        if (IsPagedFormat(document.Format) && document.Pages.Count > 0)
        {
            var minNonEmptyPages = ResolveMinNonEmptyPages(document.Pages.Count, fileSizeMb);
            if (nonEmptyPages < minNonEmptyPages)
            {
                reasons.Add($"non_empty_pages={nonEmptyPages} < min_expected={minNonEmptyPages}");
            }

            var severePageWarnings = (document.Warnings ?? Array.Empty<string>()).Count(IsSeverePageWarning);
            var severeWarningLimit = Math.Max(3, document.Pages.Count / 2);
            if (severePageWarnings > severeWarningLimit && textChars < minChars * 3)
            {
                reasons.Add($"severe_page_warnings={severePageWarnings} > limit={severeWarningLimit}");
            }
        }

        if (reasons.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Parse quality gate rejected {Path.GetFileName(filePath)}: {string.Join("; ", reasons)}");
    }

    private static bool IsPagedFormat(DocumentFormatType format)
        => format is DocumentFormatType.Pdf or DocumentFormatType.Djvu;

    private static int ResolveMinChars(double fileSizeMb)
    {
        if (fileSizeMb >= 64)
        {
            return HugeFileMinChars;
        }

        if (fileSizeMb >= 16)
        {
            return LargeFileMinChars;
        }

        if (fileSizeMb >= 4)
        {
            return MediumFileMinChars;
        }

        return SmallFileMinChars;
    }

    private static int ResolveMinChunks(double fileSizeMb)
    {
        if (fileSizeMb >= 64)
        {
            return HugeFileMinChunks;
        }

        if (fileSizeMb >= 16)
        {
            return LargeFileMinChunks;
        }

        if (fileSizeMb >= 4)
        {
            return MediumFileMinChunks;
        }

        return SmallFileMinChunks;
    }

    private static int ResolveMinNonEmptyPages(int totalPages, double fileSizeMb)
    {
        if (totalPages <= 3)
        {
            return 1;
        }

        var ratio = fileSizeMb >= 16 ? 0.25 : 0.20;
        return Math.Max(1, (int)Math.Ceiling(totalPages * ratio));
    }

    private static int CountMeaningfulChars(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Count(static ch => !char.IsWhiteSpace(ch));

    private static bool IsFatalWarning(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return false;
        }

        return warning.StartsWith("pdfpig_failed:", StringComparison.OrdinalIgnoreCase)
            || warning.StartsWith("djvu_open_failed:", StringComparison.OrdinalIgnoreCase)
            || warning.StartsWith("chm_extractor_missing:", StringComparison.OrdinalIgnoreCase)
            || warning.StartsWith("chm_extract_failed:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSeverePageWarning(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return false;
        }

        return warning.Contains("_failed:", StringComparison.OrdinalIgnoreCase)
            || warning.Contains("fallback_skipped", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLowSignalChunk(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var normalized = text.Trim();
        if (normalized.Length == 0)
        {
            return true;
        }

        if (LooksNumericOnly(normalized))
        {
            return true;
        }

        var totalTokens = 0;
        var uppercaseWords = 0;
        var distinctTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in EnumerateWordTokens(normalized))
        {
            totalTokens++;
            if (distinctTokens.Count <= 4)
            {
                distinctTokens.Add(token.ToLowerInvariant());
            }

            if (TokenIsUppercaseWord(token))
            {
                uppercaseWords++;
            }

            if (totalTokens > 8 && distinctTokens.Count > 4)
            {
                return false;
            }
        }

        if (totalTokens == 0)
        {
            return true;
        }

        return (totalTokens <= 8 && distinctTokens.Count <= 4)
            || (totalTokens <= 6 && uppercaseWords == totalTokens);
    }

    private static int CountLowSignalChunks(IReadOnlyList<StructuredChunk> chunks)
    {
        if (chunks.Count <= MaxLowSignalScanChunks)
        {
            return chunks.Count(chunk => LooksLowSignalChunk(chunk.Text));
        }

        var sample = SampleChunks(chunks, MaxLowSignalScanChunks);
        if (sample.Count == 0)
        {
            return 0;
        }

        var lowSignalInSample = sample.Count(chunk => LooksLowSignalChunk(chunk.Text));
        return (int)Math.Ceiling(lowSignalInSample * (chunks.Count / (double)sample.Count));
    }

    private static int CountSpacePoorChunks(IReadOnlyList<StructuredChunk> chunks)
        => chunks.Count(chunk => LooksSpacePoorChunk(chunk.Text));

    private static (int LongWordChunks, int ObservedMaxWordLength) CountLongWordChunks(IReadOnlyList<StructuredChunk> chunks)
    {
        var longWordChunks = 0;
        var observedMaxWordLength = 0;
        foreach (var chunk in chunks)
        {
            var maxWordLength = GetMaxWordLength(chunk.Text);
            if (maxWordLength > observedMaxWordLength)
            {
                observedMaxWordLength = maxWordLength;
            }

            if (maxWordLength > MaxWordLength)
            {
                longWordChunks++;
            }
        }

        return (longWordChunks, observedMaxWordLength);
    }

    private static List<StructuredChunk> SampleChunks(IReadOnlyList<StructuredChunk> chunks, int maxSampleSize)
    {
        if (chunks.Count <= maxSampleSize)
        {
            return chunks.ToList();
        }

        var sample = new List<StructuredChunk>(maxSampleSize);
        var step = chunks.Count / (double)maxSampleSize;
        for (var index = 0; index < maxSampleSize; index++)
        {
            var sourceIndex = Math.Min(chunks.Count - 1, (int)Math.Floor(index * step));
            sample.Add(chunks[sourceIndex]);
        }

        return sample;
    }

    private static bool LooksNumericOnly(string text)
    {
        var hasDigit = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || ch is '.' or ',' or ';' or ':' or '-' or '(' or ')' or '[' or ']')
            {
                continue;
            }

            if (!char.IsDigit(ch))
            {
                return false;
            }

            hasDigit = true;
        }

        return hasDigit;
    }

    private static bool LooksSpacePoorChunk(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (CountMeaningfulChars(text) < MinCharsForWhitespaceHeuristic)
        {
            return false;
        }

        var whitespaceChars = text.Count(char.IsWhiteSpace);
        var whitespaceRatioPercent = whitespaceChars * 100d / Math.Max(1, text.Length);
        return whitespaceRatioPercent < MinWhitespaceRatioPercent;
    }

    private static IEnumerable<string> EnumerateWordTokens(string text)
    {
        var start = -1;
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsLetterOrDigit(text[index]))
            {
                start = start < 0 ? index : start;
                continue;
            }

            if (start >= 0)
            {
                yield return text[start..index];
                start = -1;
            }
        }

        if (start >= 0)
        {
            yield return text[start..];
        }
    }

    private static bool TokenIsUppercaseWord(string token)
    {
        var hasLetter = false;
        foreach (var ch in token)
        {
            if (!char.IsLetter(ch))
            {
                continue;
            }

            hasLetter = true;
            if (!char.IsUpper(ch))
            {
                return false;
            }
        }

        return hasLetter;
    }

    private static int GetMaxWordLength(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var maxWordLength = 0;
        foreach (var token in EnumerateWordTokens(text))
        {
            if (token.Length > maxWordLength)
            {
                maxWordLength = token.Length;
            }
        }

        return maxWordLength;
    }
}

