import { useAttachmentQueue } from '../../hooks/useAttachmentQueue';
import { useConversationBranches } from '../../hooks/useConversationBranches';
import { useConversationStreaming } from '../../hooks/useConversationStreaming';
import { useConversationTurnActions } from '../../hooks/useConversationTurnActions';
import { useFeedbackActions } from '../../hooks/useFeedbackActions';
import { useHelperHubContext } from '../../hooks/useHelperHubContext';
import { useConversationArchiveAction } from '../../hooks/useConversationArchiveAction';
import { useConversationResetAction } from '../../hooks/useConversationResetAction';
import { useResumeLastTurnAction } from '../../hooks/useConversationBootstrap';
import { useConversationBranchRouteSync } from '../../hooks/useConversationBranchRouteSync';
import { useConversationActions, useConversationRuntimeState, useConversationShellState } from '../../contexts/ConversationStateContext';
import { readPreferredLanguage } from '../../services/conversationSession';
import type { LiveVoiceTurnPayload } from '../../services/liveVoiceRuntime';
import { OrchestratorView } from './OrchestratorView';

export function OrchestratorContainer() {
  const runtime = useConversationRuntimeState();
  const shell = useConversationShellState();
  const { setConversationId, setInput, setLiveWebMode } = useConversationActions();
  const { handleAttachFiles, clearAttachments } = useAttachmentQueue();
  const { handleSendMessage } = useConversationStreaming();
  const { handleCreateBranch, handleMergeIntoActive, handleSwitchBranch } = useConversationBranches();
  const { handleRegenerateTurn, handleRepairTurn } = useConversationTurnActions();
  const { handleRateMessage } = useFeedbackActions();
  const { handleArchiveConversationSnapshot } = useConversationArchiveAction();
  const { handleResetConversation } = useConversationResetAction();
  const { progressEntries, currentPlan, activeMutation, dismissActiveMutation } = useHelperHubContext();
  const resumeLastTurn = useResumeLastTurnAction();

  useConversationBranchRouteSync({
    activeBranchId: runtime.activeBranchId,
    availableBranches: runtime.availableBranches,
    onSwitchBranch: handleSwitchBranch,
  });

  const ensureConversationId = () => {
    if (runtime.conversationId) {
      return runtime.conversationId;
    }

    const nextConversationId = crypto.randomUUID();
    setConversationId(nextConversationId);
    return nextConversationId;
  };

  return (
    <OrchestratorView
      messages={runtime.messages}
      input={runtime.input}
      setInput={setInput}
      handleSendMessage={handleSendMessage}
      handleVoiceTurn={(payload: LiveVoiceTurnPayload) => void handleSendMessage({
        message: payload.transcript,
        inputMode: 'voice',
        attachments: payload.attachments,
      })}
      conversationId={runtime.conversationId}
      ensureConversationId={ensureConversationId}
      isProcessing={runtime.isProcessing}
      progressEntries={progressEntries}
      currentPlan={currentPlan}
      activeMutation={activeMutation}
      onDismissMutation={dismissActiveMutation}
      activeBranchId={runtime.activeBranchId}
      availableBranches={runtime.availableBranches}
      pendingAttachments={runtime.pendingAttachments}
      onAttachFiles={handleAttachFiles}
      onClearAttachments={clearAttachments}
      onSwitchBranch={handleSwitchBranch}
      onMergeIntoActive={handleMergeIntoActive}
      onRegenerate={handleRegenerateTurn}
      onCreateBranch={handleCreateBranch}
      onRepairTurn={handleRepairTurn}
      onRateMessage={handleRateMessage}
      onArchiveConversationSnapshot={handleArchiveConversationSnapshot}
      onResetConversation={handleResetConversation}
      canResetConversation={runtime.messages.length > 0 || runtime.pendingAttachments.length > 0 || Boolean(runtime.conversationId)}
      canArchiveConversation={runtime.messages.length > 0 || Boolean(runtime.conversationId)}
      conversationSessionEpoch={runtime.sessionEpoch}
      streamingMessageId={runtime.streamingMessageId}
      startupState={shell.startupState}
      startupAlert={shell.startupAlert}
      liveWebMode={shell.liveWebMode}
      onChangeLiveWebMode={setLiveWebMode}
      preferredLanguage={readPreferredLanguage()}
      canResumeLastTurn={shell.resumeAvailable}
      onResumeLastTurn={resumeLastTurn}
    />
  );
}
