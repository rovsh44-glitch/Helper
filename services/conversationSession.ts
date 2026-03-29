export const CONVERSATION_ID_KEY = 'helper_conversation_id';
export const RESPONSE_STYLE_KEY = 'helper_response_style';
export const PREFERRED_LANGUAGE_KEY = 'helper_preferred_language';
export const WARMTH_KEY = 'helper_style_warmth';
export const ENTHUSIASM_KEY = 'helper_style_enthusiasm';
export const DIRECTNESS_KEY = 'helper_style_directness';
export const DEFAULT_ANSWER_SHAPE_KEY = 'helper_default_answer_shape';
export const PENDING_TURN_KEY = 'helper_pending_turn';
export const PENDING_TURN_TTL_MS = 30 * 60 * 1000;

export type PendingTurnSnapshot = {
  conversationId?: string;
  message?: string;
  inputMode?: 'text' | 'voice';
  createdAt?: number;
};

export type SavedConversationStylePreferences = {
  warmth: string;
  enthusiasm: string;
  directness: string;
  defaultAnswerShape: string;
};

export function readStoredConversationId(): string | undefined {
  return localStorage.getItem(CONVERSATION_ID_KEY) || undefined;
}

export function saveConversationId(conversationId: string) {
  localStorage.setItem(CONVERSATION_ID_KEY, conversationId);
}

export function clearStoredConversationId() {
  localStorage.removeItem(CONVERSATION_ID_KEY);
}

export function getSavedResponseStyle(): string {
  return localStorage.getItem(RESPONSE_STYLE_KEY) || 'balanced';
}

export function readPreferredLanguage(): string {
  return localStorage.getItem(PREFERRED_LANGUAGE_KEY) || 'auto';
}

export function readSavedStylePreferences(): SavedConversationStylePreferences {
  return {
    warmth: localStorage.getItem(WARMTH_KEY) || 'balanced',
    enthusiasm: localStorage.getItem(ENTHUSIASM_KEY) || 'balanced',
    directness: localStorage.getItem(DIRECTNESS_KEY) || 'balanced',
    defaultAnswerShape: localStorage.getItem(DEFAULT_ANSWER_SHAPE_KEY) || 'auto',
  };
}

export function savePendingTurn(snapshot: PendingTurnSnapshot) {
  localStorage.setItem(PENDING_TURN_KEY, JSON.stringify(snapshot));
}

export function clearPendingTurn() {
  localStorage.removeItem(PENDING_TURN_KEY);
}

export function readPendingTurn(): PendingTurnSnapshot | null {
  const raw = localStorage.getItem(PENDING_TURN_KEY);
  if (!raw) {
    return null;
  }

  try {
    const snapshot = JSON.parse(raw) as PendingTurnSnapshot;
    if (!snapshot.createdAt || (Date.now() - snapshot.createdAt) > PENDING_TURN_TTL_MS) {
      clearPendingTurn();
      return null;
    }

    return snapshot;
  } catch {
    clearPendingTurn();
    return null;
  }
}
