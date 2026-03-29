import React from 'react';
import type { ControlPlaneSnapshotDto } from '../../services/api/runtimeApi';
import {
  DOMAIN_LABELS,
  type BootStage,
  type DerivedLogEntry,
  type FilterOption,
  type RuntimeDomain,
  type RuntimeScope,
  type RuntimeSeverity,
  SCOPE_LABELS,
  SEVERITY_LABELS,
  escapeRegExp,
  formatDateTime,
  formatTimelineOffset,
  truncateMiddle,
} from '../../utils/runtimeLogIntelligence';
import styles from './runtimeConsole.module.css';

export function IntelCard({
  label,
  value,
  supporting,
  tone = 'neutral',
}: {
  label: string;
  value: string;
  supporting: string;
  tone?: SurfaceTone;
}) {
  return (
    <div className={`rounded-2xl border px-3 py-3 ${badgeTone(tone)}`}>
      <div className={`${styles.kicker} font-black text-slate-500`}>{label}</div>
      <div className="mt-1 text-sm font-semibold text-white">{value}</div>
      <div className={`mt-1 ${styles.metaText} text-slate-500`}>{supporting}</div>
    </div>
  );
}

export function InfoPill({
  label,
  tone = 'neutral',
}: {
  label: string;
  tone?: 'info' | 'warn' | 'neutral';
}) {
  return (
    <span className={`rounded-full border px-3 py-1.5 ${styles.metaText} ${
      tone === 'info'
        ? 'border-cyan-500/20 bg-cyan-500/5 text-cyan-100/90'
        : tone === 'warn'
          ? 'border-amber-500/20 bg-amber-500/5 text-amber-100/90'
          : 'border-slate-800 bg-slate-900/80 text-slate-400'
    }`}>
      {label}
    </span>
  );
}

