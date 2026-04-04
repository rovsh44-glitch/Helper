import { useCallback } from 'react';
import { useConversationRuntimeState, useConversationShellState } from '../contexts/ConversationStateContext';
import { getConversationSnapshot } from '../services/conversationApi';
import { useHelperHubContext } from './useHelperHubContext';

type SerializableSnapshot = {
  exportedAtUtc: string;
  source: 'server' | 'local';
  conversationId: string | null;
  activeBranchId: string;
  availableBranches: string[];
  liveWebMode: string;
  startupState: string;
  startupAlert: string | null;
  messages: unknown[];
  progressEntries: unknown[];
  thoughts: unknown[];
  currentPlan: unknown | null;
};

function downloadSnapshotFile(payload: SerializableSnapshot) {
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `helper-conversation-snapshot-${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 0);
}

export function useConversationArchiveAction() {
  const runtime = useConversationRuntimeState();
  const shell = useConversationShellState();
  const { currentPlan, progressEntries, thoughts } = useHelperHubContext();

  const handleArchiveConversationSnapshot = useCallback(async () => {
    const exportedAtUtc = new Date().toISOString();

    let payload: SerializableSnapshot;

    if (runtime.conversationId) {
      try {
        const snapshot = await getConversationSnapshot(runtime.conversationId);
        payload = {
          exportedAtUtc,
          source: 'server',
          conversationId: snapshot.conversationId,
          activeBranchId: snapshot.activeBranchId || runtime.activeBranchId,
          availableBranches: snapshot.branches && snapshot.branches.length > 0
            ? snapshot.branches
            : runtime.availableBranches,
          liveWebMode: shell.liveWebMode,
          startupState: shell.startupState,
          startupAlert: shell.startupAlert,
          messages: snapshot.messages,
          progressEntries,
          thoughts,
          currentPlan,
        };
        downloadSnapshotFile(payload);
        return `Conversation snapshot saved from backend at ${new Date(exportedAtUtc).toLocaleTimeString()}.`;
      } catch {
        // Fall back to the local surface when server snapshot retrieval is unavailable.
      }
    }

    payload = {
      exportedAtUtc,
      source: 'local',
      conversationId: runtime.conversationId ?? null,
      activeBranchId: runtime.activeBranchId,
      availableBranches: runtime.availableBranches,
      liveWebMode: shell.liveWebMode,
      startupState: shell.startupState,
      startupAlert: shell.startupAlert,
      messages: runtime.messages,
      progressEntries,
      thoughts,
      currentPlan,
    };
    downloadSnapshotFile(payload);
    return `Conversation snapshot saved from local UI state at ${new Date(exportedAtUtc).toLocaleTimeString()}.`;
  }, [
    currentPlan,
    progressEntries,
    runtime.activeBranchId,
    runtime.availableBranches,
    runtime.conversationId,
    runtime.messages,
    shell.liveWebMode,
    shell.startupAlert,
    shell.startupState,
    thoughts,
  ]);

  return {
    handleArchiveConversationSnapshot,
  };
}
