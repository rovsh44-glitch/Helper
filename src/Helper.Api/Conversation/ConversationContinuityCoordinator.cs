namespace Helper.Api.Conversation;

public interface IConversationContinuityCoordinator
{
    void CaptureTurnStart(ConversationState state, ChatTurnContext context);
}

public sealed class ConversationContinuityCoordinator : IConversationContinuityCoordinator
{
    private readonly ILiveVoiceTurnCoordinator _liveVoiceTurnCoordinator;
    private readonly IMultimodalReferenceProjector _multimodalReferenceProjector;

    public ConversationContinuityCoordinator(
        ILiveVoiceTurnCoordinator? liveVoiceTurnCoordinator = null,
        IMultimodalReferenceProjector? multimodalReferenceProjector = null)
    {
        _liveVoiceTurnCoordinator = liveVoiceTurnCoordinator ?? new LiveVoiceTurnCoordinator();
        _multimodalReferenceProjector = multimodalReferenceProjector ?? new MultimodalReferenceProjector();
    }

    public void CaptureTurnStart(ConversationState state, ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        var projectedReferences = _multimodalReferenceProjector.Project(context);

        if (ConversationInputMode.IsVoice(context.Request.InputMode))
        {
            var language = string.IsNullOrWhiteSpace(state.PreferredLanguage) ? "auto" : state.PreferredLanguage;
            _liveVoiceTurnCoordinator.Touch(
                state,
                language,
                context.Request.Message,
                runtimeKind: "live_capture",
                transcriptSegments: string.IsNullOrWhiteSpace(context.Request.Message)
                    ? Array.Empty<string>()
                    : new[] { context.Request.Message.Trim() },
                attachedReferenceCount: projectedReferences.Count,
                lastReferenceSummary: projectedReferences.Count == 0 ? "No active multimodal references." : string.Join(", ", projectedReferences.Take(3)));
        }

        if (state.ProjectContext is null)
        {
            return;
        }

        if (projectedReferences.Count == 0)
        {
            return;
        }

        lock (state.SyncRoot)
        {
            var merged = state.ProjectContext.ReferenceArtifacts
                .Concat(projectedReferences)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray();

            state.ProjectContext = state.ProjectContext with
            {
                ReferenceArtifacts = merged,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }
}
