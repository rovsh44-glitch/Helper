import type {
  ControlPlaneSnapshotDto,
  RuntimeLogEntryDto,
  RuntimeLogSourceDto,
} from '../services/api/runtimeApi';
import {
  normalizeRuntimeDomain,
  normalizeRuntimeScope,
  type UiRuntimeDomain,
  type UiRuntimeScope,
} from '../services/runtimeTelemetry';

export type RuntimeSeverity = 'error' | 'warn' | 'info' | 'debug' | 'neutral';
export type SeverityFilter = 'all' | RuntimeSeverity;
export type RuntimeScope = UiRuntimeScope;
export type ScopeFilter = 'all' | RuntimeScope;
export type RuntimeDomain = UiRuntimeDomain;
export type DomainFilter = 'all' | RuntimeDomain;

export type FilterOption<T extends string> = {
  value: T;
  label: string;
  count: number;
};

export type DerivedLogEntry = {
  raw: RuntimeLogEntryDto;
  sourceLabel: string;
  sourcePath: string;
  sourceTag: string;
  severity: RuntimeSeverity;
  scope: RuntimeScope;
  domain: RuntimeDomain;
  summary: string;
  message: string;
  searchable: string;
  markers: string[];
  details: Array<{ label: string; value: string; tone: RuntimeSeverity }>;
  statusCode?: string;
  durationMs?: number;
  correlationId?: string;
  route?: string;
  operationKind?: string;
  degradationReason?: string;
  latencyBucket?: string;
  structured: boolean;
  hasJson: boolean;
  isStackFrame: boolean;
};

export type RuntimeIntelligence = {
  total: number;
  severity: Record<RuntimeSeverity, number>;
  scope: Record<RuntimeScope, number>;
  domain: Record<RuntimeDomain, number>;
  dominantScope: RuntimeScope;
  dominantDomain: RuntimeDomain;
  avgLatencyMs?: number;
  correlationCount: number;
  stackCount: number;
  jsonCount: number;
  httpFaults: number;
  markers: Array<{ label: string; count: number }>;
};

export type BootStage = {
  id: string;
  label: string;
  value?: string;
  reached: boolean;
  offsetMs?: number;
  segmentMs?: number;
  dotClass: string;
  toneClass: string;
  cardClass: string;
};

export const SEVERITY_LABELS: Record<SeverityFilter, string> = {
  all: 'All',
  error: 'Error',
  warn: 'Warn',
  info: 'Info',
  debug: 'Debug',
  neutral: 'Neutral',
};

export const SCOPE_LABELS: Record<ScopeFilter, string> = {
  all: 'All scopes',
  boot: 'Boot',
  control: 'Control',
  api: 'API',
  model: 'Model',
  storage: 'Storage',
  security: 'Security',
  bus: 'Bus',
  network: 'Network',
  exception: 'Exception',
  misc: 'Misc',
};

export const DOMAIN_LABELS: Record<DomainFilter, string> = {
  all: 'All domains',
  readiness: 'Readiness',
  gateway: 'Gateway',
  persistence: 'Persistence',
  auth: 'Auth',
  generation: 'Generation',
  telemetry: 'Telemetry',
  transport: 'Transport',
  runtime: 'Runtime',
  unknown: 'Unknown',
};

