using System.Diagnostics;
using Helper.Runtime.Infrastructure;
using ImageMagick;
using UglyToad.PdfPig.Content;

namespace Helper.Runtime.Knowledge;

public sealed partial class StructuredPdfParser
{
    private const string DefaultVisionModel = "qwen2.5vl:7b";
    private const string VisionPrompt = "Extract only the visible printed text from this academic book page. Ignore watermarks, website overlays, logos, stamps, marginal noise, and repeated headers or footers. Preserve headings, lists, tables, formulas, figure captions, map labels, legends, numeric scales, and embedded annotations when visible. Do not describe the image, do not explain what it depicts, and do not add commentary. If there is no readable printed text at all, return an empty string.";
    private static readonly Lazy<string?> GhostscriptExecutablePath = new(ResolveGhostscriptExecutablePath, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<bool> VisionFallbackAvailable = new(() => !string.IsNullOrWhiteSpace(GhostscriptExecutablePath.Value), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly int VisionTimeoutSeconds = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_VISION_OCR_TIMEOUT_SEC", 90, 10, 600);
    private static readonly int GhostscriptRasterDpi = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_PDF_VISION_GHOSTSCRIPT_DPI", 200, 96, 600);

    private static int ResolveVisionOnlyPageCount(string filePath)
        => Math.Max(MagickImageInfo.ReadCollection(filePath).Count(), 1);

    private async Task<string> ExtractWithGhostscriptVisionAsync(string filePath, int pageNumber, CancellationToken ct)
    {
        if (!CanUseGhostscriptVisionFallback())
        {
            return string.Empty;
        }

        var ghostscriptPath = GhostscriptExecutablePath.Value;
        if (string.IsNullOrWhiteSpace(ghostscriptPath) || !File.Exists(ghostscriptPath))
        {
            return string.Empty;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "helper-gs");
        Directory.CreateDirectory(tempDir);
        var outputPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}_page_{pageNumber}.png");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ghostscriptPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-dSAFER");
            startInfo.ArgumentList.Add("-dBATCH");
            startInfo.ArgumentList.Add("-dNOPAUSE");
            startInfo.ArgumentList.Add("-dQUIET");
            startInfo.ArgumentList.Add("-sDEVICE=pnggray");
            startInfo.ArgumentList.Add($"-r{GhostscriptRasterDpi}");
            startInfo.ArgumentList.Add($"-dFirstPage={Math.Max(pageNumber, 1)}");
            startInfo.ArgumentList.Add($"-dLastPage={Math.Max(pageNumber, 1)}");
            startInfo.ArgumentList.Add($"-sOutputFile={outputPath}");
            startInfo.ArgumentList.Add(filePath);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Ghostscript process did not start.");
            await process.WaitForExitAsync(ct);

            var stderr = await process.StandardError.ReadToEndAsync(ct);
            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(stderr) ? $"ghostscript_exit_code_{process.ExitCode}" : stderr.Trim();
                throw new InvalidOperationException(message);
            }

            if (!File.Exists(outputPath))
            {
                return string.Empty;
            }

            var bytes = await File.ReadAllBytesAsync(outputPath, ct);
            if (!VisionImagePreparation.TryPrepareBase64(bytes, out var prepared) || string.IsNullOrWhiteSpace(prepared))
            {
                return string.Empty;
            }

