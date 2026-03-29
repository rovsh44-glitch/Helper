import { useCallback } from 'react';
import { useBufferedAssistantStream } from './useBufferedAssistantStream';
import { useHelperHubContext } from './useHelperHubContext';
import { useConversationActions, useConversationRuntimeState, useConversationShellState } from '../contexts/ConversationStateContext';
import { sendChatTurn, streamChatTurn } from '../services/chatService';
import { clearPendingTurn, readPreferredLanguage, readSavedStylePreferences, savePendingTurn } from '../services/conversationSession';
import { appendSystemMessage, getSystemInstruction, mergeAssistantDoneChunk } from '../utils/conversationUi';
import type { ConversationInputMode, Message } from '../types';

export interface SendConversationTurnOptions {
  message?: string;
  inputMode?: ConversationInputMode;
}

export function useConversationStreaming() {
  const {
    activeBranchId,
    conversationId,
    input,
    isProcessing,
    pendingAttachments,
  } = useConversationRuntimeState();
  const { responseStyle, startupState, liveWebMode } = useConversationShellState();
  const {
    setConversationId,
    setInput,
    setMessages,
    setPendingAttachments,
    setProcessing,
    setStreamingMessageId,
    setActiveBranchId,
    setAvailableBranches,
  } = useConversationActions();
  const { clearProgressState } = useHelperHubContext();
  const { appendChunk, flushBufferedContent } = useBufferedAssistantStream(setMessages);

  const handleSendMessage = useCallback(async (options?: SendConversationTurnOptions) => {
    const requestedMessage = options?.message ?? input;
    const normalizedMessage = requestedMessage.trim();
    if (!normalizedMessage || isProcessing || startupState === 'booting') {
      return;
    }

    const attachmentsToSend = pendingAttachments;
    const inputMode = options?.inputMode ?? 'text';
    const assistantMessageId = crypto.randomUUID();
    const userMessage: Message = {
      id: crypto.randomUUID(),
      role: 'user',
      content: normalizedMessage,
      timestamp: Date.now(),
      branchId: activeBranchId,
      attachments: attachmentsToSend,
      inputMode,
    };

    setMessages(previous => [
      ...previous,
      userMessage,
      {
        id: assistantMessageId,
        role: 'assistant',
        content: '',
        timestamp: Date.now(),
        inputMode,
      },
    ]);
    if (!options?.message || normalizedMessage === input.trim()) {
      setInput('');
    }
    setPendingAttachments([]);
    setProcessing(true);
    setStreamingMessageId(assistantMessageId);
    clearProgressState();
    savePendingTurn({
      conversationId,
      message: userMessage.content,
      inputMode,
      createdAt: Date.now(),
    });

    try {
      const systemInstruction = getSystemInstruction(responseStyle, readPreferredLanguage(), readSavedStylePreferences());

      try {
        await streamChatTurn(userMessage.content, conversationId, chunk => {
          if (chunk.type === 'token' && chunk.content) {
            appendChunk(assistantMessageId, chunk.content);
            return;
          }

          if (chunk.type === 'done' && chunk.conversationId) {
            flushBufferedContent();
            setConversationId(chunk.conversationId);
            if (chunk.branchId) {
              setActiveBranchId(chunk.branchId);
            }
            if (chunk.availableBranches && chunk.availableBranches.length > 0) {
              setAvailableBranches(chunk.availableBranches);
            }

            setMessages(previous => previous.map(message =>
              message.id === assistantMessageId
                ? mergeAssistantDoneChunk(message, chunk)
                : message));
            clearPendingTurn();
          }
        }, 24, systemInstruction, activeBranchId, attachmentsToSend, liveWebMode, inputMode);
      } catch {
        flushBufferedContent();
        const chat = await sendChatTurn(userMessage.content, conversationId, 24, systemInstruction, activeBranchId, attachmentsToSend, liveWebMode, inputMode);
        setConversationId(chat.conversationId);
        if (chat.branchId) {
          setActiveBranchId(chat.branchId);
        }
        if (chat.availableBranches && chat.availableBranches.length > 0) {
          setAvailableBranches(chat.availableBranches);
        }

        setMessages(previous => previous.map(message =>
          message.id === assistantMessageId
            ? mergeAssistantDoneChunk(message, chat)
            : message));
        clearPendingTurn();
      }
    } catch (error) {
      setMessages(previous => appendSystemMessage(previous, `Error: ${error instanceof Error ? error.message : 'System failure'}.`));
    } finally {
      setStreamingMessageId(undefined);
      setProcessing(false);
    }
  }, [
    activeBranchId,
    appendChunk,
    clearProgressState,
    conversationId,
    flushBufferedContent,
    input,
    isProcessing,
    liveWebMode,
    pendingAttachments,
    responseStyle,
    setActiveBranchId,
    setAvailableBranches,
    setConversationId,
    setInput,
    setMessages,
    setPendingAttachments,
    setProcessing,
    setStreamingMessageId,
    startupState,
  ]);

  return {
    handleSendMessage,
  };
}
