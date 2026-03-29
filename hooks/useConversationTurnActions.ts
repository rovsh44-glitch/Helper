import { useCallback } from 'react';
import { useConversationActions, useConversationRuntimeState, useConversationShellState } from '../contexts/ConversationStateContext';
import {
  buildRepairConversationRequest,
  type RepairConversationDraft,
  regenerateConversationTurn,
  repairConversationFlow,
} from '../services/conversationApi';
import { appendSystemMessage, getSystemInstruction } from '../utils/conversationUi';
import { toConversationResponseState } from '../utils/conversationSnapshots';
import { readPreferredLanguage, readSavedStylePreferences } from '../services/conversationSession';

export function useConversationTurnActions() {
  const { activeBranchId, conversationId, isProcessing, messages } = useConversationRuntimeState();
  const { responseStyle, liveWebMode } = useConversationShellState();
  const { applySnapshot, setMessages, setProcessing } = useConversationActions();

  const handleRegenerateTurn = useCallback(async (turnId: string) => {
    if (!conversationId || !turnId || isProcessing) {
      return;
    }

    setProcessing(true);
    try {
      const response = await regenerateConversationTurn(conversationId, turnId, {
        maxHistory: 24,
        systemInstruction: getSystemInstruction(responseStyle, readPreferredLanguage(), readSavedStylePreferences()),
        branchId: activeBranchId,
        liveWebMode,
      });

      applySnapshot(toConversationResponseState(response, messages));
    } catch (error) {
      setMessages(previous => appendSystemMessage(previous, `Regenerate failed: ${error instanceof Error ? error.message : 'Unknown error'}`));
    } finally {
      setProcessing(false);
    }
  }, [activeBranchId, applySnapshot, conversationId, isProcessing, liveWebMode, messages, responseStyle, setMessages, setProcessing]);

  const handleRepairTurn = useCallback(async (turnId: string, draft: RepairConversationDraft) => {
    if (!conversationId || !turnId || isProcessing) {
      return false;
    }

    if (!draft.correctedIntent.trim()) {
      return false;
    }

    setProcessing(true);
    try {
      const repaired = await repairConversationFlow(conversationId, {
        ...buildRepairConversationRequest({
          turnId,
          maxHistory: 24,
          systemInstruction: getSystemInstruction(responseStyle, readPreferredLanguage(), readSavedStylePreferences()),
          branchId: activeBranchId,
          liveWebMode,
        }, draft),
      });

      applySnapshot(toConversationResponseState(repaired, messages));
      return true;
    } catch (error) {
      setMessages(previous => appendSystemMessage(previous, `Conversation repair failed: ${error instanceof Error ? error.message : 'Unknown error'}`));
      return false;
    } finally {
      setProcessing(false);
    }
  }, [activeBranchId, applySnapshot, conversationId, isProcessing, liveWebMode, messages, responseStyle, setMessages, setProcessing]);

  return {
    handleRegenerateTurn,
    handleRepairTurn,
  };
}