export function deriveLogEntry(entry: RuntimeLogEntryDto, source?: RuntimeLogSourceDto): DerivedLogEntry {
  const message = entry.text || '';
  const sourceLabel = source?.label ?? 'Runtime log';
  const sourcePath = source?.displayPath ?? entry.sourceId;
  const sourceTag = buildSourceTag(sourcePath);
  const severity = normalizeSeverity(entry.severity, message);
  const isStackFrame = entry.isContinuation || /^\s*at\s+/i.test(message) || /\b(inner exception|stack trace|traceback)\b/i.test(message);
  const scope = normalizeRuntimeScope(entry.semantics?.scope, isStackFrame ? 'exception' : 'misc');
  const domain = normalizeRuntimeDomain(entry.semantics?.domain, scope === 'exception' ? 'runtime' : 'unknown');
  const durationMs = entry.semantics?.latencyMs ?? extractDurationMs(message);
  const statusCode = extractStatusCode(message);
  const correlationId = entry.semantics?.correlationId ?? extractCorrelationId(message);
  const route = entry.semantics?.route ?? extractRoute(message);
  const jsonKeys = extractJsonKeys(message);
  const summary = entry.semantics?.summary?.trim() || buildSummary(message, isStackFrame);
  const operationKind = entry.semantics?.operationKind?.trim() || undefined;
  const degradationReason = entry.semantics?.degradationReason?.trim() || undefined;
  const latencyBucket = entry.semantics?.latencyBucket?.trim() || undefined;
  const structured = entry.semantics?.structured ?? false;
  const markers = buildMarkers({
    statusCode,
    durationMs,
    correlationId,
    route,
    jsonKeys,
    isStackFrame,
    isContinuation: entry.isContinuation,
    operationKind,
    degradationReason,
    latencyBucket,
    structured,
    semanticsMarkers: entry.semantics?.markers ?? [],
  });
  const details = buildDetails({
    statusCode,
    durationMs,
    correlationId,
    route,
    jsonKeys,
    isStackFrame,
    isContinuation: entry.isContinuation,
    operationKind,
    degradationReason,
    latencyBucket,
    structured,
  });

  return {
    raw: entry,
    sourceLabel,
    sourcePath,
    sourceTag,
    severity,
    scope,
    domain,
    summary,
    message,
    searchable: [
      message,
      summary,
      sourceLabel,
      sourcePath,
      sourceTag,
      scope,
      domain,
      markers.join(' '),
      details.map(detail => `${detail.label} ${detail.value}`).join(' '),
    ].join(' ').toLowerCase(),
    markers,
    details,
    statusCode,
    durationMs,
    correlationId,
    route,
    operationKind,
    degradationReason,
    latencyBucket,
    structured,
    hasJson: jsonKeys.length > 0,
    isStackFrame,
  };
}

export function buildSeverityOptions(entries: DerivedLogEntry[]): FilterOption<SeverityFilter>[] {
  const counts = createSeverityCounts();
  entries.forEach(entry => { counts[entry.severity] += 1; });
  return (['all', 'error', 'warn', 'info', 'debug', 'neutral'] as const)
    .filter(value => value === 'all' || counts[value] > 0)
    .map(value => ({ value, label: SEVERITY_LABELS[value], count: value === 'all' ? entries.length : counts[value] }));
}

export function buildScopeOptions(entries: DerivedLogEntry[]): FilterOption<ScopeFilter>[] {
  const counts = createScopeCounts();
  entries.forEach(entry => { counts[entry.scope] += 1; });
  return (['all', 'boot', 'control', 'api', 'model', 'storage', 'security', 'bus', 'network', 'exception', 'misc'] as const)
    .filter(value => value === 'all' || counts[value] > 0)
    .map(value => ({ value, label: SCOPE_LABELS[value], count: value === 'all' ? entries.length : counts[value] }));
}

export function buildDomainOptions(entries: DerivedLogEntry[]): FilterOption<DomainFilter>[] {
  const counts = createDomainCounts();
  entries.forEach(entry => { counts[entry.domain] += 1; });
  return (['all', 'readiness', 'gateway', 'persistence', 'auth', 'generation', 'telemetry', 'transport', 'runtime', 'unknown'] as const)
    .filter(value => value === 'all' || counts[value] > 0)
    .map(value => ({ value, label: DOMAIN_LABELS[value], count: value === 'all' ? entries.length : counts[value] }));
}

export function buildIntelligence(entries: DerivedLogEntry[]): RuntimeIntelligence {
  const severity = createSeverityCounts();
  const scope = createScopeCounts();
  const domain = createDomainCounts();
  const correlations = new Set<string>();
  const markers = new Map<string, number>();
  const latencies: number[] = [];
  let stackCount = 0;
  let jsonCount = 0;
  let httpFaults = 0;

  for (const entry of entries) {
    severity[entry.severity] += 1;
    scope[entry.scope] += 1;
    domain[entry.domain] += 1;
    if (entry.correlationId) correlations.add(entry.correlationId);
    if (entry.durationMs !== undefined) latencies.push(entry.durationMs);
    if (entry.isStackFrame) stackCount += 1;
    if (entry.hasJson) jsonCount += 1;
    if (entry.statusCode && (entry.statusCode.startsWith('4') || entry.statusCode.startsWith('5'))) httpFaults += 1;
    entry.markers
      .filter(marker => !marker.startsWith('corr '))
      .forEach(marker => markers.set(marker, (markers.get(marker) ?? 0) + 1));
  }

  return {
    total: entries.length,
    severity,
    scope,
    domain,
    dominantScope: pickTopScope(scope),
    dominantDomain: pickTopDomain(domain),
    avgLatencyMs: latencies.length > 0 ? latencies.reduce((sum, value) => sum + value, 0) / latencies.length : undefined,
    correlationCount: correlations.size,
    stackCount,
    jsonCount,
    httpFaults,
    markers: Array.from(markers.entries())
      .sort((left, right) => right[1] - left[1])
      .slice(0, 6)
      .map(([label, count]) => ({ label, count })),
  };
}

