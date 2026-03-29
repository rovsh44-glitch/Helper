import { type ChatAttachment, type Message, type MessageDiagnosticsDeck, type MessageDiagnosticsItem, type MessageDiagnosticsSection } from '../types';
import type { ChatResponseDto, ClaimGroundingDto, SearchTraceDto, StreamChunk } from '../services/generatedApiClient';
import type { SavedConversationStylePreferences } from '../services/conversationSession';

type MessageLike = {
  role: string;
  content: string;
  timestamp: string;
  turnId?: string;
  turnVersion?: number;
  branchId?: string;
  confidence?: number;
  toolCalls?: string[];
  citations?: string[];
  sources?: string[];
  attachments?: ChatAttachment[];
  requiresConfirmation?: boolean;
  nextStep?: string;
  groundingStatus?: string;
  citationCoverage?: number;
  verifiedClaims?: number;
  totalClaims?: number;
  claimGroundings?: ClaimGroundingDto[];
  uncertaintyFlags?: string[];
  executionMode?: string;
  budgetProfile?: string;
  budgetExceeded?: boolean;
  estimatedTokensGenerated?: number;
  availableBranches?: string[];
  searchTrace?: SearchTraceDto;
  inputMode?: Message['inputMode'];
};

export function getSystemInstruction(
  style: string,
  preferredLanguage: string,
  stylePreferences?: SavedConversationStylePreferences,
): string {
  const languageHint = preferredLanguage === 'auto' ? '' : ` Use ${preferredLanguage.toUpperCase()} language by default.`;
  const warmth = stylePreferences?.warmth ?? 'balanced';
  const enthusiasm = stylePreferences?.enthusiasm ?? 'balanced';
  const directness = stylePreferences?.directness ?? 'balanced';
  const defaultAnswerShape = stylePreferences?.defaultAnswerShape ?? 'auto';
  const styleHint = [
    warmth === 'warm' ? 'Keep the tone gently warm.' : warmth === 'cool' ? 'Keep the tone composed and restrained.' : '',
    enthusiasm === 'high' ? 'Show a bit more energy when useful.' : enthusiasm === 'low' ? 'Keep the energy level muted and calm.' : '',
    directness === 'direct' ? 'Use direct phrasing and shorter lead-ins.' : directness === 'soft' ? 'Use gentler transitions and avoid abrupt wording.' : '',
    defaultAnswerShape === 'paragraph' ? 'Prefer paragraph form unless structure is necessary.' : defaultAnswerShape === 'bullets' ? 'Prefer concise bullet points when the content naturally supports them.' : '',
    'Do not default to lists or tables unless the task clearly benefits from structure.',
  ].filter(Boolean).join(' ');

  switch (style) {
    case 'concise':
      return `Respond briefly and directly. Prefer a short paragraph unless bullets make the answer clearer.${languageHint} ${styleHint}`.trim();
    case 'detailed':
      return `Respond with detailed explanations, clear reasoning, and explicit assumptions. Use lists only when they improve clarity.${languageHint} ${styleHint}`.trim();
    default:
      return `Respond clearly and pragmatically, balancing brevity and depth. Use structure only when it improves readability.${languageHint} ${styleHint}`.trim();
  }
}

export function toUiMessage(message: MessageLike): Message {
  const uiMessage: Message = {
    id: crypto.randomUUID(),
    role: message.role as 'user' | 'assistant' | 'system',
    content: message.content,
    timestamp: new Date(message.timestamp).getTime(),
    turnId: message.turnId,
    turnVersion: message.turnVersion,
    branchId: message.branchId,
    confidence: message.confidence,
    toolCalls: message.toolCalls,
    sources: message.citations ?? message.sources,
    attachments: message.attachments,
    requiresConfirmation: message.requiresConfirmation,
    nextStep: message.nextStep,
    groundingStatus: message.groundingStatus,
    citationCoverage: message.citationCoverage,
    verifiedClaims: message.verifiedClaims,
    totalClaims: message.totalClaims,
    claimGroundings: message.claimGroundings,
    uncertaintyFlags: message.uncertaintyFlags,
    executionMode: message.executionMode,
    budgetProfile: message.budgetProfile,
    budgetExceeded: message.budgetExceeded,
    estimatedTokensGenerated: message.estimatedTokensGenerated,
    availableBranches: message.availableBranches,
    searchTrace: message.searchTrace,
    inputMode: message.inputMode ?? message.searchTrace?.inputMode,
  };

  return {
    ...uiMessage,
    diagnosticsDeck: buildDiagnosticsDeck(uiMessage),
  };
}

