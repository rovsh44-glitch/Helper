using System.Globalization;
using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Retrieval;

internal sealed class RetrievalContextExpansionService
{
    private readonly IStructuredVectorStore _structuredStore;

    public RetrievalContextExpansionService(IStructuredVectorStore structuredStore)
    {
        _structuredStore = structuredStore;
    }

    public async Task ExpandAsync(Dictionary<string, KnowledgeChunk> expanded, KnowledgeChunk source, CancellationToken ct)
    {
        var collection = source.Metadata.GetValueOrDefault("collection", source.Collection);
        var parentId = source.Metadata.GetValueOrDefault("parent_id");
        var chunkRole = source.Metadata.GetValueOrDefault("chunk_role");
        var logicalChunkId = source.Metadata.GetValueOrDefault("chunk_id");
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            var parent = await _structuredStore.GetChunkByChunkIdAsync(collection, parentId, ct).ConfigureAwait(false);
            if (parent is not null)
            {
                expanded[parent.Id] = parent;
            }
        }

        if (string.Equals(chunkRole, "parent", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(logicalChunkId))
        {
            await ExpandChildrenAsync(expanded, collection, logicalChunkId, ct).ConfigureAwait(false);
        }
        else if (string.Equals(chunkRole, "child", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(parentId))
        {
            await ExpandSiblingsAsync(expanded, collection, parentId, logicalChunkId, ct).ConfigureAwait(false);
        }

        var neighbors = await _structuredStore.GetChunksByChunkIdsAsync(
                collection,
                new[]
                {
                    source.Metadata.GetValueOrDefault("prev_chunk_id"),
                    source.Metadata.GetValueOrDefault("next_chunk_id")
                }.Where(static id => !string.IsNullOrWhiteSpace(id)).Cast<string>(),
                ct)
            .ConfigureAwait(false);
        foreach (var neighbor in neighbors)
        {
            expanded[neighbor.Id] = neighbor;
        }

        var documentId = source.Metadata.GetValueOrDefault("document_id");
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return;
        }

        var localGroup = await _structuredStore.GetDocumentLocalGroupAsync(
                collection,
                documentId,
                source.Metadata.GetValueOrDefault("section_path"),
                ParseOptionalInt(source.Metadata.GetValueOrDefault("page_start")),
                4,
                ct)
            .ConfigureAwait(false);
        foreach (var related in localGroup)
        {
            expanded[related.Id] = related;
        }
    }

    private async Task ExpandChildrenAsync(Dictionary<string, KnowledgeChunk> expanded, string collection, string chunkId, CancellationToken ct)
    {
        var children = await _structuredStore.SearchByMetadataAsync(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["parent_id"] = chunkId
                },
                collection,
                4,
                ct)
            .ConfigureAwait(false);
        foreach (var child in children)
        {
            expanded[child.Id] = child;
        }
    }

    private async Task ExpandSiblingsAsync(Dictionary<string, KnowledgeChunk> expanded, string collection, string parentId, string? currentChunkId, CancellationToken ct)
    {
        var siblings = await _structuredStore.SearchByMetadataAsync(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["parent_id"] = parentId
                },
                collection,
                4,
                ct)
            .ConfigureAwait(false);
        foreach (var sibling in siblings)
        {
            if (string.Equals(sibling.Metadata.GetValueOrDefault("chunk_id"), currentChunkId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            expanded[sibling.Id] = sibling;
        }
    }

    private static int? ParseOptionalInt(string? raw)
    {
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}