export function buildBootStages(controlPlane: ControlPlaneSnapshotDto | null): BootStage[] {
  const readiness = controlPlane?.readiness;
  const startedMs = toEpochMs(readiness?.startedAtUtc);
  let previousMs: number | undefined;

  return [
    { id: 'started', label: 'Process start', value: readiness?.startedAtUtc, dotClass: 'border-cyan-400/30 bg-cyan-400/30 shadow-[0_0_0_6px_rgba(34,211,238,0.10)]', toneClass: 'border-cyan-400/30 bg-cyan-500/5 text-cyan-100', cardClass: 'border-cyan-400/30 bg-cyan-500/5' },
    { id: 'listening', label: 'Socket bind', value: readiness?.listeningAtUtc, dotClass: 'border-sky-400/30 bg-sky-400/30 shadow-[0_0_0_6px_rgba(56,189,248,0.10)]', toneClass: 'border-sky-400/30 bg-sky-500/5 text-sky-100', cardClass: 'border-sky-400/30 bg-sky-500/5' },
    { id: 'minimal', label: 'Minimal ready', value: readiness?.minimalReadyAtUtc, dotClass: 'border-emerald-400/30 bg-emerald-400/30 shadow-[0_0_0_6px_rgba(52,211,153,0.10)]', toneClass: 'border-emerald-400/30 bg-emerald-500/5 text-emerald-100', cardClass: 'border-emerald-400/30 bg-emerald-500/5' },
    { id: 'warm', label: 'Warm ready', value: readiness?.warmReadyAtUtc, dotClass: 'border-violet-400/30 bg-violet-400/30 shadow-[0_0_0_6px_rgba(167,139,250,0.10)]', toneClass: 'border-violet-400/30 bg-violet-500/5 text-violet-100', cardClass: 'border-violet-400/30 bg-violet-500/5' },
  ].map(stage => {
    const currentMs = toEpochMs(stage.value);
    const reached = currentMs !== undefined;
    const mapped: BootStage = {
      ...stage,
      reached,
      offsetMs: reached && startedMs !== undefined ? currentMs - startedMs : undefined,
      segmentMs: reached && previousMs !== undefined ? currentMs - previousMs : undefined,
    };
    if (currentMs !== undefined) {
      previousMs = currentMs;
    }
    return mapped;
  });
}

export function tokenizeSearch(value: string) {
  return value
    .trim()
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean)
    .filter((token, index, list) => list.indexOf(token) === index)
    .sort((left, right) => right.length - left.length)
    .slice(0, 6);
}

export function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export function truncateMiddle(value: string, maxLength: number) {
  if (value.length <= maxLength) return value;
  const head = Math.ceil((maxLength - 3) / 2);
  const tail = Math.floor((maxLength - 3) / 2);
  return `${value.slice(0, head)}...${value.slice(value.length - tail)}`;
}

export function formatBytes(sizeBytes: number) {
  if (!Number.isFinite(sizeBytes) || sizeBytes <= 0) {
    return '0 B';
  }

  const units = ['B', 'KB', 'MB', 'GB'];
  let value = sizeBytes;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  return `${value >= 10 || unitIndex === 0 ? value.toFixed(0) : value.toFixed(1)} ${units[unitIndex]}`;
}

export function formatDuration(durationMs?: number) {
  if (!durationMs || durationMs <= 0) {
    return 'n/a';
  }

  if (durationMs < 1000) {
    return `${Math.round(durationMs)}ms`;
  }

  return `${(durationMs / 1000).toFixed(durationMs >= 10_000 ? 0 : 1)}s`;
}

export function formatPreciseDuration(durationMs?: number) {
  if (durationMs === undefined || Number.isNaN(durationMs)) {
    return 'n/a';
  }

  if (Math.abs(durationMs) < 1000) {
    return `${Math.round(durationMs)}ms`;
  }

  return `${(durationMs / 1000).toFixed(Math.abs(durationMs) >= 10_000 ? 0 : 1)}s`;
}

export function formatTimelineOffset(durationMs?: number) {
  if (durationMs === undefined) {
    return 'n/a';
  }

  return `t+${formatPreciseDuration(durationMs)}`;
}