export function mergeAssistantDoneChunk(message: Message, chunk: StreamChunk | ChatResponseDto): Message {
  const sources = 'sources' in chunk ? chunk.sources : undefined;
  const availableBranches = 'availableBranches' in chunk ? chunk.availableBranches : undefined;

  const merged: Message = {
    ...message,
    content: ('fullResponse' in chunk ? chunk.fullResponse : undefined) || ('response' in chunk ? chunk.response : undefined) || message.content || '',
    turnId: chunk.turnId,
    confidence: chunk.confidence,
    sources,
    toolCalls: chunk.toolCalls,
    requiresConfirmation: chunk.requiresConfirmation,
    nextStep: chunk.nextStep,
    groundingStatus: chunk.groundingStatus,
    citationCoverage: chunk.citationCoverage,
    verifiedClaims: chunk.verifiedClaims,
    totalClaims: chunk.totalClaims,
    claimGroundings: chunk.claimGroundings,
    uncertaintyFlags: chunk.uncertaintyFlags,
    executionMode: chunk.executionMode,
    budgetProfile: chunk.budgetProfile,
    budgetExceeded: chunk.budgetExceeded,
    estimatedTokensGenerated: chunk.estimatedTokensGenerated,
    branchId: chunk.branchId,
    availableBranches,
    searchTrace: chunk.searchTrace,
    inputMode: chunk.inputMode ?? chunk.searchTrace?.inputMode ?? message.inputMode,
  };

  return {
    ...merged,
    diagnosticsDeck: buildDiagnosticsDeck(merged),
  };
}

export function appendSystemMessage(previous: Message[], content: string): Message[] {
  return [
    ...previous,
    {
      id: crypto.randomUUID(),
      role: 'system',
      content,
      timestamp: Date.now(),
    },
  ];
}

export function shouldRenderSupplementalNextStep(message: Message): boolean {
  if (message.role !== 'assistant' || !message.nextStep) {
    return false;
  }

  const normalizedNextStep = normalizeForComparison(message.nextStep);
  if (!normalizedNextStep) {
    return false;
  }

  return !normalizeForComparison(message.content).includes(normalizedNextStep);
}

