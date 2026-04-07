import type {
  MutationProposal,
  ProgressLogEntry,
  StrategicPlan,
  ThoughtStreamEntry,
} from '../types';

export type ReasoningEventSource = 'thought' | 'progress' | 'strategy' | 'mutation';
export type ReasoningEventKind =
  | 'observe'
  | 'hypothesis'
  | 'plan'
  | 'decision'
  | 'tool_call'
  | 'verification'
  | 'uncertainty'
  | 'mutation'
  | 'safety'
  | 'reflection'
  | 'progress'
  | 'system';
export type ReasoningEventPhase = 'boot' | 'analyze' | 'plan' | 'route' | 'execute' | 'verify' | 'mutate' | 'runtime' | 'complete';
export type ReasoningEventVisibility = 'operator' | 'safe_summary';
export type ReasoningEventViewMode = 'compact' | 'detail' | 'raw';

export type ReasoningEvent = {
  id: string;
  source: ReasoningEventSource;
  kind: ReasoningEventKind;
  phase: ReasoningEventPhase;
  summary: string;
  detail?: string | null;
  timestamp: number;
  typeLabel: string;
  turnId?: string;
  branchId?: string;
  relatedTool?: string;
  relatedFile?: string;
  relatedRoute?: string;
  correlationId?: string;
  startedAtUtc?: string;
  completedAtUtc?: string;
  confidence?: number;
  visibility: ReasoningEventVisibility;
  redactionLevel: 'raw' | 'safe';
  rawPayload?: string | null;
};

type RawThoughtEnvelope = Partial<{
  content: string;
  summary: string;
  detail: string;
  timestamp: string;
  type: string;
  kind: ReasoningEventKind;
  phase: ReasoningEventPhase;
  confidence: number;
  turnId: string;
  branchId: string;
  relatedTool: string;
  relatedFile: string;
  relatedRoute: string;
  correlationId: string;
  startedAtUtc: string;
  completedAtUtc: string;
  visibility: ReasoningEventVisibility;
  redactionLevel: 'raw' | 'safe';
  metadata: Record<string, string>;
}>;

export function parseThoughtEvent(rawMessage: string): ThoughtStreamEntry {
  const fallback = createFallbackThought(rawMessage);

  try {
    const parsed = JSON.parse(rawMessage) as RawThoughtEnvelope;
    const metadata = parsed.metadata ?? {};
    const normalizedType = parsed.type ?? metadata.type ?? 'thought';
    const normalizedKind = parsed.kind ?? inferReasoningKind(parsed.summary ?? parsed.content ?? rawMessage, normalizedType);
    const normalizedPhase = parsed.phase ?? inferReasoningPhase(normalizedKind, normalizedType);

    return {
      id: crypto.randomUUID(),
      content: parsed.content || parsed.summary || rawMessage,
      summary: parsed.summary || parsed.content || rawMessage,
      detail: parsed.detail || null,
      timestamp: parsed.timestamp ? new Date(parsed.timestamp).getTime() : Date.now(),
      type: normalizedType,
      kind: normalizedKind,
      phase: normalizedPhase,
      confidence: parsed.confidence,
      turnId: parsed.turnId ?? metadata.turnId,
      branchId: parsed.branchId ?? metadata.branchId,
      relatedTool: parsed.relatedTool ?? metadata.relatedTool,
      relatedFile: parsed.relatedFile ?? metadata.relatedFile,
      relatedRoute: parsed.relatedRoute ?? metadata.relatedRoute,
      correlationId: parsed.correlationId ?? metadata.correlationId,
      startedAtUtc: parsed.startedAtUtc ?? metadata.startedAtUtc,
      completedAtUtc: parsed.completedAtUtc ?? metadata.completedAtUtc,
      visibility: parsed.visibility ?? 'operator',
      redactionLevel: parsed.redactionLevel ?? 'safe',
      rawPayload: rawMessage,
      metadata,
    };
  } catch {
    return fallback;
  }
}

