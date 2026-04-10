#nullable enable

using System.Text.Json;
using Helper.Api.Conversation;
using Microsoft.AspNetCore.Http;

namespace Helper.Api.Hosting;

internal static class ConversationPreferenceEndpointHandler
{
    public static async Task<IResult> HandleAsync(
        string conversationId,
        HttpRequest request,
        IConversationStore store,
        IUserProfileService userProfile,
        IMemoryPolicyService memoryPolicy,
        IFeatureFlags flags,
        CancellationToken ct)
    {
        if (!flags.MemoryV2Enabled)
        {
            return Results.Json(new
            {
                success = false,
                error = "Memory v2 is disabled."
            }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
        }

        if (!store.TryGet(conversationId, out ConversationState state))
        {
            return Results.NotFound(new
            {
                success = false,
                error = "Conversation not found."
            });
        }

        ConversationPreferencePayloadReader.Update update;
        try
        {
            update = await ConversationPreferencePayloadReader.ReadAsync(request, ct);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new
            {
                success = false,
                error = "Invalid preferences payload."
            });
        }

        userProfile.ApplyPreferences(state, update.Preferences, update.PresentFields);
        memoryPolicy.ApplyPreferences(state, update.Preferences, DateTimeOffset.UtcNow);
        store.MarkUpdated(state);

        var conversationUserProfile = userProfile.Resolve(state);
        var policySnapshot = memoryPolicy.GetPolicySnapshot(state);
        return Results.Ok(new
        {
            success = true,
            longTermMemoryEnabled = policySnapshot.LongTermMemoryEnabled,
            personalMemoryConsentGranted = policySnapshot.PersonalMemoryConsentGranted,
            personalMemoryConsentAt = policySnapshot.PersonalMemoryConsentAt,
            sessionMemoryTtlMinutes = policySnapshot.SessionMemoryTtlMinutes,
            taskMemoryTtlHours = policySnapshot.TaskMemoryTtlHours,
            longTermMemoryTtlDays = policySnapshot.LongTermMemoryTtlDays,
            preferredLanguage = conversationUserProfile.Language,
            detailLevel = conversationUserProfile.DetailLevel,
            formality = conversationUserProfile.Formality,
            domainFamiliarity = conversationUserProfile.DomainFamiliarity,
            preferredStructure = conversationUserProfile.PreferredStructure,
            warmth = conversationUserProfile.Warmth,
            enthusiasm = conversationUserProfile.Enthusiasm,
            directness = conversationUserProfile.Directness,
            defaultAnswerShape = conversationUserProfile.DefaultAnswerShape,
            searchLocalityHint = conversationUserProfile.SearchLocalityHint,
            decisionAssertiveness = conversationUserProfile.DecisionAssertiveness,
            clarificationTolerance = conversationUserProfile.ClarificationTolerance,
            citationPreference = conversationUserProfile.CitationPreference,
            repairStyle = conversationUserProfile.RepairStyle,
            reasoningStyle = conversationUserProfile.ReasoningStyle,
            reasoningEffort = conversationUserProfile.ReasoningEffort,
            projectId = state.ProjectContext?.ProjectId,
            projectLabel = state.ProjectContext?.Label,
            projectInstructions = state.ProjectContext?.Instructions,
            projectMemoryEnabled = state.ProjectContext?.MemoryEnabled,
            backgroundResearchEnabled = state.BackgroundResearchEnabled,
            proactiveUpdatesEnabled = state.ProactiveUpdatesEnabled
        });
    }
}
