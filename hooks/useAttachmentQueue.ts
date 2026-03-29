import { useCallback } from 'react';
import { useConversationActions, useConversationRuntimeState } from '../contexts/ConversationStateContext';
import type { ChatAttachment } from '../types';

export function useAttachmentQueue() {
  const { pendingAttachments } = useConversationRuntimeState();
  const { setPendingAttachments } = useConversationActions();

  const handleAttachFiles = useCallback((files: FileList | null) => {
    if (!files || files.length === 0) {
      return;
    }

    const mapped: ChatAttachment[] = Array.from(files)
      .slice(0, 8)
      .map(file => ({
        id: crypto.randomUUID(),
        type: file.type || 'application/octet-stream',
        name: file.name,
        sizeBytes: file.size,
      }));

    setPendingAttachments(mapped);
  }, [setPendingAttachments]);

  const clearAttachments = useCallback(() => {
    setPendingAttachments([]);
  }, [setPendingAttachments]);

  return {
    pendingAttachments,
    handleAttachFiles,
    clearAttachments,
  };
}
