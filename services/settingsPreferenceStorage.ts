export function readStoredPreference(key: string, fallback: string) {
  const value = localStorage.getItem(key);
  return value && value.trim().length > 0 ? value : fallback;
}

export function writeStoredPreference(key: string, value: string) {
  localStorage.setItem(key, value);
}

export function readActiveConversationId() {
  return localStorage.getItem('helper_conversation_id');
}
