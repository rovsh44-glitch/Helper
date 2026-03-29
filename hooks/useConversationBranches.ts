import { useCallback } from 'react';
import { useConversationActions, useConversationRuntimeState } from '../contexts/ConversationStateContext';
import {
  activateConversationBranch,
  compareConversationBranches,
  createConversationBranch,
  getConversationSnapshot,
  mergeConversationBranches,
} from '../services/conversationApi';
import { appendSystemMessage } from '../utils/conversationUi';
import { toConversationSnapshotState } from '../utils/conversationSnapshots';

export function useConversationBranches() {
  const { activeBranchId, conversationId } = useConversationRuntimeState();
  const { applySnapshot, setMessages, setActiveBranchId, setAvailableBranches } = useConversationActions();

  const refreshConversation = useCallback(async () => {
    if (!conversationId) {
      return;
    }

    const snapshot = await getConversationSnapshot(conversationId);
    applySnapshot(toConversationSnapshotState(snapshot));
  }, [applySnapshot, conversationId]);

  const handleCreateBranch = useCallback(async (fromTurnId: string) => {
    if (!conversationId || !fromTurnId) {
      return;
    }

    try {
      const result = await createConversationBranch(conversationId, { fromTurnId });
      if (!result.success || !result.branchId) {
        return;
      }

      setActiveBranchId(result.branchId);
      setAvailableBranches(previous => Array.from(new Set([...(previous as string[]), result.branchId!])) as string[]);
      await refreshConversation();
    } catch (error) {
      setMessages(previous => appendSystemMessage(previous, `Branch creation failed: ${error instanceof Error ? error.message : 'Unknown error'}`));
    }
  }, [conversationId, refreshConversation, setActiveBranchId, setAvailableBranches, setMessages]);

  const handleSwitchBranch = useCallback(async (branchId: string) => {
    if (!conversationId || !branchId) {
      return;
    }

    try {
      await activateConversationBranch(conversationId, branchId);
      await refreshConversation();
    } catch (error) {
      setMessages(previous => appendSystemMessage(previous, `Branch switch failed: ${error instanceof Error ? error.message : 'Unknown error'}`));
    }
  }, [conversationId, refreshConversation, setMessages]);

  const handleMergeIntoActive = useCallback(async (sourceBranchId: string) => {
    if (!conversationId || !sourceBranchId || !activeBranchId || sourceBranchId === activeBranchId) {
      return;
    }

    try {
      const comparison = await compareConversationBranches(conversationId, sourceBranchId, activeBranchId);
      const preview = comparison.sourceOnlyMessages
        .slice(0, 3)
        .map(item => `- ${item.turnId || 'n/a'} (${item.role}) tools=${item.provenance.toolCalls}, citations=${item.provenance.citations}: ${item.contentPreview}`)
        .join('\n');

      const confirmed = window.confirm(
        `Merge '${sourceBranchId}' -> '${activeBranchId}'?\n` +
        `Shared turns: ${comparison.sharedTurnIds.length}\n` +
        `Source-only turns: ${comparison.sourceOnlyTurnIds.length}\n` +
        `Target-only turns: ${comparison.targetOnlyTurnIds.length}\n\n` +
        `${preview || 'No source-only preview messages.'}`,
      );
      if (!confirmed) {
        return;
      }

      const result = await mergeConversationBranches(conversationId, {
        sourceBranchId,
        targetBranchId: activeBranchId,
      });

      if (!result.success) {
        throw new Error(result.error || 'Branch merge failed');
      }

      await refreshConversation();
      setMessages(previous => appendSystemMessage(previous, `Merged branch '${sourceBranchId}' into '${activeBranchId}'.`));
    } catch (error) {
      setMessages(previous => appendSystemMessage(previous, `Branch merge failed: ${error instanceof Error ? error.message : 'Unknown error'}`));
    }
  }, [activeBranchId, conversationId, refreshConversation, setMessages]);

  return {
    handleCreateBranch,
    handleSwitchBranch,
    handleMergeIntoActive,
  };
}
