namespace Helper.Runtime.Knowledge.Retrieval;

internal static class RetrievalRoutingText
{
    private static readonly string[] ProfileNoiseTokens =
    {
        "pdf", "epub", "fb2", "djvu", "docs", "doc", "library", "helper", "folder", "data"
    };

    private static readonly string[] HistoricalArchiveGenericTokens =
    {
        "большая", "советская", "энциклопедия", "изд", "издание", "том", "томов", "тома", "третье", "третьему"
    };

    internal static HashSet<string> StopWords { get; } = RetrievalTextProcessing.CreateStopWordSet(
        ProfileNoiseTokens.Concat(ExternalProductLexicon.RetrievalNoiseTokens).ToArray());

    internal static HashSet<string> HistoricalArchiveGenericTokenSet { get; } =
        new(HistoricalArchiveGenericTokens, StringComparer.OrdinalIgnoreCase);

    internal static HashSet<string> HistoricalArchiveGenericRootSet { get; } =
        new(HistoricalArchiveGenericTokens.Select(RetrievalTextProcessing.BuildStaticRoot), StringComparer.OrdinalIgnoreCase);

    internal static string NormalizeText(string? text) => RetrievalTextProcessing.NormalizeText(text);

    internal static IEnumerable<string> Tokenize(string normalizedText) => RetrievalTextProcessing.Tokenize(normalizedText, StopWords);

    internal static string BuildRoot(string token) => RetrievalTextProcessing.BuildRoot(token);

    internal static string BuildStaticRoot(string token) => RetrievalTextProcessing.BuildStaticRoot(token);
}

internal sealed record PreparedRoutingQuery(string NormalizedText, IReadOnlyList<RoutingQueryTerm> Terms)
{
    public static PreparedRoutingQuery Create(string query)
    {
        var normalizedText = RetrievalRoutingText.NormalizeText(query);
        var terms = RetrievalRoutingText.Tokenize(normalizedText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(token => new RoutingQueryTerm(token, BuildRoots(token)))
            .ToList();

        return new PreparedRoutingQuery(normalizedText, terms);
    }

    private static IReadOnlyList<string> BuildRoots(string token)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RetrievalRoutingText.BuildRoot(token)
        };

        if (token.Length >= 6)
        {
            roots.Add(token[..Math.Min(token.Length, 5)]);
        }

        return roots
            .Where(static root => root.Length >= 4)
            .ToList();
    }
}

internal sealed record RoutingQueryTerm(string Token, IReadOnlyList<string> Roots);

internal sealed record CollectionProfile(
    string Collection,
    string Domain,
    int PointCount,
    Dictionary<string, double> TokenWeights,
    Dictionary<string, double> RootWeights,
    Dictionary<string, double> AnchorTokenWeights,
    Dictionary<string, double> AnchorRootWeights);

internal readonly record struct CollectionRoutingScore(double Score, double AnchorScore, int AnchorMatches, int HintMatches);

internal readonly record struct CollectionRoute(string Collection, double Score, double AnchorScore, int AnchorMatches, int HintMatches, int Rank);

