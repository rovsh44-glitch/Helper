using System.Globalization;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public sealed record ReasoningAwareRetrievalPlan(
    RetrievalRequestOptions Options,
    int EffectiveLimit,
    bool ExpandContext,
    bool AllowDeepRetrievalFallback,
    int DeepRetrievalLimit,
    double TopicalFitFloor,
    double SourceReuseDominanceThreshold,
    IReadOnlyList<string> Trace);

public interface IReasoningAwareRetrievalPolicy
{
    ReasoningAwareRetrievalPlan Resolve(ChatTurnContext context, MemoryLayerSelection selection);
}

public sealed class ReasoningAwareRetrievalPolicy : IReasoningAwareRetrievalPolicy
{
    private static readonly string[] GenericReferenceDomains =
    {
        "historical_encyclopedias",
        "encyclopedias",
        "analysis_strategy"
    };

    public ReasoningAwareRetrievalPlan Resolve(ChatTurnContext context, MemoryLayerSelection selection)
    {
        var purpose = ResolvePurpose(context);
        var effectiveLimit = ResolveEffectiveLimit(selection, purpose);
        var expandContext = purpose != RetrievalPurpose.ReasoningSupport;
        var allowDeepRetrievalFallback = purpose != RetrievalPurpose.Standard || context.Intent.Intent == IntentType.Research;
        var deepRetrievalLimit = allowDeepRetrievalFallback
            ? Math.Max(effectiveLimit + 2, Math.Min(Math.Max(selection.RetrievalChunkBudget * 2, effectiveLimit + 2), 8))
            : effectiveLimit;
        var topicalFitFloor = purpose switch
        {
            RetrievalPurpose.ReasoningSupport => 0.58d,
            RetrievalPurpose.FactualLookup => 0.52d,
            _ => 0.46d
        };
        var sourceReuseDominanceThreshold = purpose switch
        {
            RetrievalPurpose.ReasoningSupport => 0.55d,
            RetrievalPurpose.FactualLookup => 0.60d,
            _ => 0.72d
        };
        var disallowedDomains = purpose == RetrievalPurpose.ReasoningSupport
            ? GenericReferenceDomains
            : Array.Empty<string>();

        var options = new RetrievalRequestOptions(
            Purpose: purpose,
            PreferredDomains: Array.Empty<string>(),
            DisallowedDomains: disallowedDomains,
            PreferTraceableChunks: purpose != RetrievalPurpose.Standard);

        var trace = new List<string>
        {
            $"purpose:{purpose}",
            $"effective_limit:{effectiveLimit}",
            $"expand_context:{expandContext.ToString().ToLowerInvariant()}",
            $"topical_fit_floor:{topicalFitFloor.ToString("0.00", CultureInfo.InvariantCulture)}",
            $"source_diversity_threshold:{sourceReuseDominanceThreshold.ToString("0.00", CultureInfo.InvariantCulture)}"
        };

        if (disallowedDomains.Length > 0)
        {
            trace.Add($"disallowed_domains:{string.Join(",", disallowedDomains)}");
        }

        if (allowDeepRetrievalFallback)
        {
            trace.Add($"deep_retrieval_limit:{deepRetrievalLimit}");
        }

        return new ReasoningAwareRetrievalPlan(
            options,
            effectiveLimit,
            expandContext,
            allowDeepRetrievalFallback,
            deepRetrievalLimit,
            topicalFitFloor,
            sourceReuseDominanceThreshold,
            trace);
    }

    private static RetrievalPurpose ResolvePurpose(ChatTurnContext context)
    {
        var prompt = context.Request.Message ?? string.Empty;
        if (ReasoningSelectionPolicy.HasVerifiablePromptSignal(prompt) || context.ExecutionMode == TurnExecutionMode.Deep)
        {
            return RetrievalPurpose.ReasoningSupport;
        }

        if (context.IsFactualPrompt || context.Intent.Intent == IntentType.Research)
        {
            return RetrievalPurpose.FactualLookup;
        }

        return RetrievalPurpose.Standard;
    }

    private static int ResolveEffectiveLimit(MemoryLayerSelection selection, RetrievalPurpose purpose)
    {
        return purpose == RetrievalPurpose.ReasoningSupport
            ? Math.Max(2, Math.Min(selection.RetrievalChunkBudget, 3))
            : selection.RetrievalChunkBudget;
    }
}

