using System.Text;

namespace Helper.Runtime.WebResearch.Extraction;

internal static class WebSourceTypeExtractionSupport
{
    public static string NormalizeMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "text/html";
        }

        var semicolonIndex = contentType.IndexOf(';');
        var mediaType = semicolonIndex >= 0 ? contentType[..semicolonIndex] : contentType;
        var normalized = mediaType.Trim().ToLowerInvariant();
        return normalized.Length == 0 ? "text/html" : normalized;
    }

    public static string? DecodeIfTextLike(byte[] bytes, string normalizedContentType, string? charset)
    {
        if (LooksLikePdf(normalizedContentType, bytes))
        {
            return null;
        }

        if (!IsTextLikeMediaType(normalizedContentType))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim().Trim('"')).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // Fall back to UTF-8 below.
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    public static bool LooksLikePdf(string normalizedContentType, byte[] contentBytes)
    {
        if (normalizedContentType.Contains("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return contentBytes.Length >= 5 &&
               contentBytes[0] == (byte)'%' &&
               contentBytes[1] == (byte)'P' &&
               contentBytes[2] == (byte)'D' &&
               contentBytes[3] == (byte)'F' &&
               contentBytes[4] == (byte)'-';
    }

    private static bool IsTextLikeMediaType(string normalizedContentType)
    {
        return normalizedContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
               normalizedContentType.Contains("html", StringComparison.OrdinalIgnoreCase) ||
               normalizedContentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) ||
               normalizedContentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
               normalizedContentType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }
}

