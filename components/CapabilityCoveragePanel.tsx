import React, { useMemo } from 'react';
import type { CapabilityCatalogSnapshotDto } from '../services/api/runtimeApi';
import styles from './capabilityCoveragePanel.module.css';

type CapabilityCoveragePanelProps = {
  snapshot: CapabilityCatalogSnapshotDto | null;
  error?: string | null;
  isRefreshing?: boolean;
  compact?: boolean;
  title?: string;
};

export function CapabilityCoveragePanel({
  snapshot,
  error = null,
  isRefreshing = false,
  compact = false,
  title = 'Capability Coverage',
}: CapabilityCoveragePanelProps) {
  const unmapped = useMemo(
    () => (snapshot?.declaredCapabilities ?? [])
      .filter(entry => entry.certificationRelevant && entry.enabledInCertification && !entry.owningGate)
      .slice(0, compact ? 3 : 8),
    [compact, snapshot],
  );
  const degraded = useMemo(
    () => (snapshot?.declaredCapabilities ?? [])
      .filter(entry => entry.hasCriticalAlerts || entry.status === 'degraded' || entry.status === 'blocked')
      .slice(0, compact ? 3 : 8),
    [compact, snapshot],
  );
  const models = useMemo(
    () => (snapshot?.models ?? []).slice(0, compact ? 3 : 6),
    [compact, snapshot],
  );

  return (
    <section className={`${styles.sectionCard} border border-slate-800 bg-slate-950/85 p-5 shadow-2xl backdrop-blur`}>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <div className={`${styles.kickerWide} font-black text-slate-500`}>Capability Catalog</div>
          <div className="mt-1 text-lg font-semibold text-white">{title}</div>
          <p className="mt-2 text-xs text-slate-500">
            Inspectable capability truth for model routes, templates, tools, and extensions.
          </p>
        </div>
        <div className={`${styles.kickerMedium} text-right text-slate-500`}>
          {isRefreshing ? 'refreshing' : snapshot ? `updated ${formatDateTime(snapshot.generatedAtUtc)}` : 'idle'}
        </div>
      </div>

      {error && (
        <div className="mb-4 rounded-2xl border border-rose-500/20 bg-rose-500/5 px-4 py-3 text-sm text-rose-200">
          {error}
        </div>
      )}

      {!snapshot ? (
        <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/70 px-4 py-6 text-sm text-slate-500">
          Capability snapshot has not been loaded yet.
        </div>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <SummaryCard label="Model routes" value={String(snapshot.models.length)} supporting={`${snapshot.models.filter(model => model.resolvedModelAvailable).length} available`} tone="info" />
            <SummaryCard label="Declared" value={String(snapshot.summary.totalDeclaredCapabilities)} supporting={`${snapshot.declaredCapabilities.length} cataloged`} tone="neutral" />
            <SummaryCard label="Missing gate" value={String(snapshot.summary.missingGateOwnership)} supporting="needs owner gate" tone={snapshot.summary.missingGateOwnership > 0 ? 'warn' : 'good'} />
            <SummaryCard label="Degraded" value={String(snapshot.summary.degraded)} supporting={`${snapshot.summary.disabledInCertification} disabled in cert`} tone={snapshot.summary.degraded > 0 ? 'warn' : 'neutral'} />
          </div>

          <div className={`mt-5 ${compact ? 'space-y-4' : styles.splitGrid}`}>
            <div className="space-y-3">
              <SectionTitle title="Model Routes" subtitle="Current route class, resolved model, and fallback role." />
              {models.map(model => (
                <div key={model.capabilityId} className="rounded-2xl border border-slate-800 bg-slate-900/70 px-4 py-3">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <div className="text-sm font-semibold text-white">{model.routeKey}</div>
                      <div className="text-[11px] text-slate-500">{model.intendedUse}</div>
                    </div>
                    <StatusBadge label={model.resolvedModelAvailable ? 'available' : 'missing'} tone={model.resolvedModelAvailable ? 'good' : 'warn'} />
                  </div>
                  <div className={`mt-3 flex flex-wrap gap-2 ${styles.pillRow}`}>
                    <Pill label={model.modelClass} tone="info" />
                    <Pill label={`latency ${model.latencyTier}`} tone="neutral" />
                    <Pill label={`fallback ${model.fallbackClass}`} tone="neutral" />
                    {model.supportsVision && <Pill label="vision" tone="info" />}
                    {model.supportsStreaming && <Pill label="streaming" tone="info" />}
                  </div>
                  <div className="mt-3 text-xs text-slate-400">
                    resolved `{model.resolvedModel}`{model.configuredFallbackModel ? ` · configured fallback ${model.configuredFallbackModel}` : ''}
                  </div>
                </div>
              ))}
              {snapshot.models.length === 0 && (
                <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/70 px-4 py-4 text-sm text-slate-500">
                  No model route catalog entries are available.
                </div>
              )}
            </div>

            <div className="space-y-3">
              <SectionTitle title="Gate Ownership" subtitle="Capabilities missing owning certification gate or blocked by current status." />
              {unmapped.map(entry => (
                <CapabilityEntry key={entry.capabilityId} entry={entry} />
              ))}
              {unmapped.length === 0 && degraded.length === 0 && (
                <div className="rounded-2xl border border-emerald-500/20 bg-emerald-500/5 px-4 py-4 text-sm text-emerald-200">
                  No capability entries are currently missing owning certification gates.
                </div>
              )}

              {degraded.length > 0 && (
                <>
                  <SectionTitle title="Blocked Or Degraded" subtitle="Capability records currently blocked by certification or critical alerts." />
                  {degraded.map(entry => (
                    <CapabilityEntry key={entry.capabilityId} entry={entry} />
                  ))}
                </>
              )}
            </div>
          </div>

          {!compact && (
            <div className="mt-5 space-y-3">
              <SectionTitle title="Catalog Alerts" subtitle="Derived warnings from capability ownership and registry state." />
              {snapshot.alerts.length > 0 ? snapshot.alerts.slice(0, 8).map(alert => (
                <div key={alert} className="rounded-2xl border border-amber-500/20 bg-amber-500/5 px-4 py-3 text-sm text-amber-100">
                  {alert}
                </div>
              )) : (
                <div className="rounded-2xl border border-emerald-500/20 bg-emerald-500/5 px-4 py-3 text-sm text-emerald-200">
                  No capability catalog alerts are active.
                </div>
              )}
            </div>
          )}
        </>
      )}
    </section>
  );
}

