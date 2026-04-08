using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public interface IConversationModelSelectionPolicy
{
    TurnModelRoutingDecision Select(ChatTurnContext context, IReadOnlyList<string> availableModels);
}

public sealed class ConversationModelSelectionPolicy : IConversationModelSelectionPolicy
{
    private readonly IConversationModelCapabilityCatalog _catalog;
    private readonly IProviderProfileResolver? _providerProfileResolver;

    public ConversationModelSelectionPolicy(
        IConversationModelCapabilityCatalog? catalog = null,
        IProviderProfileResolver? providerProfileResolver = null)
    {
        _catalog = catalog ?? new ConversationModelCapabilityCatalog();
        _providerProfileResolver = providerProfileResolver;
    }

    public TurnModelRoutingDecision Select(ChatTurnContext context, IReadOnlyList<string> availableModels)
    {
        ArgumentNullException.ThrowIfNull(context);

        var reasons = new List<string>();
        if (context.Request.Attachments is { Count: > 0 })
        {
            reasons.Add("attachments_present");
            return CreateDecision(HelperModelClass.Vision, "vision", reasons, availableModels);
        }

        if (context.Intent.Intent == IntentType.Generate)
        {
            reasons.Add("intent_generate");
            return CreateDecision(HelperModelClass.Coder, "coder", reasons, availableModels);
        }

        if (context.IsFactualPrompt || context.Intent.Intent == IntentType.Research || context.BudgetProfile == TurnBudgetProfile.HighRisk)
        {
            reasons.Add(context.Intent.Intent == IntentType.Research ? "intent_research" : "verification_or_factual");
            return CreateDecision(HelperModelClass.Reasoning, "primary_dialogue", reasons, availableModels);
        }

        if (context.ExecutionMode == TurnExecutionMode.Fast)
        {
            reasons.Add("fast_execution_mode");
            return CreateDecision(HelperModelClass.Fast, "fast", reasons, availableModels);
        }

        reasons.Add("default_primary_dialogue");
        return CreateDecision(HelperModelClass.Reasoning, "primary_dialogue", reasons, availableModels);
    }

    private TurnModelRoutingDecision CreateDecision(
        HelperModelClass modelClass,
        string routeKey,
        List<string> reasons,
        IReadOnlyList<string> availableModels)
    {
        var profileBinding = _providerProfileResolver?.ResolveModelBinding(modelClass);
        if (!string.IsNullOrWhiteSpace(profileBinding))
        {
            reasons.Add($"profile_binding:{modelClass.ToString().ToLowerInvariant()}");
            if (availableModels.Count > 0 && !availableModels.Contains(profileBinding, StringComparer.OrdinalIgnoreCase))
            {
                reasons.Add("profile_binding_not_in_catalog");
            }

            return new TurnModelRoutingDecision(profileBinding!, modelClass, routeKey, reasons);
        }

        return new TurnModelRoutingDecision(
            _catalog.ResolveBestModel(routeKey, availableModels),
            modelClass,
            routeKey,
            reasons);
    }
}