            return await StructuredParserUtilities.ExtractVisionTextAsync(
                _ai!,
                VisionPrompt,
                ResolveVisionModel(),
                prepared,
                ct,
                VisionTimeoutSeconds);
        }
        finally
        {
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            catch
            {
            }
        }
    }

    private async Task<string> ExtractWithEmbeddedImageVisionAsync(Page page, CancellationToken ct)
    {
        if (!CanUseAiVision())
        {
            return string.Empty;
        }

        foreach (var image in page.GetImages().OrderByDescending(static image => (long)image.WidthInSamples * image.HeightInSamples))
        {
            if (!TryBuildEmbeddedImageBase64(image, out var base64))
            {
                continue;
            }

            var extracted = await StructuredParserUtilities.ExtractVisionTextAsync(
                _ai!,
                VisionPrompt,
                ResolveVisionModel(),
                base64,
                ct,
                VisionTimeoutSeconds);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted;
            }
        }

        return string.Empty;
    }

    private static bool TryBuildEmbeddedImageBase64(dynamic image, out string base64)
    {
        base64 = string.Empty;

        try
        {
            byte[]? pngBytes = null;
            if (image.TryGetPng(out pngBytes) && pngBytes is { Length: > 0 })
            {
                if (VisionImagePreparation.TryPrepareBase64(pngBytes!, out base64))
                {
                    return true;
                }

                base64 = Convert.ToBase64String(pngBytes!);
                return true;
            }
        }
        catch
        {
        }

        try
        {
            var rawBytes = ((IEnumerable<byte>?)image.RawBytes)?.ToArray();
            IReadOnlyList<byte>? decodedBytes = null;
            if (rawBytes is not { Length: > 0 } && image.TryGetBytes(out decodedBytes) && decodedBytes is not null)
            {
                rawBytes = decodedBytes!.ToArray();
            }

            if (rawBytes is not { Length: > 0 })
            {
                return false;
            }

            if (LooksLikeSupportedEncodedImage(rawBytes))
            {
                if (VisionImagePreparation.TryPrepareBase64(rawBytes, out base64))
                {
                    return true;
                }

                base64 = Convert.ToBase64String(rawBytes);
                return true;
            }

            return VisionImagePreparation.TryPrepareBase64(rawBytes, out base64);
        }
        catch
        {
            return false;
        }
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

    private static string? ResolveGhostscriptExecutablePath()
    {
        try
        {
            var configured = Environment.GetEnvironmentVariable("HELPER_PDF_VISION_GHOSTSCRIPT_PATH");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                ConfigureGhostscriptRuntime(configured);
                return configured;
            }

            var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var entry in pathEntries)
            {
                var candidate = Path.Combine(entry, "gswin64c.exe");
                if (File.Exists(candidate))
                {
                    ConfigureGhostscriptRuntime(candidate);
                    return candidate;
                }
            }

            foreach (var candidate in EnumerateGhostscriptCandidates())
            {
                if (File.Exists(candidate))
                {
                    ConfigureGhostscriptRuntime(candidate);
                    return candidate;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static IEnumerable<string> EnumerateGhostscriptCandidates()
    {
        foreach (var candidate in EnumerateBootstrapManagedGhostscriptCandidates()) { yield return candidate; }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 }.Where(static path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)))
        {
            var gsRoot = Path.Combine(root, "gs");
            if (!Directory.Exists(gsRoot))
            {
                continue;
            }

            foreach (var candidate in Directory.EnumerateDirectories(gsRoot, "gs*", SearchOption.TopDirectoryOnly)
                         .Select(static directory => Path.Combine(directory, "bin", "gswin64c.exe"))
                         .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateBootstrapManagedGhostscriptCandidates()
    {
        string helperRoot;
        try
        {
            helperRoot = HelperWorkspacePathResolver.ResolveHelperRoot();
        }
        catch
        {
            yield break;
        }

        var candidates = new[]
        {
            HelperWorkspacePathResolver.ResolveWorkspaceFile(Path.Combine("tools", "ghostscript", "gs10051", "bin", "gswin64c.exe"), helperRoot),
            HelperWorkspacePathResolver.ResolveDataFilePath(Path.Combine("runtime", "ghostscript", "gs10051", "bin", "gswin64c.exe"), helperRoot: helperRoot)
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return candidate;
        }
    }

    private static void ConfigureGhostscriptRuntime(string executablePath)
    {
        var ghostscriptDir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(ghostscriptDir) || !Directory.Exists(ghostscriptDir))
        {
            return;
        }

        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathEntries = currentPath
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!pathEntries.Contains(ghostscriptDir, StringComparer.OrdinalIgnoreCase))
        {
            var updatedPath = string.IsNullOrWhiteSpace(currentPath)
                ? ghostscriptDir
                : string.Concat(ghostscriptDir, Path.PathSeparator, currentPath);
            Environment.SetEnvironmentVariable("PATH", updatedPath);

            try
            {
                MagickNET.SetEnvironmentVariable("PATH", updatedPath);
            }
            catch
            {
            }
        }

        try
        {
            MagickNET.SetGhostscriptDirectory(ghostscriptDir);
        }
        catch
        {
        }
    }
}

