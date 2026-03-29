import type { ChatAttachment, Message } from '../types';
import { toUiMessage } from './conversationUi';

type SnapshotMessage = {
  role: string;
  content: string;
  timestamp: string;
  turnId?: string;
  turnVersion?: number;
  branchId?: string;
  toolCalls?: string[];
  citations?: string[];
  sources?: string[];
  attachments?: ChatAttachment[];
  inputMode?: import('../types').ConversationInputMode;
};

export type ConversationSnapshotDto = {
  conversationId: string;
  activeBranchId?: string;
  branches?: string[];
  messages: SnapshotMessage[];
};

export function toConversationSnapshotState(snapshot: ConversationSnapshotDto) {
  const activeBranchId = snapshot.activeBranchId || 'main';
  return {
    conversationId: snapshot.conversationId,
    activeBranchId,
    availableBranches: snapshot.branches && snapshot.branches.length > 0 ? snapshot.branches : [activeBranchId],
    messages: snapshot.messages.map(toUiMessage),
  };
}

export function toConversationResponseState(snapshot: {
  conversationId: string;
  branchId?: string;
  availableBranches?: string[];
  response?: string;
  turnId?: string;
  confidence?: number;
  sources?: string[];
  toolCalls?: string[];
  requiresConfirmation?: boolean;
  nextStep?: string;
  groundingStatus?: string;
  citationCoverage?: number;
  verifiedClaims?: number;
  totalClaims?: number;
  claimGroundings?: Array<{ claim: string; type: string; sourceIndex?: number; evidenceGrade: string }>;
  uncertaintyFlags?: string[];
  executionMode?: string;
  budgetProfile?: string;
  budgetExceeded?: boolean;
  estimatedTokensGenerated?: number;
  searchTrace?: import('../services/generatedApiClient').SearchTraceDto;
  inputMode?: import('../types').ConversationInputMode;
  messages: SnapshotMessage[];
}, previousMessages: Message[] = []) {
  const activeBranchId = snapshot.branchId || 'main';
  const previousRatings = new Map<string, number>();
  previousMessages.forEach(message => {
    if (message.role === 'assistant' && message.turnId && typeof message.rating === 'number') {
      previousRatings.set(message.turnId, message.rating);
    }
  });

  return {
    conversationId: snapshot.conversationId,
    activeBranchId,
    availableBranches: snapshot.availableBranches && snapshot.availableBranches.length > 0
      ? snapshot.availableBranches
      : [activeBranchId],
    messages: snapshot.messages.map(message => {
      const enrichedMessage = message.role === 'assistant' &&
        snapshot.turnId &&
        message.turnId === snapshot.turnId
        ? {
            ...message,
            content: snapshot.response ?? message.content,
            confidence: snapshot.confidence,
            sources: snapshot.sources,
            toolCalls: snapshot.toolCalls,
            requiresConfirmation: snapshot.requiresConfirmation,
            nextStep: snapshot.nextStep,
            groundingStatus: snapshot.groundingStatus,
            citationCoverage: snapshot.citationCoverage,
            verifiedClaims: snapshot.verifiedClaims,
            totalClaims: snapshot.totalClaims,
            claimGroundings: snapshot.claimGroundings,
            uncertaintyFlags: snapshot.uncertaintyFlags,
            executionMode: snapshot.executionMode,
            budgetProfile: snapshot.budgetProfile,
            budgetExceeded: snapshot.budgetExceeded,
            estimatedTokensGenerated: snapshot.estimatedTokensGenerated,
            searchTrace: snapshot.searchTrace,
            inputMode: snapshot.inputMode,
          }
        : message;
      const uiMessage = toUiMessage(enrichedMessage);
      if (uiMessage.role !== 'assistant' || !uiMessage.turnId) {
        return uiMessage;
      }

      const preservedRating = previousRatings.get(uiMessage.turnId);
      return typeof preservedRating === 'number'
        ? { ...uiMessage, rating: preservedRating }
        : uiMessage;
    }),
  };
}