export function formatDateTime(value?: string) {
  if (!value) {
    return 'n/a';
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return parsed.toLocaleString();
}

export function formatFeedTime(timestamp: number) {
  return new Date(timestamp).toLocaleTimeString();
}

export function formatAge(value?: string) {
  if (!value) {
    return 'n/a';
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return 'n/a';
  }

  const deltaMs = Date.now() - parsed.getTime();
  if (deltaMs < 60_000) {
    return `${Math.max(1, Math.round(deltaMs / 1000))}s`;
  }

  if (deltaMs < 3_600_000) {
    return `${Math.round(deltaMs / 60_000)}m`;
  }

  return `${Math.round(deltaMs / 3_600_000)}h`;
}

export function mapRuntimeApiError(error: unknown): string {
  if (!(error instanceof Error)) {
    return 'Runtime console failed to reach Helper backend.';
  }

  const message = error.message || '';
  if (message.includes('API 403')) {
    return message.toLowerCase().includes('missing scope')
      ? 'Runtime console session is missing metrics scope (403). Reload session bootstrap if this persists.'
      : 'Runtime console access was denied by backend policy (403).';
  }

  if (message.includes('API 401')) {
    return 'Runtime console authentication failed (401). Check session bootstrap or HELPER_API_KEY.';
  }

  if (message.toLowerCase().includes('network error') || message.includes('Failed to fetch')) {
    return 'Runtime console lost connection to Helper backend.';
  }

  return `Runtime console error: ${message}`;
}

function createSeverityCounts(): Record<RuntimeSeverity, number> {
  return { error: 0, warn: 0, info: 0, debug: 0, neutral: 0 };
}

function createScopeCounts(): Record<RuntimeScope, number> {
  return { boot: 0, control: 0, api: 0, model: 0, storage: 0, security: 0, bus: 0, network: 0, exception: 0, misc: 0 };
}

function createDomainCounts(): Record<RuntimeDomain, number> {
  return { readiness: 0, gateway: 0, persistence: 0, auth: 0, generation: 0, telemetry: 0, transport: 0, runtime: 0, unknown: 0 };
}

function pickTopScope(counts: Record<RuntimeScope, number>) {
  return (Object.keys(counts) as RuntimeScope[]).reduce((best, key) => counts[key] > counts[best] ? key : best, 'misc');
}

function pickTopDomain(counts: Record<RuntimeDomain, number>) {
  return (Object.keys(counts) as RuntimeDomain[]).reduce((best, key) => counts[key] > counts[best] ? key : best, 'unknown');
}

function normalizeSeverity(severity: string, message: string): RuntimeSeverity {
  const value = severity.trim().toLowerCase();
  if (value === 'error' || value === 'fatal') return 'error';
  if (value === 'warn' || value === 'warning') return 'warn';
  if (value === 'info' || value === 'information') return 'info';
  if (value === 'debug' || value === 'trace') return 'debug';

  const lower = message.toLowerCase();
  if (/\b(exception|fatal|unhandled|error|fail(ed|ure)?)\b/.test(lower)) return 'error';
  if (/\bwarning|denied|degraded|timeout\b/.test(lower)) return 'warn';
  if (/\b(started|ready|listening|connected|loaded)\b/.test(lower)) return 'info';
  return 'neutral';
}

function buildSummary(message: string, isStackFrame: boolean) {
  if (!message.trim()) return 'Blank line';
  if (isStackFrame) return message.trim();
  return message
    .replace(/^\[?\d{2}:\d{2}:\d{2}(?:\.\d+)?\]?\s*/i, '')
    .replace(/^\[?\d{4}-\d{2}-\d{2}[t ][^\]]+\]?\s*/i, '')
    .replace(/^(fail|warn|warning|info|debug|trace)\s*:\s*/i, '')
    .replace(/^[a-z0-9_.]+\[\d+\]\s*/i, '')
    .trim();
}

function buildSourceTag(sourcePath: string) {
  const basis = sourcePath.split('/').pop() ?? sourcePath;
  return basis
    .replace(/\.[a-z0-9]+$/i, '')
    .split(/[^a-z0-9]+/i)
    .filter(Boolean)
    .slice(0, 2)
    .map(token => token.slice(0, 3))
    .join('-')
    .toUpperCase() || 'LOG';
}

