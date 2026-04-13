using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge.Retrieval;
using Moq;

namespace Helper.Runtime.Tests;

public partial class RetrievalPipelineTests
{
    public RetrievalPipelineTests()
    {
        ContextAssemblyService.ResetCollectionProfilesForTesting();
    }


    private static KnowledgeChunk CreateChunk(string id, string content, string documentId, string title, double vectorScore, double routingScore = 0d, string domain = "physics", string? sourcePath = null, string? collection = null, Dictionary<string, string>? extraMetadata = null)
    {
        var resolvedCollection = collection ?? $"knowledge_{domain}_v2";
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["document_id"] = documentId,
            ["title"] = title,
            ["source_path"] = sourcePath ?? title,
            ["vector_score"] = vectorScore.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["chunk_role"] = "standalone",
            ["page_start"] = "1",
            ["section_path"] = "chapter",
            ["domain"] = domain,
            ["collection"] = resolvedCollection
        };

        if (routingScore > 0d)
        {
            metadata["routing_score"] = routingScore.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (extraMetadata is not null)
        {
            foreach (var pair in extraMetadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        return new KnowledgeChunk(
            id,
            content,
            Array.Empty<float>(),
            metadata,
            resolvedCollection);
    }
}