export function createStrategyReasoningEvent(plan: StrategicPlan): ThoughtStreamEntry {
  return {
    id: crypto.randomUUID(),
    content: plan.reasoning,
    summary: `Route decision: ${plan.selectedStrategyId || 'strategy pending'}`,
    detail: plan.reasoning,
    timestamp: Date.now(),
    type: 'strategy',
    kind: 'plan',
    phase: 'route',
    relatedRoute: plan.selectedStrategyId,
    correlationId: plan.selectedStrategyId ? `route:${plan.selectedStrategyId}` : 'route:pending',
    startedAtUtc: new Date().toISOString(),
    completedAtUtc: new Date().toISOString(),
    visibility: 'safe_summary',
    redactionLevel: 'safe',
    rawPayload: JSON.stringify(plan),
  };
}

export function createMutationReasoningEvent(mutation: MutationProposal): ThoughtStreamEntry {
  return {
    id: crypto.randomUUID(),
    content: mutation.reason || `Mutation proposed for ${mutation.filePath}`,
    summary: `Mutation proposed: ${mutation.filePath}`,
    detail: mutation.reason || null,
    timestamp: mutation.timestamp ? new Date(mutation.timestamp).getTime() : Date.now(),
    type: 'mutation',
    kind: 'mutation',
    phase: 'mutate',
    relatedFile: mutation.filePath,
    correlationId: `mutation:${mutation.id}`,
    startedAtUtc: mutation.timestamp ?? new Date().toISOString(),
    completedAtUtc: mutation.timestamp ?? new Date().toISOString(),
    visibility: 'operator',
    redactionLevel: 'safe',
    rawPayload: JSON.stringify(mutation),
  };
}

export function buildReasoningFeed(
  thoughts: ThoughtStreamEntry[],
  progressEntries: ProgressLogEntry[],
  currentPlan: StrategicPlan | null,
  activeMutation: MutationProposal | null,
): ReasoningEvent[] {
  const fromThoughts = thoughts.map<ReasoningEvent>(thought => ({
    id: thought.id,
    source: thought.type === 'strategy' ? 'strategy' : thought.type === 'mutation' ? 'mutation' : 'thought',
    kind: thought.kind ?? inferReasoningKind(thought.summary ?? thought.content, thought.type),
    phase: thought.phase ?? inferReasoningPhase(thought.kind, thought.type),
    summary: thought.summary ?? thought.content,
    detail: thought.detail ?? null,
    timestamp: thought.timestamp,
    typeLabel: (thought.type ?? 'thought').toUpperCase(),
    turnId: thought.turnId,
    branchId: thought.branchId,
    relatedTool: thought.relatedTool,
    relatedFile: thought.relatedFile,
    relatedRoute: thought.relatedRoute,
    correlationId: thought.correlationId,
    startedAtUtc: thought.startedAtUtc,
    completedAtUtc: thought.completedAtUtc,
    confidence: thought.confidence,
    visibility: thought.visibility ?? 'operator',
    redactionLevel: thought.redactionLevel ?? 'safe',
    rawPayload: thought.rawPayload ?? null,
  }));

  const fromProgress = progressEntries.map<ReasoningEvent>(entry => ({
    id: entry.id,
    source: 'progress',
    kind: 'progress',
    phase: inferProgressPhase(entry.message),
    summary: summarizeProgressMessage(entry.message),
    detail: entry.message,
    timestamp: entry.timestamp,
    typeLabel: 'BUS',
    visibility: 'operator',
    redactionLevel: 'safe',
    rawPayload: entry.message,
  }));

  const synthesized: ReasoningEvent[] = [];
  if (currentPlan && !fromThoughts.some(entry => entry.source === 'strategy' && entry.relatedRoute === currentPlan.selectedStrategyId)) {
    synthesized.push({
      id: `strategy-${currentPlan.selectedStrategyId || 'current'}`,
      source: 'strategy',
      kind: 'decision',
      phase: 'route',
      summary: `Current route: ${currentPlan.selectedStrategyId || 'not selected'}`,
      detail: currentPlan.reasoning,
      timestamp: Date.now(),
      typeLabel: 'STRATEGY',
      relatedRoute: currentPlan.selectedStrategyId,
      correlationId: currentPlan.selectedStrategyId ? `route:${currentPlan.selectedStrategyId}` : 'route:current',
      startedAtUtc: new Date().toISOString(),
      completedAtUtc: new Date().toISOString(),
      visibility: 'safe_summary',
      redactionLevel: 'safe',
      rawPayload: JSON.stringify(currentPlan),
    });
  }

  if (activeMutation && !fromThoughts.some(entry => entry.source === 'mutation' && entry.relatedFile === activeMutation.filePath)) {
    synthesized.push({
      id: `mutation-${activeMutation.id}`,
      source: 'mutation',
      kind: 'mutation',
      phase: 'mutate',
      summary: `Pending mutation: ${activeMutation.filePath}`,
      detail: activeMutation.reason ?? null,
      timestamp: activeMutation.timestamp ? new Date(activeMutation.timestamp).getTime() : Date.now(),
      typeLabel: 'MUTATION',
      relatedFile: activeMutation.filePath,
      correlationId: `mutation:${activeMutation.id}`,
      startedAtUtc: activeMutation.timestamp ?? new Date().toISOString(),
      completedAtUtc: activeMutation.timestamp ?? new Date().toISOString(),
      visibility: 'operator',
      redactionLevel: 'safe',
      rawPayload: JSON.stringify(activeMutation),
    });
  }

  return [...synthesized, ...fromThoughts, ...fromProgress]
    .sort((left, right) => right.timestamp - left.timestamp);
}

