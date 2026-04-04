import { useCallback } from 'react';
import { useConversationActions, useConversationRuntimeState } from '../contexts/ConversationStateContext';
import { deleteConversation } from '../services/conversationApi';
import { clearPendingTurn, clearStoredConversationId } from '../services/conversationSession';
import { useHelperHubContext } from './useHelperHubContext';

export function useConversationResetAction() {
  const { conversationId } = useConversationRuntimeState();
  const { resetConversationRuntime, setResumeAvailable } = useConversationActions();
  const { clearConversationSurface } = useHelperHubContext();

  const handleResetConversation = useCallback(async () => {
    let warning: string | null = null;

    if (conversationId) {
      try {
        await deleteConversation(conversationId);
      } catch (error) {
        warning = error instanceof Error
          ? `Started a fresh chat locally, but the previous server-side conversation could not be deleted: ${error.message}`
          : 'Started a fresh chat locally, but the previous server-side conversation could not be deleted.';
      }
    }

    clearPendingTurn();
    clearStoredConversationId();
    clearConversationSurface();
    resetConversationRuntime();
    setResumeAvailable(false);

    return warning;
  }, [
    clearConversationSurface,
    conversationId,
    resetConversationRuntime,
    setResumeAvailable,
  ]);

  return {
    handleResetConversation,
  };
}
