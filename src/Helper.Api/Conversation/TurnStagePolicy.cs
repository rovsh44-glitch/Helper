using Helper.Runtime.Core;
using Helper.Api.Backend.Configuration;

namespace Helper.Api.Conversation;

public enum TurnBudgetProfile
{
    ChatLight,
    ChatGrounded,
    Research,
    Generation,
    HighRisk
}

public interface ITurnStagePolicy
{
    bool RequiresSynchronousCritic(ChatTurnContext context);
    bool AllowsAsyncAudit(ChatTurnContext context);
}

public sealed class TurnStagePolicy : ITurnStagePolicy
{
    private readonly IBackendRuntimePolicyProvider _policyProvider;

    public TurnStagePolicy(IBackendRuntimePolicyProvider? policyProvider = null)
    {
        _policyProvider = policyProvider ?? new BackendOptionsCatalog(new Hosting.ApiRuntimeConfig("root", "data", "projects", "library", "logs", "templates", "dev-key"));
    }

    public bool RequiresSynchronousCritic(ChatTurnContext context)
    {
        if (!_policyProvider.GetPolicies().SynchronousCriticEnabled)
        {
            return false;
        }

        if (context.RequiresClarification)
        {
            return false;
        }

        if (context.BudgetProfile == TurnBudgetProfile.HighRisk)
        {
            return true;
        }

        if (context.Intent.Intent == IntentType.Research)
        {
            return false;
        }

        if (context.ToolCalls.Any(x => string.Equals(x, "helper.generate", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return false;
    }

    public bool AllowsAsyncAudit(ChatTurnContext context)
    {
        if (!_policyProvider.GetPolicies().AsyncAuditEnabled)
        {
            return false;
        }

        if (context.RequiresClarification)
        {
            return false;
        }

        if (context.BudgetProfile == TurnBudgetProfile.HighRisk)
        {
            return true;
        }

        return context.Intent.Intent == IntentType.Research;
    }
}