function buildMarkers({
  statusCode,
  durationMs,
  correlationId,
  route,
  jsonKeys,
  isStackFrame,
  isContinuation,
  operationKind,
  degradationReason,
  latencyBucket,
  structured,
  semanticsMarkers,
}: {
  statusCode?: string;
  durationMs?: number;
  correlationId?: string;
  route?: string;
  jsonKeys: string[];
  isStackFrame: boolean;
  isContinuation: boolean;
  operationKind?: string;
  degradationReason?: string;
  latencyBucket?: string;
  structured: boolean;
  semanticsMarkers: string[];
}) {
  const markers = [];
  if (statusCode) markers.push(`HTTP ${statusCode}`);
  if (durationMs !== undefined) markers.push(formatPreciseDuration(durationMs));
  if (route) markers.push(`route ${truncateMiddle(route, 22)}`);
  if (correlationId) markers.push(`corr ${truncateMiddle(correlationId, 18)}`);
  if (operationKind) markers.push(`op ${truncateMiddle(operationKind, 16)}`);
  if (latencyBucket) markers.push(latencyBucket);
  if (degradationReason) markers.push(`degraded ${truncateMiddle(degradationReason, 14)}`);
  if (structured) markers.push('dto v2');
  semanticsMarkers.forEach(marker => marker.trim() && markers.push(marker.trim()));
  if (jsonKeys.length > 0) markers.push('json');
  if (isStackFrame) markers.push('stack');
  if (isContinuation) markers.push('continuation');
  return Array.from(new Set(markers));
}

function buildDetails({
  statusCode,
  durationMs,
  correlationId,
  route,
  jsonKeys,
  isStackFrame,
  isContinuation,
  operationKind,
  degradationReason,
  latencyBucket,
  structured,
}: {
  statusCode?: string;
  durationMs?: number;
  correlationId?: string;
  route?: string;
  jsonKeys: string[];
  isStackFrame: boolean;
  isContinuation: boolean;
  operationKind?: string;
  degradationReason?: string;
  latencyBucket?: string;
  structured: boolean;
}) {
  const details: Array<{ label: string; value: string; tone: RuntimeSeverity }> = [];
  if (statusCode) details.push({ label: 'status', value: statusCode, tone: statusCode.startsWith('5') ? 'error' : statusCode.startsWith('4') ? 'warn' : 'info' });
  if (durationMs !== undefined) details.push({ label: 'latency', value: formatPreciseDuration(durationMs), tone: durationMs > 1500 ? 'warn' : 'info' });
  if (route) details.push({ label: 'route', value: truncateMiddle(route, 28), tone: 'info' });
  if (correlationId) details.push({ label: 'corr', value: truncateMiddle(correlationId, 18), tone: 'neutral' });
  if (operationKind) details.push({ label: 'op', value: truncateMiddle(operationKind, 20), tone: 'info' });
  if (latencyBucket) details.push({ label: 'bucket', value: latencyBucket, tone: 'neutral' });
  if (degradationReason) details.push({ label: 'degraded', value: truncateMiddle(degradationReason, 24), tone: 'warn' });
  if (structured) details.push({ label: 'telemetry', value: 'dto-v2', tone: 'neutral' });
  if (jsonKeys.length > 0) details.push({ label: 'payload', value: jsonKeys.join(', '), tone: 'debug' });
  if (isStackFrame || isContinuation) details.push({ label: 'flow', value: isStackFrame ? 'stack detail' : 'continuation', tone: 'neutral' });
  return details;
}

function extractDurationMs(message: string) {
  const msMatch = message.match(/\b(\d+(?:\.\d+)?)\s*ms\b/i);
  if (msMatch) return Number.parseFloat(msMatch[1]);
  const sMatch = message.match(/\b(\d+(?:\.\d+)?)\s*s\b/i);
  return sMatch ? Number.parseFloat(sMatch[1]) * 1000 : undefined;
}

function extractStatusCode(message: string) {
  const explicit = message.match(/\b(?:status(?:code)?|http)\D{0,10}([1-5]\d{2})\b/i);
  if (explicit) return explicit[1];
  const fallback = message.match(/\b(2\d{2}|3\d{2}|4\d{2}|5\d{2})\b(?=.*\b(request|response|route|endpoint|http)\b)/i);
  return fallback?.[1];
}

function extractCorrelationId(message: string) {
  const named = message.match(/\b(?:corr(?:elation)?[-_ ]?id|trace[-_ ]?id|request[-_ ]?id)\s*[:=]\s*([a-z0-9-]{6,})\b/i);
  if (named) return named[1];
  return message.match(/\b([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\b/i)?.[1];
}

function extractRoute(message: string) {
  const methodRoute = message.match(/\b(?:GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\s+([\/a-z0-9._?=&%-]+)/i);
  if (methodRoute) return methodRoute[1];
  return message.match(/\b(?:route|path|endpoint)\s*[:=]\s*([\/a-z0-9._?=&%-]+)/i)?.[1];
}

function extractJsonKeys(message: string) {
  return Array.from(message.matchAll(/"([a-z0-9_.-]{2,24})"\s*:/gi))
    .map(match => match[1])
    .filter((value, index, list) => list.indexOf(value) === index)
    .slice(0, 4);
}

function toEpochMs(value?: string) {
  if (!value) return undefined;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.getTime();
}
