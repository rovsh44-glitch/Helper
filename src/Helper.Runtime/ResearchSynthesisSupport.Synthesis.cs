using System.Text;
using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Infrastructure;

internal static partial class ResearchSynthesisSupport
{
    public static string BuildDeterministicSynthesis(
        string topic,
        IReadOnlyList<string> sources,
        IReadOnlyList<ResearchEvidenceItem> evidenceItems)
    {
        var profile = ResearchRequestProfileResolver.From(topic);
        if (evidenceItems.Count == 0 || evidenceItems.All(static item => item.IsFallback))
        {
            return BuildHonestFallback(topic, sources);
        }

        if (profile.IsDocumentAnalysis)
        {
            return BuildDeterministicDocumentAnalysis(topic, sources, evidenceItems, profile);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Research request: {topic}");
        builder.AppendLine();

        var first = evidenceItems[0];
        if (evidenceItems.Count == 1)
        {
            builder.AppendLine($"{first.Title} [1] is the clearest direct source here, and it focuses on {FormatSnippet(first.Snippet)}.");
            builder.AppendLine("The safest reading is to anchor the answer to that source instead of generalizing beyond it [1].");
            return builder.ToString().TrimEnd();
        }

        var second = evidenceItems[1];
        builder.AppendLine($"{first.Title} [1] focuses on {FormatSnippet(first.Snippet)}, while {second.Title} [2] focuses on {FormatSnippet(second.Snippet)}.");

        if (evidenceItems.Count >= 3)
        {
            var third = evidenceItems[2];
            builder.AppendLine($"{third.Title} [3] adds {FormatSnippet(third.Snippet)}, which broadens the picture without replacing the first two sources.");
        }

        builder.AppendLine(BuildComparativeTakeaway(evidenceItems.Count >= 3));
        return builder.ToString().TrimEnd();
    }

    public static string BuildHonestFallbackResponse(string topic, IReadOnlyList<string> sources)
        => BuildHonestFallback(topic, sources);

    private static string BuildComparativeTakeaway(bool hasThirdSource)
    {
        return hasThirdSource
            ? "Taken together, [1] and [2] provide the core comparison, while [3] adds a narrower implementation angle."
            : "Taken together, the sources are complementary rather than redundant: [1] answers one side of the question, and [2] answers another.";
    }

    private static string BuildHonestFallback(string topic, IReadOnlyList<string> sources)
    {
        var profile = ResearchRequestProfileResolver.From(topic);
        var builder = new StringBuilder();
        if (profile.IsDocumentAnalysis)
        {
            builder.AppendLine("I could not reliably read enough of the referenced document to support a responsible analysis.");
            if (ContainsUrl(topic))
            {
                builder.AppendLine("The link was detected, but the document body was not retrieved strongly enough to prove that the document itself was actually read.");
            }
            else
            {
                builder.AppendLine("The request looks like document analysis, but the runtime did not retrieve enough grounded document content to support a responsible reading.");
            }

            builder.AppendLine("So the safe conclusion is to stop at that limit instead of pretending the thesis, strengths, or weaknesses were confirmed.");
            builder.AppendLine("My view: when document retrieval fails, the honest answer is to ask for a direct retry or the document text, not to improvise a review.");
        }
        else
        {
            builder.AppendLine("I could not verify enough grounded sources to answer this responsibly in the current runtime.");
            if (ContainsUrl(topic))
            {
                builder.AppendLine("The request points to one or more URLs, but the page contents were not retrieved strongly enough to justify analyzing the source itself.");
            }
            else if (sources.Count > 0)
            {
                builder.AppendLine("Candidate source URLs were preserved, but not enough grounded page content was retrieved to compare them safely.");
            }
            else
            {
                builder.AppendLine("No verifiable sources were retrieved for this topic.");
            }

            builder.AppendLine("So the safe conclusion is to treat the question as unresolved until the source path works or the relevant text is provided explicitly.");
            builder.AppendLine("My view: it is better to stop at this evidence limit than to smooth it over with a confident-looking but unverified answer.");
        }
        return builder.ToString().TrimEnd();
    }

    private static string BuildDeterministicDocumentAnalysis(
        string topic,
        IReadOnlyList<string> sources,
        IReadOnlyList<ResearchEvidenceItem> evidenceItems,
        ResearchRequestProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Research request: {topic}");
        builder.AppendLine();

        var primary = evidenceItems[0];
        builder.Append(primary.Title)
            .Append(" [1] is the clearest retrieved document here. ");
        builder.Append("From that source, the strongest supported reading is this: ");
        builder.Append(FormatSnippet(primary.Snippet));
        builder.Append(".");
        builder.AppendLine();

        if (evidenceItems.Count > 1)
        {
            var secondary = evidenceItems[1];
            builder.Append(secondary.Title)
                .Append(" [2] adds ")
                .Append(FormatSnippet(secondary.Snippet))
                .AppendLine(".");
        }

        builder.AppendLine();
        builder.Append("My view: ");
        builder.Append(profile.LooksLikePaperOrArticle
            ? "the idea looks technically interesting, but I would want direct reading of the full document, ablations, and stronger cross-source confirmation before treating the strongest claim as settled."
            : "the source looks potentially useful, but I would still separate what is directly supported from what would require a fuller read of the original document.");
        builder.AppendLine();
        builder.Append("Main limitation: this synthesis is grounded only in the retrieved document evidence");
        if (sources.Count > 1)
        {
            builder.Append(", not in a full comparison of every relevant source");
        }
        builder.AppendLine(".");

        return builder.ToString().TrimEnd();
    }
}
