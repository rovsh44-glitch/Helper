using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Knowledge;

internal static class StructuredDocumentFormatter
{
    public static string Flatten(DocumentParseResult document)
    {
        var builder = new StringBuilder();
        foreach (var block in document.Blocks.OrderBy(static b => b.ReadingOrder))
        {
            if (string.IsNullOrWhiteSpace(block.Text))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(block.Text.Trim());
        }

        return builder.ToString();
    }

    public static IReadOnlyList<string> SplitForStreaming(DocumentParseResult document)
    {
        var chunks = new List<string>();
        foreach (var page in document.Pages.OrderBy(static p => p.PageNumber))
        {
            var pageText = string.Join(
                Environment.NewLine + Environment.NewLine,
                page.Blocks
                    .OrderBy(static b => b.ReadingOrder)
                    .Select(static b => b.Text?.Trim())
                    .Where(static text => !string.IsNullOrWhiteSpace(text)));

            if (!string.IsNullOrWhiteSpace(pageText))
            {
                chunks.Add(pageText);
            }
        }

        if (chunks.Count == 0)
        {
            var fallback = Flatten(document);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                chunks.Add(fallback);
            }
        }

        return chunks;
    }
}

internal static partial class StructuredParserUtilities
{
    private static readonly int MinCharsForDegradedExtractionHeuristic = ReadBoundedIntEnvironment("HELPER_PARSE_DEGRADED_MIN_CHARS", 120, 32, 20000);
    private static readonly int MinWhitespaceRatioPercentForHealthyExtraction = ReadBoundedIntEnvironment("HELPER_PARSE_DEGRADED_MIN_WHITESPACE_RATIO_PERCENT", 5, 1, 30);
    private static readonly int MaxWordLengthForHealthyExtraction = ReadBoundedIntEnvironment("HELPER_PARSE_DEGRADED_MAX_WORD_LENGTH", 120, 40, 10000);
    private static readonly Regex ExplicitHeadingPrefixRegex = new(
        @"^(chapter|part|section|appendix)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumberedHeadingRegex = new(
        @"^\d+(\.\d+){0,3}\.?\s+[\p{L}""'“‘(\[]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RomanHeadingRegex = new(
        @"^[ivxlcdm]+\.\s+[\p{L}""'“‘(\[]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WatermarkOnlyRegex = new(
        @"^[A-Z0-9._-]+\.(com|net|org|ru|ua|pdf|info)(\.[A-Z]{2})?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] VisionMetaCommentaryAnchors =
    {
        "the image provided is",
        "the provided image is",
        "this image is",
        "this image appears to",
        "this image shows",
        "this image depicts",
        "the image appears to",
        "the image shows",
        "the image depicts",
        "the page appears to",
        "there is no printed text visible",
        "there is no visible printed text",
        "there is no text visible",
        "there is no printed text",
        "there is no text to extract",
        "not a page from an academic book",
        "if you have another image or text",
        "if you have another image",
        "therefore, there is no"
    };

    public static string CreateDocumentId(string sourcePath)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(sourcePath).ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string NormalizeWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sanitized = new string(text
            .Where(static ch => ch == '\r' || ch == '\n' || ch == '\t' || !char.IsControl(ch))
            .ToArray());

        var normalized = sanitized
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\u00A0', ' ');

        var lines = normalized
            .Split('\n')
            .Select(static line => Regex.Replace(line.Trim(), @"\s+", " "))
            .ToArray();

        var compact = string.Join("\n", lines);
        compact = Regex.Replace(compact, @"\n{3,}", "\n\n");
        return compact.Trim();
    }

    public static string SelectBestTextExtraction(params string?[] candidates)
    {
        var normalizedCandidates = candidates
            .Select(NormalizeWhitespace)
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalizedCandidates.Count == 0)
        {
            return string.Empty;
        }

