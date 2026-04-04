import { useAttachmentQueue } from '../../hooks/useAttachmentQueue';
import { useConversationBranches } from '../../hooks/useConversationBranches';
import { useConversationStreaming } from '../../hooks/useConversationStreaming';
import { useConversationTurnActions } from '../../hooks/useConversationTurnActions';
import { useFeedbackActions } from '../../hooks/useFeedbackActions';
import { useHelperHubContext } from '../../hooks/useHelperHubContext';
import { useConversationArchiveAction } from '../../hooks/useConversationArchiveAction';
import { useConversationResetAction } from '../../hooks/useConversationResetAction';
import { useResumeLastTurnAction } from '../../hooks/useConversationBootstrap';
import { useConversationActions, useConversationRuntimeState, useConversationShellState } from '../../contexts/ConversationStateContext';
import { readPreferredLanguage } from '../../services/conversationSession';
import { OrchestratorView } from './OrchestratorView';

export function OrchestratorContainer() {
  const runtime = useConversationRuntimeState();
  const shell = useConversationShellState();
  const { setInput, setLiveWebMode } = useConversationActions();
  const { handleAttachFiles, clearAttachments } = useAttachmentQueue();
  const { handleSendMessage } = useConversationStreaming();
  const { handleCreateBranch, handleMergeIntoActive, handleSwitchBranch } = useConversationBranches();
  const { handleRegenerateTurn, handleRepairTurn } = useConversationTurnActions();
  const { handleRateMessage } = useFeedbackActions();
  const { handleArchiveConversationSnapshot } = useConversationArchiveAction();
  const { handleResetConversation } = useConversationResetAction();
  const { progressEntries, currentPlan, activeMutation, dismissActiveMutation } = useHelperHubContext();
  const resumeLastTurn = useResumeLastTurnAction();

  return (
    <OrchestratorView
      messages={runtime.messages}
      input={runtime.input}
      setInput={setInput}
      handleSendMessage={handleSendMessage}
      handleVoiceInput={(transcript) => void handleSendMessage({ message: transcript, inputMode: 'voice' })}
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