function SectionTitle({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <div>
      <div className={`${styles.kickerTight} font-black text-slate-500`}>{title}</div>
      <div className={`mt-1 ${styles.metaText} text-slate-500`}>{subtitle}</div>
    </div>
  );
}

function SummaryCard({
  label,
  value,
  supporting,
  tone,
}: {
  label: string;
  value: string;
  supporting: string;
  tone: 'good' | 'warn' | 'info' | 'neutral';
}) {
  return (
    <div className={`rounded-2xl border px-3 py-3 ${toneClass(tone)}`}>
      <div className={`${styles.kicker} font-black text-slate-500`}>{label}</div>
      <div className="mt-1 text-sm font-semibold text-white">{value}</div>
      <div className={`mt-1 ${styles.metaText} text-slate-500`}>{supporting}</div>
    </div>
  );
}

function CapabilityEntry({
  entry,
}: {
  entry: CapabilityCatalogSnapshotDto['declaredCapabilities'][number];
}) {
  const tone = entry.hasCriticalAlerts || entry.status === 'degraded' || entry.status === 'blocked'
    ? 'warn'
    : !entry.owningGate && entry.certificationRelevant && entry.enabledInCertification
      ? 'warn'
      : entry.certified
        ? 'good'
        : 'neutral';

  return (
    <div className={`rounded-2xl border px-4 py-3 ${toneClass(tone)}`}>
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-sm font-semibold text-white">{entry.displayName}</div>
          <div className={`mt-1 ${styles.metaText} text-slate-400`}>{entry.declaredCapability}</div>
        </div>
        <StatusBadge label={entry.status.replace(/_/g, ' ')} tone={tone} />
      </div>
      <div className={`mt-3 flex flex-wrap gap-2 ${styles.pillRow}`}>
        <Pill label={entry.surfaceKind} tone="neutral" />
        <Pill label={entry.capabilityId} tone="info" />
        <Pill label={entry.owningGate ?? 'no owner gate'} tone={entry.owningGate ? 'neutral' : 'warn'} />
      </div>
      {(entry.evidenceType || entry.evidenceRef) && (
        <div className={`mt-3 ${styles.metaText} text-slate-500`}>
          {entry.evidenceType ?? 'evidence'}: {entry.evidenceRef ?? 'not reported'}
        </div>
      )}
    </div>
  );
}

function StatusBadge({ label, tone }: { label: string; tone: 'good' | 'warn' | 'neutral' | 'info' }) {
  return (
    <span className={`rounded-full border px-2.5 py-1 font-bold ${styles.kicker} ${toneClass(tone)}`}>
      {label}
    </span>
  );
}

function Pill({ label, tone }: { label: string; tone: 'info' | 'warn' | 'neutral' }) {
  return (
    <span className={`rounded-full border px-2 py-1 ${tone === 'info'
      ? 'border-cyan-500/20 bg-cyan-500/10 text-cyan-100'
      : tone === 'warn'
        ? 'border-amber-500/20 bg-amber-500/10 text-amber-100'
        : 'border-slate-700 bg-slate-900/80 text-slate-300'}`}>
      {label}
    </span>
  );
}

function toneClass(tone: 'good' | 'warn' | 'info' | 'neutral') {
  switch (tone) {
    case 'good':
      return 'border-emerald-500/20 bg-emerald-500/5 text-emerald-100';
    case 'warn':
      return 'border-amber-500/20 bg-amber-500/5 text-amber-100';
    case 'info':
      return 'border-cyan-500/20 bg-cyan-500/5 text-cyan-100';
    default:
      return 'border-slate-800 bg-slate-900/70 text-slate-200';
  }
}

function formatDateTime(value?: string) {
  if (!value) {
    return 'n/a';
  }

  try {
    return new Date(value).toLocaleString();
  } catch {
    return value;
  }
}