        return normalizedCandidates
            .OrderByDescending(ComputeTextExtractionQualityScore)
            .FirstOrDefault()
            ?? string.Empty;
    }

    public static bool IsBetterTextExtraction(string? candidate, string? baseline)
        => ComputeTextExtractionQualityScore(candidate) > ComputeTextExtractionQualityScore(baseline);

    public static bool LooksLikeDegradedTextExtraction(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (CountMeaningfulChars(text) < MinCharsForDegradedExtractionHeuristic)
        {
            return false;
        }

        return GetWhitespaceRatioPercent(text) < MinWhitespaceRatioPercentForHealthyExtraction
            || GetMaxWordLength(text) > MaxWordLengthForHealthyExtraction;
    }

    public static bool LooksLikeLowValueExtraction(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var compact = Regex.Replace(text.Trim(), @"\s+", string.Empty);
        return WatermarkOnlyRegex.IsMatch(compact);
    }

    public static int CountMeaningfulChars(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Count(static ch => !char.IsWhiteSpace(ch));

    public static double GetWhitespaceRatioPercent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var whitespaceChars = text.Count(char.IsWhiteSpace);
        return whitespaceChars * 100d / Math.Max(1, text.Length);
    }

    public static int GetMaxWordLength(string? text)
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

    public static string? DetectPublishedYear(string title, IEnumerable<DocumentBlock> blocks)
    {
        var titleMatch = YearRegex.Match(title);
        if (titleMatch.Success)
        {
            return titleMatch.Value;
        }

        var sample = string.Join("\n", blocks.Take(10).Select(static b => b.Text));
        var blockMatch = YearRegex.Match(sample);
        return blockMatch.Success ? blockMatch.Value : null;
    }

    public static DocumentBlockType GuessBlockType(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DocumentBlockType.Unknown;
        }

        var trimmed = text.Trim();
        if (trimmed.Length <= 160 && IsHeadingCandidate(trimmed))
        {
            return DocumentBlockType.Heading;
        }

        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            return DocumentBlockType.ListItem;
        }

        if (trimmed.StartsWith("Table ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Figure ", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentBlockType.Caption;
        }

        if (trimmed.Contains('|') && trimmed.Count(static ch => ch == '|') >= 2)
        {
            return DocumentBlockType.Table;
        }

        return DocumentBlockType.Paragraph;
    }

    public static IReadOnlyList<DocumentBlock> SplitPlainTextIntoBlocks(string text, int? pageNumber, ref int readingOrder, string? sectionPath = null)
    {
        var blocks = new List<DocumentBlock>();
        foreach (var segment in Regex.Split(NormalizeWhitespace(text), @"\n\s*\n"))
        {
            var normalized = NormalizeWhitespace(segment);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var blockType = GuessBlockType(normalized);
            blocks.Add(new DocumentBlock(
                BlockId: $"blk-{pageNumber ?? 0}-{++readingOrder}",
                BlockType: blockType,
                Text: normalized,
                ReadingOrder: readingOrder,
                PageNumber: pageNumber,
                SectionPath: sectionPath,
                HeadingLevel: blockType == DocumentBlockType.Heading ? InferHeadingLevel(normalized) : null,
                IsTable: blockType == DocumentBlockType.Table,
                IsList: blockType == DocumentBlockType.ListItem,
                IsCaption: blockType == DocumentBlockType.Caption,
                Attributes: new Dictionary<string, string>()));
        }

        return blocks;
    }

    public static async Task<string> ReadTextWithFallbackAsync(string filePath, CancellationToken ct)
    {
        EncodingBootstrap.EnsureCodePages();
        var bytes = await File.ReadAllBytesAsync(filePath, ct);
        foreach (var encodingName in new[] { "utf-8", "windows-1251", "ibm866" })
        {
            try
            {
                var encoding = Encoding.GetEncoding(encodingName);
                return encoding.GetString(bytes);
            }
            catch
            {
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    public static int ReadBoundedIntEnvironment(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static bool IsProbablyHeading(string text)
    {
        if (IsBareNumericHeading(text))
        {
            return false;
        }

        var letters = text.Count(char.IsLetter);
        if (letters == 0 || text.Length > 160)
        {
            return false;
        }

        var upperLetters = text.Count(char.IsUpper);
        var upperRatio = (double)upperLetters / letters;
        return upperRatio >= 0.55 || text.EndsWith(':');
    }

    private static bool IsHeadingCandidate(string text)
        => ExplicitHeadingPrefixRegex.IsMatch(text)
            || NumberedHeadingRegex.IsMatch(text)
            || RomanHeadingRegex.IsMatch(text)
            || IsProbablyHeading(text);

    private static bool IsBareNumericHeading(string text)
        => Regex.IsMatch(text.Trim(), @"^\d+(\.\d+){0,4}\.?\s*$", RegexOptions.CultureInvariant);

    private static int InferHeadingLevel(string text)
    {
        var match = Regex.Match(text.Trim(), @"^(\d+(\.\d+){0,4})");
        if (!match.Success)
        {
            return 1;
        }

        return match.Value.Count(static ch => ch == '.') + 1;
    }

    private static double ComputeTextExtractionQualityScore(string? text)
    {
        var normalized = NormalizeWhitespace(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return double.MinValue;
        }

        var meaningfulChars = CountMeaningfulChars(normalized);
        var whitespaceRatio = GetWhitespaceRatioPercent(normalized);
        var maxWordLength = GetMaxWordLength(normalized);
        var lineCount = normalized.Count(static ch => ch == '\n') + 1;
        double score = meaningfulChars;

        if (LooksLikeLowValueExtraction(normalized))
        {
            score -= 100_000;
        }

        if (LooksLikeDegradedTextExtraction(normalized))
        {
            score -= Math.Max(250, meaningfulChars / 2d);
        }

        if (whitespaceRatio is >= 8 and <= 26)
        {
            score += meaningfulChars * 0.15;
        }
        else if (whitespaceRatio < 2)
        {
            score -= meaningfulChars * 0.75;
        }
        else if (whitespaceRatio < MinWhitespaceRatioPercentForHealthyExtraction)
        {
            score -= meaningfulChars * 0.35;
        }

        if (maxWordLength > MaxWordLengthForHealthyExtraction)
        {
            score -= (maxWordLength - MaxWordLengthForHealthyExtraction) * 8;
        }

        score += Math.Min(lineCount, 24) * 4;
        return score;
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
}