function buildDiagnosticsDeck(message: Message): MessageDiagnosticsDeck | undefined {
  if (message.role !== 'assistant') {
    return undefined;
  }

  const badges: MessageDiagnosticsItem[] = [];
  const sections: MessageDiagnosticsSection[] = [];

  const confidenceBadge = buildConfidenceBadge(message.confidence);
  if (confidenceBadge) {
    badges.push(confidenceBadge);
  }

  const toolingBadge = buildToolingBadge(message.toolCalls);
  if (toolingBadge) {
    badges.push(toolingBadge);
  }

  const groundingBadge = buildGroundingBadge(message.sources, message.citationCoverage, message.groundingStatus);
  if (groundingBadge) {
    badges.push(groundingBadge);
  }

  const flagsBadge = buildFlagsBadge(message);
  if (flagsBadge) {
    badges.push(flagsBadge);
  }

  const reliabilityItems: MessageDiagnosticsItem[] = [];
  if (typeof message.confidence === 'number') {
    reliabilityItems.push({
      label: 'Confidence',
      value: formatConfidenceLabel(message.confidence),
      tone: confidenceTone(message.confidence),
    });
  }

  if (message.groundingStatus) {
    reliabilityItems.push({
      label: 'Grounding',
      value: formatGroundingStatus(message.groundingStatus),
      tone: message.sources && message.sources.length > 0 ? 'info' : 'warning',
    });
  }

  if (message.sources && message.sources.length > 0) {
    reliabilityItems.push({
      label: 'Sources',
      value: `${message.sources.length} source(s)`,
      tone: 'info',
    });
  }

  if (typeof message.citationCoverage === 'number') {
    reliabilityItems.push({
      label: 'Coverage',
      value: `${Math.round(message.citationCoverage * 100)}%`,
      tone: message.citationCoverage >= 0.7 ? 'positive' : message.citationCoverage >= 0.4 ? 'info' : 'warning',
    });
  }

  if (typeof message.verifiedClaims === 'number' || typeof message.totalClaims === 'number') {
    const verified = message.verifiedClaims ?? 0;
    const total = message.totalClaims ?? message.claimGroundings?.length ?? 0;
    reliabilityItems.push({
      label: 'Claim evidence',
      value: `${verified}/${total} verified`,
      tone: verified > 0 ? 'info' : 'warning',
    });
  }

  if (reliabilityItems.length > 0) {
    sections.push({
      id: 'reliability',
      title: 'Reliability',
      layout: 'grid',
      items: reliabilityItems,
    });
  }

  const executionItems: MessageDiagnosticsItem[] = [];
  if (message.searchTrace) {
    executionItems.push({
      label: 'Live web',
      value: formatSearchTraceStatus(message.searchTrace),
      tone: message.searchTrace.status === 'executed_live_web' || message.searchTrace.status === 'used_cached_web_result'
        ? 'positive'
        : message.searchTrace.status === 'disabled_by_user'
          ? 'neutral'
          : 'info',
    });
  }

  if (message.toolCalls && message.toolCalls.length > 0) {
    executionItems.push({
      label: 'Tool calls',
      value: message.toolCalls.join(', '),
      tone: 'positive',
      mono: true,
    });
  }

  if (message.executionMode) {
    executionItems.push({
      label: 'Execution mode',
      value: message.executionMode,
      tone: 'neutral',
      mono: true,
    });
  }

  if (message.budgetProfile) {
    executionItems.push({
      label: 'Budget profile',
      value: message.budgetProfile,
      tone: 'neutral',
      mono: true,
    });
  }

  if (typeof message.estimatedTokensGenerated === 'number') {
    executionItems.push({
      label: 'Generated tokens',
      value: `${message.estimatedTokensGenerated}`,
      tone: 'neutral',
      mono: true,
    });
  }

  if (message.inputMode) {
    executionItems.push({
      label: 'Channel',
      value: message.inputMode,
      tone: message.inputMode === 'voice' ? 'info' : 'neutral',
      mono: true,
    });
  }

  if (executionItems.length > 0) {
    sections.push({
      id: 'execution',
      title: 'Execution',
      layout: 'grid',
      items: executionItems,
    });
  }

  const flagItems: MessageDiagnosticsItem[] = [];
  if (message.requiresConfirmation) {
    flagItems.push({
      label: 'Safety',
      value: 'Confirmation required before risky action',
      tone: 'critical',
    });
  }

  if (message.budgetExceeded) {
    flagItems.push({
      label: 'Budget',
      value: 'Turn budget was exceeded',
      tone: 'warning',
    });
  }

  if (message.uncertaintyFlags && message.uncertaintyFlags.length > 0) {
    message.uncertaintyFlags.forEach(flag => {
      flagItems.push({
        label: 'Flag',
        value: flag,
        tone: 'warning',
        mono: true,
      });
    });
  }

  if (flagItems.length > 0) {
    sections.push({
      id: 'flags',
      title: 'Flags',
      layout: 'list',
      items: flagItems,
    });
  }

  if (message.claimGroundings && message.claimGroundings.length > 0) {
    sections.push({
      id: 'evidence',
      title: 'Evidence preview',
      layout: 'list',
      items: message.claimGroundings.slice(0, 6).map(claim => ({
        label: buildClaimLabel(claim),
        value: claim.claim,
        tone: evidenceTone(claim.evidenceGrade),
      })),
    });
  }

  if (badges.length === 0 && sections.length === 0) {
    return undefined;
  }

  return {
    title: 'Response diagnostics',
    badges,
    sections,
  };
}

