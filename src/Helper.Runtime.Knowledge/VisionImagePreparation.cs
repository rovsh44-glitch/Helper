using ImageMagick;

namespace Helper.Runtime.Knowledge;

internal static class VisionImagePreparation
{
    private static readonly int MaxDimension = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_VISION_OCR_MAX_DIM", 1600, 512, 4096);
    private static readonly int JpegQuality = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_VISION_OCR_JPEG_QUALITY", 75, 40, 95);

    public static bool TryPrepareBase64(byte[] bytes, out string base64)
    {
        base64 = string.Empty;
        if (bytes is not { Length: > 0 })
        {
            return false;
        }

        try
        {
            using var image = new MagickImage(bytes);
            base64 = PrepareBase64(image);
            return !string.IsNullOrWhiteSpace(base64);
        }
        catch
        {
            return false;
        }
    }

    public static string PrepareBase64(MagickImage image)
    {
        using var clone = (MagickImage)image.Clone();
        Normalize(clone);
        return clone.ToBase64();
    }

    private static void Normalize(MagickImage image)
    {
        image.AutoOrient();
        image.Strip();

        var width = Math.Max(image.Width, 1);
        var height = Math.Max(image.Height, 1);
        var longestSide = Math.Max(width, height);
        if (longestSide > MaxDimension)
        {
            var scale = (double)MaxDimension / longestSide;
            var targetWidth = (uint)Math.Max((int)Math.Round(width * scale), 1);
            var targetHeight = (uint)Math.Max((int)Math.Round(height * scale), 1);
            image.Resize(targetWidth, targetHeight);
        }

        image.Format = MagickFormat.Jpg;
        image.Quality = (uint)JpegQuality;
    }
}

