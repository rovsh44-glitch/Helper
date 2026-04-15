using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;

namespace Helper.Runtime.Knowledge;

public sealed class StructuredDjvuParser : StructuredDocumentParserBase
{
    public StructuredDjvuParser(AILink ai) { }

    public override string ParserVersion => "djvu_v2";
    public override bool CanParse(string extension) => string.Equals(extension, ".djvu", StringComparison.OrdinalIgnoreCase);

    public override async Task<DocumentParseResult> ParseStructuredAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default)
    {
        if (onProgress is not null)
        {
            await onProgress(100d);
        }

        return StructuredParserUtilities.BuildDocument(
            filePath,
            DocumentFormatType.Djvu,
            ParserVersion,
            Path.GetFileName(filePath),
            Array.Empty<DocumentPage>(),
            Array.Empty<DocumentBlock>(),
            new[] { "djvu_ocr_unavailable:Magick.NET rasterizer removed from runtime security boundary" });
    }
}

public sealed class StructuredChmParser : StructuredDocumentParserBase
{
    public override string ParserVersion => "chm_v2";
    public override bool CanParse(string extension) => string.Equals(extension, ".chm", StringComparison.OrdinalIgnoreCase);

    public override async Task<DocumentParseResult> ParseStructuredAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default)
    {
        EncodingBootstrap.EnsureCodePages();
        var tempDir = Path.Combine(Path.GetTempPath(), "helper_chm_extract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var pages = new List<DocumentPage>();
        var blocks = new List<DocumentBlock>();
        var warnings = new List<string>();
        var readingOrder = 0;
        var syntheticPage = 0;

        try
        {
            var extractorPath = ResolveSevenZipPath();
            if (extractorPath is null)
            {
                warnings.Add("chm_extractor_missing:7z.exe not found");
                return StructuredParserUtilities.BuildDocument(filePath, DocumentFormatType.Chm, ParserVersion, Path.GetFileName(filePath), pages, blocks, warnings);
            }

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = extractorPath,
                    Arguments = $@"x ""{filePath}"" -o""{tempDir}"" -y",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };

            process.Start();
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                warnings.Add($"chm_extract_failed:exit_code_{process.ExitCode}");
            }

            var contentFiles = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories)
                .Where(static path => path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var file in contentFiles)
            {
                ct.ThrowIfCancellationRequested();
                syntheticPage++;
                var text = await StructuredParserUtilities.ReadTextWithFallbackAsync(file, ct);
                IReadOnlyList<DocumentBlock> pageBlocks = file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                    ? StructuredParserUtilities.SplitPlainTextIntoBlocks(text, syntheticPage, ref readingOrder)
                    : StructuredParserUtilities.ExtractHtmlBlocks(text, syntheticPage, ref readingOrder, Path.GetFileName(file));

                var rawText = pageBlocks.Count > 0
                    ? string.Join(Environment.NewLine + Environment.NewLine, pageBlocks.Select(static block => block.Text))
                    : StructuredParserUtilities.NormalizeWhitespace(text);

                pages.Add(new DocumentPage(syntheticPage, rawText, pageBlocks, Path.GetFileName(file)));
                blocks.AddRange(pageBlocks);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                }
            }
        }

        return StructuredParserUtilities.BuildDocument(
            filePath,
            DocumentFormatType.Chm,
            ParserVersion,
            Path.GetFileName(filePath),
            pages,
            blocks,
            warnings);
    }

    private static string? ResolveSevenZipPath()
    {
        var candidates = new[]
        {
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe"
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}

public sealed class StructuredZimParser : StructuredDocumentParserBase
{
    public override string ParserVersion => "zim_v2";
    public override bool CanParse(string extension) => string.Equals(extension, ".zim", StringComparison.OrdinalIgnoreCase);

    public override Task<DocumentParseResult> ParseStructuredAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default)
    {
        var blocks = new[]
        {
            new DocumentBlock(
                BlockId: "blk-1-1",
                BlockType: DocumentBlockType.Paragraph,
                Text: $"[ZIM placeholder] {Path.GetFileName(filePath)}",
                ReadingOrder: 1,
                PageNumber: 1,
                Attributes: new Dictionary<string, string> { ["status"] = "placeholder" })
        };

        var page = new DocumentPage(1, blocks[0].Text, blocks, "page-1");
        return Task.FromResult(StructuredParserUtilities.BuildDocument(
            filePath,
            DocumentFormatType.Zim,
            ParserVersion,
            Path.GetFileName(filePath),
            new[] { page },
            blocks));
    }
}

