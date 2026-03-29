import React, { memo } from 'react';
import type { Message } from '../../types';
import type { RepairConversationDraft } from '../../services/conversationApi';
import { OrchestratorMessageCard } from './OrchestratorMessageCard';

interface MessageListProps {
  messages: Message[];
  isProcessing: boolean;
  streamingMessageId?: string;
  onRegenerate: (turnId: string) => void;
  onCreateBranch: (turnId: string) => void;
  onRepairTurn: (turnId: string, draft: RepairConversationDraft) => Promise<boolean>;
  onRateMessage: (turnId: string | undefined, rating: number) => void;
}

export const MessageList = memo(function MessageList({
  messages,
  isProcessing,
  streamingMessageId,
  onRegenerate,
  onCreateBranch,
  onRepairTurn,
  onRateMessage,
}: MessageListProps) {
  return (
    <div className="space-y-5">
      {messages.map(message => (
        <OrchestratorMessageCard
          key={message.id}
          message={message}
          isStreaming={isProcessing && message.id === streamingMessageId}
          isBusy={isProcessing}
          onRegenerate={onRegenerate}
          onCreateBranch={onCreateBranch}
          onRepairTurn={onRepairTurn}
          onRateMessage={onRateMessage}
        />
      ))}
    </div>
  );
});
