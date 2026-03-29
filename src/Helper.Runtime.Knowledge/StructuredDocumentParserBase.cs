using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge;

public abstract class StructuredDocumentParserBase : IStructuredDocumentParser
{
    public abstract string ParserVersion { get; }

    public abstract bool CanParse(string extension);

    public abstract Task<DocumentParseResult> ParseStructuredAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default);
}