function createFallbackThought(rawMessage: string): ThoughtStreamEntry {
  return {
    id: crypto.randomUUID(),
    content: rawMessage,
    summary: rawMessage,
    detail: null,
    timestamp: Date.now(),
    type: 'thought',
    kind: inferReasoningKind(rawMessage),
    phase: 'runtime',
    startedAtUtc: new Date().toISOString(),
    completedAtUtc: new Date().toISOString(),
    visibility: 'operator',
    redactionLevel: 'safe',
    rawPayload: rawMessage,
  };
}

function inferReasoningKind(message: string, type?: string): ReasoningEventKind {
  const text = `${type ?? ''} ${message}`.toLowerCase();
  if (text.includes('verify') || text.includes('validation')) {
    return 'verification';
  }
  if (text.includes('mutation')) {
    return 'mutation';
  }
  if (text.includes('plan') || text.includes('strategy') || text.includes('route')) {
    return 'plan';
  }
  if (text.includes('warn') || text.includes('guard') || text.includes('blocked')) {
    return 'safety';
  }
  if (text.includes('uncertain') || text.includes('unknown') || text.includes('maybe')) {
    return 'uncertainty';
  }
  if (text.includes('tool')) {
    return 'tool_call';
  }
  if (text.includes('decide') || text.includes('selected')) {
    return 'decision';
  }
  if (text.includes('reflect')) {
    return 'reflection';
  }

  return type === 'progress' ? 'progress' : 'observe';
}

function inferReasoningPhase(kind?: ReasoningEventKind, type?: string): ReasoningEventPhase {
  switch (kind) {
    case 'plan':
    case 'decision':
      return 'route';
    case 'tool_call':
      return 'execute';
    case 'verification':
      return 'verify';
    case 'mutation':
      return 'mutate';
    case 'safety':
      return 'runtime';
    case 'reflection':
      return 'complete';
    default:
      return type === 'prometheus' ? 'runtime' : 'analyze';
  }
}

function inferProgressPhase(message: string): ReasoningEventPhase {
  const text = message.toLowerCase();
  if (text.includes('mutation')) {
    return 'mutate';
  }
  if (text.includes('strategy') || text.includes('route')) {
    return 'route';
  }
  if (text.includes('verify') || text.includes('audit')) {
    return 'verify';
  }
  if (text.includes('build') || text.includes('execute')) {
    return 'execute';
  }
  return 'runtime';
}

function summarizeProgressMessage(message: string) {
  return message.length > 120 ? `${message.slice(0, 117)}...` : message;
}