export function FilterRow<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: T;
  options: FilterOption<T>[];
  onChange: (value: T) => void;
}) {
  return (
    <div className={styles.filterGrid}>
      <div className={`pt-2 ${styles.kickerMedium} font-black text-slate-500`}>{label}</div>
      <div className="flex flex-wrap gap-2">
        {options.map(option => {
          const active = option.value === value;

          return (
            <button
              key={option.value}
              type="button"
              onClick={() => onChange(option.value)}
              className={`inline-flex items-center gap-2 rounded-full border px-3 py-1.5 font-bold transition-all ${styles.kicker} ${
                active
                  ? `border-cyan-400/50 bg-cyan-500/10 text-cyan-100 ${styles.accentGlow}`
                  : 'border-slate-800 bg-slate-900/80 text-slate-400 hover:border-slate-600 hover:text-slate-200'
              }`}
            >
              <span>{option.label}</span>
              <span className={`rounded-full px-1.5 py-0.5 ${styles.kicker} ${active ? 'bg-cyan-400/10 text-cyan-100/80' : 'bg-black/20 text-slate-500'}`}>
                {option.count}
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}

export function LogLine({
  entry,
  searchTokens,
}: {
  entry: DerivedLogEntry;
  searchTokens: string[];
}) {
  const lineTone = logTone(entry.severity, entry.raw.isContinuation);
  const showRaw = entry.summary !== entry.message;

  return (
    <article className={`${styles.logCard} border px-4 py-3 ${lineTone.container}`}>
      <div className="flex flex-col gap-3 xl:flex-row xl:items-start">
        <div className={`grid gap-2 text-slate-500 ${styles.kicker} ${styles.sourceMetaGrid}`}>
          <div className="rounded-xl border border-black/10 bg-black/15 px-2.5 py-2 text-slate-300">
            {entry.raw.timestampLabel ?? '--:--:--'}
          </div>
          <div className={`rounded-xl border px-2.5 py-2 ${lineTone.badge}`}>L{entry.raw.lineNumber}</div>
          <div className="rounded-xl border border-slate-800 bg-slate-900/70 px-2.5 py-2 text-slate-400">{entry.sourceTag}</div>
        </div>

        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap gap-1.5">
            <LineChip label={SEVERITY_LABELS[entry.severity]} className={severityTone(entry.severity)} />
            <LineChip label={SCOPE_LABELS[entry.scope]} className={scopeTone(entry.scope)} />
            <LineChip label={DOMAIN_LABELS[entry.domain]} className={domainTone(entry.domain)} />
            {entry.markers.slice(0, 4).map(marker => (
              <LineChip key={marker} label={marker} className="border-slate-700 bg-slate-900/80 text-slate-300" />
            ))}
          </div>

          <div className={`mt-3 whitespace-pre-wrap break-words ${styles.summaryText} ${lineTone.text}`}>
            {highlightText(entry.summary, searchTokens)}
          </div>

          {showRaw && (
            <div className={`mt-2 rounded-2xl border border-black/10 bg-black/15 px-3 py-2 text-slate-300/85 ${styles.detailText}`}>
              {highlightText(entry.message, searchTokens)}
            </div>
          )}

          {entry.details.length > 0 && (
            <div className="mt-3 flex flex-wrap gap-2">
              {entry.details.map(detail => (
                <LineChip
                  key={`${detail.label}:${detail.value}`}
                  label={`${detail.label}: ${detail.value}`}
                  className={severityTone(detail.tone)}
                />
              ))}
            </div>
          )}
        </div>
      </div>
    </article>
  );
}

export function SignalBadge({
  label,
  value,
  tone,
}: {
  label: string;
  value: string;
  tone: SurfaceTone;
}) {
  return (
    <div className={`rounded-2xl border px-3 py-3 ${badgeTone(tone)}`}>
      <div className={`${styles.kicker} font-black text-slate-500`}>{label}</div>
      <div className="mt-1 truncate text-sm font-semibold text-white">{value}</div>
    </div>
  );
}

export function MetricCell({
  label,
  value,
  tone = 'neutral',
}: {
  label: string;
  value: string;
  tone?: SurfaceTone;
}) {
  return (
    <div className={`rounded-2xl border px-3 py-3 ${badgeTone(tone)}`}>
      <div className={`${styles.kicker} font-black text-slate-500`}>{label}</div>
      <div className="mt-1 text-sm font-semibold text-white">{value}</div>
    </div>
  );
}

export function RouteEventRow({
  event,
}: {
  event: NonNullable<ControlPlaneSnapshotDto['routeTelemetry']>['recent'][number];
}) {
  const qualityTone = event.quality === 'failed' || event.quality === 'blocked'
    ? 'warn'
    : event.quality === 'degraded'
      ? 'warn'
      : 'info';

  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-950/70 px-3 py-3">
      <div className={`flex items-center justify-between gap-3 text-slate-500 ${styles.kickerTight}`}>
        <span>{event.channel}</span>
        <span>{formatDateTime(event.recordedAtUtc)}</span>
      </div>
      <div className="mt-2 flex flex-wrap gap-2">
        <LineChip label={event.operationKind} className="border-cyan-500/20 bg-cyan-500/10 text-cyan-100" />
        <LineChip
          label={event.quality}
          className={qualityTone === 'warn'
            ? 'border-amber-500/20 bg-amber-500/10 text-amber-100'
            : 'border-emerald-500/20 bg-emerald-500/10 text-emerald-100'}
        />
        <LineChip label={event.outcome} className="border-slate-700 bg-slate-900/80 text-slate-300" />
      </div>
      <div className="mt-2 text-sm text-slate-200">{truncateMiddle(event.routeKey || 'Route unavailable', 44)}</div>
      <div className={`mt-1 ${styles.metaText} text-slate-500`}>
        {event.modelRoute ? `model ${event.modelRoute}` : 'no model route'}
        {event.correlationId ? ` · corr ${truncateMiddle(event.correlationId, 16)}` : ''}
      </div>
    </div>
  );
}

export function BootTimeline({
  stages,
  status,
}: {
  stages: BootStage[];
  status?: string;
}) {
  return (
    <div className={`${styles.timelineShell} ${styles.sectionCardLarge} border border-slate-800 bg-slate-950/70 p-4`}>
      <div className="mb-4 flex items-center justify-between gap-3">
        <div className={`${styles.kickerTight} text-slate-500`}>Staged readiness path</div>
        <div className={`rounded-full border border-slate-700 bg-slate-900/90 px-3 py-1 font-bold text-slate-300 ${styles.kicker}`}>
          {status ?? 'starting'}
        </div>
      </div>

      <div className="space-y-3">
        {stages.map((stage, index) => {
          const nextStage = stages[index + 1];
          const connectorActive = stage.reached && Boolean(nextStage?.reached);

          return (
            <div key={stage.id} className={`${styles.timelineCard} relative pl-11`} style={{ animationDelay: `${index * 120}ms` }}>
              {index < stages.length - 1 && (
                <div
                  className={`absolute w-px ${styles.timelineConnector} ${
                    connectorActive ? `${styles.timelineConnectorActive} bg-gradient-to-b from-cyan-400/70 via-sky-400/50 to-emerald-400/20` : 'bg-slate-800'
                  }`}
                />
              )}

              <span className={`absolute h-5 w-5 rounded-full border ${styles.timelineNode} ${stage.reached ? `${styles.timelineNodeActive} ${stage.dotClass}` : 'border-slate-700 bg-slate-800'}`} />

              <div className={`${styles.timelineBlock} border px-4 py-4 ${stage.reached ? stage.cardClass : 'border-slate-800 bg-slate-950/80'}`}>
                <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                  <div>
                    <div className={`${styles.kickerTight} font-black text-slate-500`}>{stage.label}</div>
                    <div className="mt-1 text-sm font-semibold text-white">{formatDateTime(stage.value)}</div>
                  </div>
                  <div className={`rounded-full border px-3 py-1 font-bold ${styles.kicker} ${stage.reached ? stage.toneClass : 'border-slate-700 bg-slate-900/80 text-slate-400'}`}>
                    {stage.reached ? formatTimelineOffset(stage.offsetMs) : 'pending'}
                  </div>
                </div>

                <div className={`mt-3 grid grid-cols-2 gap-2 ${styles.metaText}`}>
                  <TimelineMini label="Segment" value={stage.reached ? formatTimelineOffset(stage.segmentMs) : 'pending'} />
                  <TimelineMini label="State" value={stage.reached ? 'reached' : 'awaiting'} />
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export function PolicyPill({ label, enabled }: { label: string; enabled: boolean }) {
  return (
    <div className={`rounded-full border px-3 py-1.5 font-bold ${styles.kicker} ${
      enabled
        ? 'border-emerald-400/30 bg-emerald-500/10 text-emerald-200'
        : 'border-slate-700 bg-slate-900/80 text-slate-400'
    }`}>
      {label}: {enabled ? 'ON' : 'OFF'}
    </div>
  );
}

export function MiniStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-900/80 px-3 py-3">
      <div className={`${styles.kicker} font-black text-slate-500`}>{label}</div>
      <div className="mt-1 text-sm font-semibold text-white">{value}</div>
    </div>
  );
}

export function PoolStat({ label, value }: { label: string; value: number | string }) {
  return (
    <div className="rounded-xl border border-slate-800 bg-black/20 px-2 py-2 text-center">
      <div className={`${styles.kicker} font-black text-slate-500`} style={{ fontSize: '9px' }}>{label}</div>
      <div className="mt-1 text-xs font-semibold text-slate-100">{value}</div>
    </div>
  );
}

export function statusCapsule(tone: 'good' | 'warn') {
  return tone === 'good'
    ? 'border border-emerald-500/20 bg-emerald-500/10 text-emerald-200'
    : 'border border-amber-500/20 bg-amber-500/10 text-amber-100';
}

type SurfaceTone = 'good' | 'warn' | 'info' | 'neutral';

function LineChip({ label, className }: { label: string; className: string }) {
  return (
    <span className={`rounded-full border px-2.5 py-1 font-bold ${styles.kicker} ${className}`}>
      {label}
    </span>
  );
}

function TimelineMini({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-slate-800 bg-black/20 px-3 py-2">
      <div className={`${styles.kicker} font-black text-slate-500`} style={{ fontSize: '9px' }}>{label}</div>
      <div className="mt-1 text-xs font-semibold text-slate-100">{value}</div>
    </div>
  );
}

function badgeTone(tone: SurfaceTone) {
  switch (tone) {
    case 'good':
      return 'border-emerald-500/20 bg-emerald-500/5';
    case 'warn':
      return 'border-amber-500/20 bg-amber-500/5';
    case 'info':
      return 'border-cyan-500/20 bg-cyan-500/5';
    default:
      return 'border-slate-800 bg-slate-900/80';
  }
}

function logTone(severity: RuntimeSeverity, isContinuation: boolean) {
  if (severity === 'error') {
    return {
      container: 'border-rose-500/15 bg-rose-500/5',
      badge: 'border-rose-500/20 bg-rose-500/10 text-rose-200',
      text: isContinuation ? 'text-rose-100/85' : 'text-rose-100',
    };
  }

  if (severity === 'warn') {
    return {
      container: 'border-amber-500/15 bg-amber-500/5',
      badge: 'border-amber-500/20 bg-amber-500/10 text-amber-200',
      text: isContinuation ? 'text-amber-100/85' : 'text-amber-100',
    };
  }

  if (severity === 'info') {
    return {
      container: 'border-cyan-500/15 bg-cyan-500/5',
      badge: 'border-cyan-500/20 bg-cyan-500/10 text-cyan-200',
      text: isContinuation ? 'text-cyan-50/85' : 'text-cyan-50',
    };
  }

  if (severity === 'debug') {
    return {
      container: 'border-violet-500/15 bg-violet-500/5',
      badge: 'border-violet-500/20 bg-violet-500/10 text-violet-200',
      text: isContinuation ? 'text-violet-50/75' : 'text-violet-50/90',
    };
  }

  return {
    container: 'border-slate-900 bg-slate-950/70',
    badge: 'border-slate-700 bg-slate-900/80 text-slate-300',
    text: isContinuation ? 'text-slate-300/85' : 'text-slate-200',
  };
}

function severityTone(severity: RuntimeSeverity) {
  switch (severity) {
    case 'error':
      return 'border-rose-500/20 bg-rose-500/10 text-rose-100';
    case 'warn':
      return 'border-amber-500/20 bg-amber-500/10 text-amber-100';
    case 'info':
      return 'border-cyan-500/20 bg-cyan-500/10 text-cyan-100';
    case 'debug':
      return 'border-violet-500/20 bg-violet-500/10 text-violet-100';
    default:
      return 'border-slate-700 bg-slate-900/80 text-slate-300';
  }
}

function scopeTone(scope: RuntimeScope) {
  switch (scope) {
    case 'boot':
      return 'border-sky-500/20 bg-sky-500/10 text-sky-100';
    case 'control':
      return 'border-cyan-500/20 bg-cyan-500/10 text-cyan-100';
    case 'api':
      return 'border-blue-500/20 bg-blue-500/10 text-blue-100';
    case 'model':
      return 'border-fuchsia-500/20 bg-fuchsia-500/10 text-fuchsia-100';
    case 'storage':
      return 'border-emerald-500/20 bg-emerald-500/10 text-emerald-100';
    case 'security':
      return 'border-amber-500/20 bg-amber-500/10 text-amber-100';
    case 'bus':
      return 'border-teal-500/20 bg-teal-500/10 text-teal-100';
    case 'network':
      return 'border-indigo-500/20 bg-indigo-500/10 text-indigo-100';
    case 'exception':
      return 'border-rose-500/20 bg-rose-500/10 text-rose-100';
    default:
      return 'border-slate-700 bg-slate-900/80 text-slate-300';
  }
}

function domainTone(domain: RuntimeDomain) {
  switch (domain) {
    case 'readiness':
      return 'border-sky-500/20 bg-sky-500/10 text-sky-100';
    case 'gateway':
      return 'border-fuchsia-500/20 bg-fuchsia-500/10 text-fuchsia-100';
    case 'persistence':
      return 'border-emerald-500/20 bg-emerald-500/10 text-emerald-100';
    case 'auth':
      return 'border-amber-500/20 bg-amber-500/10 text-amber-100';
    case 'generation':
      return 'border-violet-500/20 bg-violet-500/10 text-violet-100';
    case 'telemetry':
      return 'border-cyan-500/20 bg-cyan-500/10 text-cyan-100';
    case 'transport':
      return 'border-blue-500/20 bg-blue-500/10 text-blue-100';
    case 'runtime':
      return 'border-slate-600 bg-slate-900/80 text-slate-200';
    default:
      return 'border-slate-700 bg-slate-900/80 text-slate-400';
  }
}

function highlightText(text: string, tokens: string[]) {
  if (!text) return '\u00A0';
  if (tokens.length === 0) return text;
  const expression = new RegExp(`(${tokens.map(escapeRegExp).join('|')})`, 'ig');
  return text.split(expression).map((part, index) => tokens.some(token => part.toLowerCase() === token)
    ? <mark key={`${part}-${index}`} className="rounded bg-cyan-400/20 px-0.5 text-cyan-50">{part}</mark>
    : <React.Fragment key={`${part}-${index}`}>{part}</React.Fragment>);
}
