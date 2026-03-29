using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Knowledge;

internal static partial class StructuredParserUtilities
{
    public static string NormalizeVisionResponse(string? text)
    {
        var normalized = NormalizeWhitespace(text);
        if (LooksLikeLowValueExtraction(normalized) || LooksLikeVisionRefusal(normalized))
        {
            return string.Empty;
        }

        normalized = StripVisionMetaCommentary(normalized);
        return LooksLikeLowValueExtraction(normalized) || LooksLikeVisionRefusal(normalized) ? string.Empty : normalized;
    }

    public static async Task<string> ExtractVisionTextAsync(
        AILink ai,
        string prompt,
        string model,
        string base64Image,
        CancellationToken ct,
        int timeoutSeconds,
        int keepAliveSeconds = 120)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(timeoutSeconds, 1)));

        try
        {
            var response = await ai.AskAsync(
                prompt,
                timeoutCts.Token,
                overrideModel: model,
                base64Image: base64Image,
                keepAliveSeconds: Math.Max(keepAliveSeconds, timeoutSeconds));
            return NormalizeVisionResponse(response);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"vision_ocr_timeout_{timeoutSeconds}s");
        }
    }

    private static bool LooksLikeVisionRefusal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().ToLowerInvariant();
        return normalized.StartsWith("i'm unable to directly extract text", StringComparison.Ordinal)
            || normalized.StartsWith("i am unable to directly extract text", StringComparison.Ordinal)
            || normalized.StartsWith("i can't directly extract text", StringComparison.Ordinal)
            || normalized.StartsWith("i cannot directly extract text", StringComparison.Ordinal)
            || normalized.Contains("if you provide the text content", StringComparison.Ordinal)
            || normalized.Contains("describe the content of the pdf page", StringComparison.Ordinal);
    }

    private static string StripVisionMetaCommentary(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleanedLines = new List<string>();
        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var commentaryIndex = FindVisionMetaCommentaryStart(line);
            if (commentaryIndex == 0)
            {
                continue;
            }

            if (commentaryIndex > 0)
            {
                line = line[..commentaryIndex].TrimEnd(' ', '.', ';', ':', '-', '—');
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                cleanedLines.Add(line);
            }
        }

        return NormalizeWhitespace(string.Join(Environment.NewLine, cleanedLines));
    }

    private static int FindVisionMetaCommentaryStart(string line)
    {
        var lowered = line.ToLowerInvariant();
        var bestIndex = -1;

        foreach (var anchor in VisionMetaCommentaryAnchors)
        {
            var searchIndex = 0;
            while (searchIndex < lowered.Length)
            {
                var index = lowered.IndexOf(anchor, searchIndex, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                if (index == 0 || IsLikelyVisionMetaBoundary(lowered, index))
                {
                    if (bestIndex < 0 || index < bestIndex)
                    {
                        bestIndex = index;
                    }

                    break;
                }

                searchIndex = index + anchor.Length;
            }
        }

        return bestIndex;
    }

    private static bool IsLikelyVisionMetaBoundary(string text, int index)
    {
        if (index <= 0)
        {
            return true;
        }

        var prev = text[index - 1];
        if (prev is '.' or '!' or '?' or ':' or ';' or '\n')
        {
            return true;
        }

        return index >= 2 && text[index - 1] == ' ' && (text[index - 2] is '.' or '!' or '?' or ':' or ';');
    }
}

