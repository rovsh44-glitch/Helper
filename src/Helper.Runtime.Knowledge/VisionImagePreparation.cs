namespace Helper.Runtime.Knowledge;

internal static class VisionImagePreparation
{
    private static readonly int MaxImageBytes = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_VISION_OCR_MAX_IMAGE_BYTES", 12 * 1024 * 1024, 1024, 64 * 1024 * 1024);

    public static bool TryPrepareBase64(byte[] bytes, out string base64)
    {
        base64 = string.Empty;
        if (bytes is not { Length: > 0 } || bytes.Length > MaxImageBytes || !LooksLikeSupportedEncodedImage(bytes))
        {
            return false;
        }

        base64 = Convert.ToBase64String(bytes);
        return !string.IsNullOrWhiteSpace(base64);
    }

    private static bool LooksLikeSupportedEncodedImage(byte[] bytes)
    {
        if (bytes.Length < 12)
        {
            return false;
        }

        var isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8;
        var isPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        var isGif = bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46;
        var isBmp = bytes[0] == 0x42 && bytes[1] == 0x4D;
        var isWebp = bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;

        return isJpeg || isPng || isGif || isBmp || isWebp;
    }
}

