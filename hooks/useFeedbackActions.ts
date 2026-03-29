import { useCallback } from 'react';
import { useConversationActions, useConversationRuntimeState } from '../contexts/ConversationStateContext';
import { submitConversationFeedback } from '../services/conversationApi';

export function useFeedbackActions() {
  const { conversationId } = useConversationRuntimeState();
  const { setMessages } = useConversationActions();

  const handleRateMessage = useCallback(async (turnId: string | undefined, rating: number) => {
    if (!conversationId || !turnId) {
      return;
    }

    try {
      await submitConversationFeedback(conversationId, {
        turnId,
        rating,
        tags: ['ui_quick_rating'],
      });
      setMessages(previous => previous.map(message =>
        message.turnId === turnId && message.role === 'assistant'
          ? { ...message, rating }
          : message));
    } catch {
    }
  }, [conversationId, setMessages]);

  return {
    handleRateMessage,
  };
}