function buildConfidenceBadge(confidence?: number): MessageDiagnosticsItem | undefined {
  if (typeof confidence !== 'number') {
    return undefined;
  }

  return {
    label: 'Confidence',
    value: formatConfidenceLabel(confidence),
    tone: confidenceTone(confidence),
  };
}

function buildToolingBadge(toolCalls?: string[]): MessageDiagnosticsItem | undefined {
  if (!toolCalls || toolCalls.length === 0) {
    return undefined;
  }

  return {
    label: 'Tooling',
    value: `${toolCalls.length} call(s)`,
    tone: 'positive',
  };
}

function formatSearchTraceStatus(trace: NonNullable<Message['searchTrace']>): string {
  if (trace.status === 'executed_live_web') {
    return `live (${formatModeLabel(trace.requestedMode)})`;
  }

  if (trace.status === 'used_cached_web_result') {
    return `cache (${formatModeLabel(trace.requestedMode)})`;
  }

  if (trace.status === 'disabled_by_user') {
    return 'off';
  }

  return `${formatRequirementLabel(trace.resolvedRequirement)} (${formatModeLabel(trace.requestedMode)})`;
}

function formatModeLabel(mode: NonNullable<Message['searchTrace']>['requestedMode']): string {
  switch (mode) {
    case 'force_search':
      return 'forced';
    case 'no_web':
      return 'off';
    default:
      return 'auto';
  }
}

function formatRequirementLabel(requirement: NonNullable<Message['searchTrace']>['resolvedRequirement']): string {
  switch (requirement) {
    case 'web_required':
      return 'required';
    case 'web_helpful':
      return 'helpful';
    default:
      return 'local';
  }
}

function buildGroundingBadge(sources?: string[], citationCoverage?: number, groundingStatus?: string): MessageDiagnosticsItem | undefined {
  if (sources && sources.length > 0) {
    return {
      label: 'Grounding',
      value: `${sources.length} source(s)`,
      tone: 'info',
    };
  }

  if (typeof citationCoverage === 'number') {
    return {
      label: 'Grounding',
      value: `${Math.round(citationCoverage * 100)}% coverage`,
      tone: citationCoverage >= 0.4 ? 'info' : 'warning',
    };
  }

  if (!groundingStatus) {
    return undefined;
  }

  return {
    label: 'Grounding',
    value: formatGroundingStatus(groundingStatus),
    tone: 'warning',
  };
}

function buildFlagsBadge(message: Message): MessageDiagnosticsItem | undefined {
  const flagCount = (message.uncertaintyFlags?.length ?? 0) +
    (message.requiresConfirmation ? 1 : 0) +
    (message.budgetExceeded ? 1 : 0);

  if (flagCount === 0) {
    return undefined;
  }

  return {
    label: 'Flags',
    value: `${flagCount}`,
    tone: message.requiresConfirmation ? 'critical' : 'warning',
  };
}

function buildClaimLabel(claim: ClaimGroundingDto): string {
  const grade = (claim.evidenceGrade || 'unknown').toUpperCase();
  const reference = claim.sourceIndex ? ` [${claim.sourceIndex}]` : ' [unverified]';
  return `${grade}${reference}`;
}

function formatConfidenceLabel(confidence: number): string {
  if (confidence >= 0.8) return 'high';
  if (confidence >= 0.6) return 'medium';
  return 'low';
}

function confidenceTone(confidence: number): MessageDiagnosticsItem['tone'] {
  if (confidence >= 0.8) return 'positive';
  if (confidence >= 0.6) return 'warning';
  return 'critical';
}

function evidenceTone(grade?: string): MessageDiagnosticsItem['tone'] {
  const normalized = (grade || '').toLowerCase();
  if (normalized === 'strong') return 'positive';
  if (normalized === 'medium') return 'info';
  if (normalized === 'weak') return 'warning';
  return 'neutral';
}

function formatGroundingStatus(status: string): string {
  return status
    .replace(/_/g, ' ')
    .replace(/\b\w/g, letter => letter.toUpperCase());
}

function normalizeForComparison(value?: string): string {
  if (!value) {
    return '';
  }

  return value
    .toLowerCase()
    .replace(/\s+/g, ' ')
    .trim();
}
