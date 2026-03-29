using System.Text;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Knowledge;

internal static class EncodingBootstrap
{
    private static int _initialized;

    public static void EnsureCodePages()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }
}

public abstract class StructuredParserAdapterBase : IDocumentParser
{
    protected abstract IStructuredDocumentParser StructuredParser { get; }

    public bool CanParse(string extension) => StructuredParser.CanParse(extension);

    public async Task<string> ParseAsync(string filePath, CancellationToken ct = default)
        => StructuredDocumentFormatter.Flatten(await StructuredParser.ParseStructuredAsync(filePath, ct: ct));

    public async Task ParseStreamingAsync(string filePath, Func<string, Task> onChunk, CancellationToken ct = default)
    {
        var document = await StructuredParser.ParseStructuredAsync(filePath, ct: ct);
        foreach (var chunk in StructuredDocumentFormatter.SplitForStreaming(document))
        {
            ct.ThrowIfCancellationRequested();
            await onChunk(chunk);
        }
    }
}

public sealed class PdfParser : StructuredParserAdapterBase
{
    private readonly StructuredPdfParser _structured;

    public PdfParser(AILink? ai = null)
    {
        _structured = new StructuredPdfParser(ai);
    }

    protected override IStructuredDocumentParser StructuredParser => _structured;
}

public sealed class EpubParser : StructuredParserAdapterBase
{
    private readonly StructuredEpubParser _structured = new();

    protected override IStructuredDocumentParser StructuredParser => _structured;
}

public sealed class HtmlParser : StructuredParserAdapterBase
{
    private readonly StructuredHtmlParser _structured = new();

    protected override IStructuredDocumentParser StructuredParser => _structured;
}

public sealed class DocxParser : StructuredParserAdapterBase
{
    private readonly StructuredDocxParser _structured = new();

    protected override IStructuredDocumentParser StructuredParser => _structured;
}

public sealed class Fb2Parser : StructuredParserAdapterBase
{
    private readonly StructuredFb2Parser _structured = new();

    protected override IStructuredDocumentParser StructuredParser => _structured;
}

public sealed class MarkdownParser : StructuredParserAdapterBase
{
    private readonly StructuredMarkdownParser _structured = new();

    protected override IStructuredDocumentParser StructuredParser => _structured;
}

public sealed class DjvuParser : StructuredParserAdapterBase
{
    private readonly StructuredDjvuParser _structured;

    public DjvuParser(AILink ai)
    {
        _structured = new StructuredDjvuParser(ai);
    }

    protected override IStructuredDocumentParser StructuredParser => _structured;
}

public sealed class ChmParser : StructuredParserAdapterBase
{
    private readonly StructuredChmParser _structured = new();

    protected override IStructuredDocumentParser StructuredParser => _structured;
}

public sealed class ZimParser : StructuredParserAdapterBase
{
    private readonly StructuredZimParser _structured = new();

    protected override IStructuredDocumentParser StructuredParser => _structured;
}

