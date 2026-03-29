using System.Globalization;
using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Retrieval;

internal static class RetrievalRoutingMetadataSupport
{
    public static void Append(IEnumerable<KnowledgeChunk> results, CollectionRoute route)
    {
        if (results is null)
        {
            return;
        }

        foreach (var result in results)
        {
            if (!result.Metadata.ContainsKey("collection"))
            {
                result.Metadata["collection"] = route.Collection;
            }

            result.Metadata["routing_score"] = route.Score.ToString(CultureInfo.InvariantCulture);
            result.Metadata["routing_anchor_score"] = route.AnchorScore.ToString(CultureInfo.InvariantCulture);
            result.Metadata["routing_anchor_matches"] = route.AnchorMatches.ToString(CultureInfo.InvariantCulture);
            result.Metadata["routing_hint_matches"] = route.HintMatches.ToString(CultureInfo.InvariantCulture);
            result.Metadata["routing_rank"] = route.Rank.ToString(CultureInfo.InvariantCulture);
        }
    }
}

