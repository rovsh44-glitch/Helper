import { helperApi, ChatResponseDto, StreamChunk, AttachmentDto, type ConversationInputMode, type LiveWebMode } from './generatedApiClient';

export async function sendChatTurn(
  message: string,
  conversationId?: string,
  maxHistory: number = 20,
  systemInstruction?: string,
  branchId?: string,
  attachments?: AttachmentDto[],
  liveWebMode?: LiveWebMode,
  inputMode?: ConversationInputMode,
): Promise<ChatResponseDto> {
  return await helperApi.chat({ message, conversationId, maxHistory, systemInstruction, branchId, attachments, liveWebMode, inputMode });
}

export async function streamChatTurn(
  message: string,
  conversationId: string | undefined,
  onChunk: (chunk: StreamChunk) => void,
  maxHistory: number = 20,
  systemInstruction?: string,
  branchId?: string,
  attachments?: AttachmentDto[],
  liveWebMode?: LiveWebMode,
  inputMode?: ConversationInputMode,
): Promise<void> {
  await helperApi.streamChat({
    message,
    conversationId,
    maxHistory,
    systemInstruction,
    branchId,
    attachments,
    liveWebMode,
    inputMode,
  }, onChunk);
}
