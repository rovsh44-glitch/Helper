import { helperApi } from './generatedApiClient';
import type { ConversationRepairRequestDto } from './generatedApiClient';

export type {
  ConversationMemoryItemDto,
  ConversationRepairRequestDto,
  LiveWebMode,
} from './generatedApiClient';

export type RepairQuickActionId =
  | 'misunderstood'
  | 'shorter'
  | 'summary_first'
  | 'add_concrete';

export type RepairUiLanguage = 'ru' | 'en';

export interface RepairQuickActionPreset {
  id: RepairQuickActionId;
  label: string;
  description: string;
  correctedIntent: string;
  repairNote?: string;
}

export interface RepairConversationDraft {
  correctedIntent: string;
  repairNote: string;
  selectedActionId?: RepairQuickActionId;
}

type RepairQuickActionCatalog = Record<RepairUiLanguage, RepairQuickActionPreset[]>;

const REPAIR_QUICK_ACTIONS: RepairQuickActionCatalog = {
  ru: [
    {
      id: 'misunderstood',
      label: 'Не это имел в виду',
      description: 'Сменить трактовку запроса и перестроить ответ вокруг правильного смысла.',
      correctedIntent: 'Не это имелось в виду. Перестрой ответ вокруг правильного смысла моего запроса вместо текущей трактовки.',
      repairNote: 'Сначала исправь направление ответа, потом уже расширяй детали.',
    },
    {
      id: 'shorter',
      label: 'Сделай короче',
      description: 'Ужать ответ, убрать повторы и оставить только суть.',
      correctedIntent: 'Сделай тот же ответ короче и плотнее, без потери сути.',
      repairNote: 'Убери повторы и второстепенные детали.',
    },
    {
      id: 'summary_first',
      label: 'Сначала вывод',
      description: 'Поставить bottom line в начало, а потом дать поддержку и шаги.',
      correctedIntent: 'Перестрой ответ так, чтобы сначала шёл вывод, потом ключевые аргументы и шаги.',
      repairNote: 'Начни с краткого вывода, затем дай сжатое обоснование.',
    },
    {
      id: 'add_concrete',
      label: 'Добавь конкретику',
      description: 'Сделать ответ более исполнимым: шаги, параметры, примеры, команды.',
      correctedIntent: 'Добавь больше конкретики: точные шаги, параметры, примеры или команды там, где это уместно.',
      repairNote: 'Меньше общих формулировок, больше исполнимых деталей.',
    },
  ],
  en: [
    {
      id: 'misunderstood',
      label: 'Not What I Meant',
      description: 'Correct the interpretation and rebuild the answer around the intended meaning.',
      correctedIntent: 'That is not what I meant. Rebuild the answer around the intended meaning of my request instead of the current interpretation.',
      repairNote: 'Correct the direction first, then expand the answer.',
    },
    {
      id: 'shorter',
      label: 'Make It Shorter',
      description: 'Compress the answer and keep only the high-signal content.',
      correctedIntent: 'Make the same answer shorter and denser without losing the core point.',
      repairNote: 'Remove repetition and secondary detail.',
    },
    {
      id: 'summary_first',
      label: 'Summary First',
      description: 'Lead with the conclusion, then follow with the key support and steps.',
      correctedIntent: 'Restructure the answer so the conclusion comes first, followed by the key reasoning and steps.',
      repairNote: 'Start with the bottom line, then keep the support concise.',
    },
    {
      id: 'add_concrete',
      label: 'Add Specifics',
      description: 'Add concrete steps, parameters, examples, or commands where appropriate.',
      correctedIntent: 'Add more concrete detail: precise steps, parameters, examples, or commands where appropriate.',
      repairNote: 'Reduce generic phrasing and make the answer more executable.',
    },
  ],
};

export function resolveRepairUiLanguage(preferredLanguage: string, messageContent?: string): RepairUiLanguage {
  const normalized = preferredLanguage.trim().toLowerCase();
  if (normalized === 'ru') {
    return 'ru';
  }

  if (normalized === 'en') {
    return 'en';
  }

  return /[\u0400-\u04FF]/.test(messageContent ?? '') ? 'ru' : 'en';
}

export function getRepairQuickActionPresets(language: RepairUiLanguage): RepairQuickActionPreset[] {
  return REPAIR_QUICK_ACTIONS[language];
}

export function createRepairConversationDraft(
  language: RepairUiLanguage,
  selectedActionId?: RepairQuickActionId,
): RepairConversationDraft {
  const selectedAction = selectedActionId
    ? getRepairQuickActionPresets(language).find(action => action.id === selectedActionId)
    : undefined;

  return {
    correctedIntent: selectedAction?.correctedIntent ?? '',
    repairNote: selectedAction?.repairNote ?? '',
    selectedActionId,
  };
}

export function buildRepairConversationRequest(
  envelope: Omit<ConversationRepairRequestDto, 'correctedIntent' | 'repairNote'>,
  draft: RepairConversationDraft,
): ConversationRepairRequestDto {
  return {
    ...envelope,
    correctedIntent: draft.correctedIntent.trim(),
    repairNote: draft.repairNote.trim() || undefined,
  };
}

export async function getConversationSnapshot(conversationId: string) {
  return helperApi.getConversation(conversationId);
}

export async function getConversationReadiness() {
  return helperApi.readiness();
}

export async function resumeConversationTurn(conversationId: string, body: Parameters<typeof helperApi.resumeConversationTurn>[1]) {
  return helperApi.resumeConversationTurn(conversationId, body);
}

export async function regenerateConversationTurn(
  conversationId: string,
  turnId: string,
  body: Parameters<typeof helperApi.regenerateTurn>[2],
) {
  return helperApi.regenerateTurn(conversationId, turnId, body);
}

export async function repairConversationFlow(
  conversationId: string,
  body: Parameters<typeof helperApi.repairConversation>[1],
) {
  return helperApi.repairConversation(conversationId, body);
}

export async function getConversationMemorySnapshot(conversationId: string) {
  return helperApi.getConversationMemory(conversationId);
}

export async function setConversationPreferences(
  conversationId: string,
  body: Parameters<typeof helperApi.setConversationPreferences>[1],
) {
  return helperApi.setConversationPreferences(conversationId, body);
}

export async function deleteConversationMemoryEntry(conversationId: string, memoryId: string) {
  return helperApi.deleteConversationMemoryItem(conversationId, memoryId);
}

export async function deleteConversation(conversationId: string) {
  return helperApi.deleteConversation(conversationId);
}

export async function createConversationBranch(
  conversationId: string,
  body: Parameters<typeof helperApi.createBranch>[1],
) {
  return helperApi.createBranch(conversationId, body);
}

export async function activateConversationBranch(conversationId: string, branchId: string) {
  return helperApi.activateBranch(conversationId, branchId);
}

export async function compareConversationBranches(conversationId: string, sourceBranchId: string, targetBranchId: string) {
  return helperApi.compareBranches(conversationId, sourceBranchId, targetBranchId);
}

export async function mergeConversationBranches(
  conversationId: string,
  body: Parameters<typeof helperApi.mergeBranches>[1],
) {
  return helperApi.mergeBranches(conversationId, body);
}

export async function submitConversationFeedback(
  conversationId: string,
  body: Parameters<typeof helperApi.submitFeedback>[1],
) {
  return helperApi.submitFeedback(conversationId, body);
}
