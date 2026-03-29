namespace Helper.Runtime.Core;

public static class HelperKnowledgeCollections
{
    public const string CanonicalDefault = "helper_knowledge";
    public const string CanonicalSemantic = "helper_semantic";
    public const string CanonicalEpisodic = "helper_episodic";
    public const string CanonicalIngestScope = "rag:helper_knowledge";

    public static string NormalizeWriteCollection(string? collection)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return CanonicalDefault;
        }

        return collection.Trim();
    }

    public static IReadOnlyList<string> ExpandReadCandidates(string? collection)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return new[] { CanonicalDefault };
        }

        return new[] { collection.Trim() };
    }
}
