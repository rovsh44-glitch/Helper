import React from 'react';
import { CapabilityCoveragePanel } from '../CapabilityCoveragePanel';
import {
  MetricCell,
  RouteEventRow,
  SignalBadge,
  statusCapsule,
} from './RuntimeConsolePresentation';
import { formatDateTime, formatFeedTime, truncateMiddle } from '../../utils/runtimeLogIntelligence';
import styles from './runtimeConsole.module.css';
import type { RuntimeConsoleModel } from '../../hooks/useRuntimeConsoleModel';
import { useCapabilityCatalog } from '../../hooks/useCapabilityCatalog';
import { useHelperHubContext } from '../../hooks/useHelperHubContext';

type RuntimeConsoleSidebarProps = Pick<
  RuntimeConsoleModel,
  | 'activeSource'
  | 'controlPlane'
  | 'filteredEntries'
  | 'logsSnapshot'
  | 'semanticsCoverage'
  | 'sourceEntries'
  | 'telemetryOverview'
  | 'updatedLabel'
>;

export default function RuntimeConsoleSidebar({
  activeSource,
  controlPlane,
  filteredEntries,
  logsSnapshot,
  semanticsCoverage,
  sourceEntries,
  telemetryOverview,
  updatedLabel,
}: RuntimeConsoleSidebarProps) {
  const { snapshot, error, isRefreshing } = useCapabilityCatalog();
  const { thoughts, progressEntries } = useHelperHubContext();
  const [actionStatus, setActionStatus] = React.useState<string | null>(null);

  React.useEffect(() => {
    if (!actionStatus) {
      return;
    }

    const timeoutId = window.setTimeout(() => setActionStatus(null), 2500);
    return () => window.clearTimeout(timeoutId);
  }, [actionStatus]);

  const runtimeFeed = React.useMemo(() => {
    const thoughtFeed = thoughts.map(thought => ({
      id: thought.id,
      kind: 'thought' as const,
      text: thought.content,
      timestamp: thought.timestamp,
      tag: thought.type ? thought.type.toUpperCase() : 'THOUGHT',
    }));

    const progressFeed = progressEntries.map(entry => ({
      id: entry.id,
      kind: 'progress' as const,
      text: entry.message,
      timestamp: entry.timestamp,
      tag: 'BUS',
    }));

    return [...thoughtFeed, ...progressFeed]
      .sort((left, right) => right.timestamp - left.timestamp)
      .slice(0, 32);
  }, [progressEntries, thoughts]);

  const alerts = React.useMemo(() => {
    const nextAlerts = [
      ...(controlPlane?.alerts ?? []),
      ...(logsSnapshot?.alerts ?? []),
    ];

    return nextAlerts.filter((value, index) => nextAlerts.indexOf(value) === index);
  }, [controlPlane, logsSnapshot]);

  const snapshotPayload = React.useMemo(() => ({
    exportedAtUtc: new Date().toISOString(),
    selectedSourceId: activeSource?.id ?? null,
    controlPlane,
    runtimeLogs: logsSnapshot,
  }), [activeSource?.id, controlPlane, logsSnapshot]);

  const copySnapshot = React.useCallback(async () => {
    const payload = JSON.stringify(snapshotPayload, null, 2);

    try {
      if (!navigator.clipboard?.writeText) {
        throw new Error('Clipboard API unavailable');
      }

      await navigator.clipboard.writeText(payload);
      setActionStatus('Runtime snapshot copied to clipboard.');
    } catch {
      setActionStatus('Runtime snapshot copy failed.');
    }
  }, [snapshotPayload]);

  const exportSnapshot = React.useCallback(() => {
    try {
      const payload = JSON.stringify(snapshotPayload, null, 2);
      const blob = new Blob([payload], { type: 'application/json;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `helper-runtime-snapshot-${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
      setActionStatus('Runtime snapshot exported.');
    } catch {
      setActionStatus('Runtime snapshot export failed.');
    }
  }, [snapshotPayload]);

  return (
    <div className="space-y-6">
      <section className={`${styles.sectionCard} border border-slate-800 bg-slate-950/85 p-5 shadow-2xl backdrop-blur`}>
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-6">
            <SignalBadge
              label="Phase"
              value={controlPlane?.readiness.phase ?? 'booting'}
              tone={controlPlane?.readiness.readyForChat ? 'good' : 'warn'}
            />
            <SignalBadge label="Model" value={controlPlane?.modelGateway.currentModel ?? 'n/a'} tone="info" />
            <SignalBadge label="Alerts" value={String(alerts.length)} tone={alerts.length > 0 ? 'warn' : 'good'} />
            <SignalBadge label="Semantics" value={semanticsCoverage.label} tone={semanticsCoverage.tone} />
            <SignalBadge label="Visible" value={`${filteredEntries.length}/${sourceEntries.length}`} tone={filteredEntries.length !== sourceEntries.length ? 'info' : 'neutral'} />
            <SignalBadge label="Updated" value={updatedLabel} tone="neutral" />
          </div>

          <div className="flex flex-wrap justify-end gap-2">
            <button
              type="button"
              onClick={() => void copySnapshot()}
              className={`rounded-full border border-slate-700 bg-slate-900/80 px-3 py-1.5 font-bold text-slate-300 transition-colors hover:border-slate-500 hover:text-white ${styles.kicker}`}
            >
              Copy snapshot
            </button>
            <button
              type="button"
              onClick={exportSnapshot}
              className={`rounded-full border border-cyan-500/30 bg-cyan-500/10 px-3 py-1.5 font-bold text-cyan-100 transition-colors hover:border-cyan-400/60 hover:text-white ${styles.kicker}`}
            >
              Export json
            </button>
          </div>

          {actionStatus && (
            <div className={`text-right text-cyan-200/80 ${styles.metaText}`}>{actionStatus}</div>
          )}
        </div>
      </section>

      <section className={`${styles.sectionCard} border border-slate-800 bg-slate-950/85 p-5 shadow-2xl backdrop-blur`}>
        <div className="mb-4 flex items-center justify-between">
          <div>
            <div className={`${styles.kickerWide} font-black text-slate-500`}>Control Plane</div>
            <div className="mt-1 text-lg font-semibold text-white">System posture</div>
          </div>
          <div className={`rounded-full px-3 py-1 font-bold ${styles.metaText} ${statusCapsule(controlPlane?.readiness.readyForChat ? 'good' : 'warn')}`}>
            {controlPlane?.readiness.status ?? 'starting'}
          </div>
        </div>

        <div className="mb-4 rounded-2xl border border-cyan-500/20 bg-cyan-500/5 px-4 py-3 text-xs leading-6 text-cyan-100/90">
          Runtime Console prefers DTO v2 semantics and backend route telemetry. Client-side inference remains only as a compatibility fallback for legacy log lines.
        </div>

        <div className="grid grid-cols-2 gap-3">
          <MetricCell label="Lifecycle" value={controlPlane?.readiness.lifecycleState ?? 'booting'} />
          <MetricCell label="Warmup" value={controlPlane?.readiness.warmupMode ?? 'minimal'} />
          <MetricCell label="Persistence" value={controlPlane?.persistence.ready ? 'ready' : 'degraded'} tone={controlPlane?.persistence.ready ? 'good' : 'warn'} />
          <MetricCell label="Audit Queue" value={String(controlPlane?.auditQueue.pending ?? 0)} tone={(controlPlane?.auditQueue.pending ?? 0) > 0 ? 'warn' : 'neutral'} />
          <MetricCell label="Config" value={controlPlane?.configuration.isValid ? 'valid' : 'attention'} tone={controlPlane?.configuration.isValid ? 'good' : 'warn'} />
          <MetricCell label="Model Catalog" value={String(controlPlane?.modelGateway.availableModels.length ?? 0)} tone="info" />
        </div>

        <div className="mt-5 rounded-2xl border border-slate-800 bg-slate-900/70 p-4">
          <div className="mb-3 flex items-center justify-between">
            <div>
              <div className={`${styles.kickerMedium} font-black text-slate-500`}>Structured Routing</div>
              <div className="mt-1 text-sm font-semibold text-white">Route telemetry snapshot</div>
            </div>
            <div className={`rounded-full px-3 py-1 font-bold ${styles.kicker} ${
              telemetryOverview.failedCount > 0 || telemetryOverview.blockedCount > 0
                ? 'border-amber-500/20 bg-amber-500/10 text-amber-100'
                : telemetryOverview.totalEvents > 0
                  ? 'border-emerald-500/20 bg-emerald-500/10 text-emerald-100'
                  : 'border-slate-700 bg-slate-900/80 text-slate-400'
            }`}>
              {telemetryOverview.totalEvents > 0 ? `${telemetryOverview.totalEvents} events` : 'idle'}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <MetricCell label="Top channel" value={telemetryOverview.dominantChannel} tone="info" />
            <MetricCell label="Top op" value={telemetryOverview.dominantOperationKind} tone="info" />
            <MetricCell label="Top route" value={truncateMiddle(telemetryOverview.dominantRoute, 20)} tone="neutral" />
            <MetricCell
              label="Quality"
              value={telemetryOverview.dominantQuality}
              tone={telemetryOverview.failedCount > 0 || telemetryOverview.blockedCount > 0 ? 'warn' : 'good'}
            />
          </div>

          <div className="mt-3 space-y-2">
            {telemetryOverview.recent.slice(0, 3).map(event => (
              <RouteEventRow key={`${event.recordedAtUtc}-${event.correlationId ?? event.routeKey ?? event.operationKind}`} event={event} />
            ))}
            {telemetryOverview.recent.length === 0 && (
              <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/70 px-4 py-3 text-sm text-slate-500">
                Route telemetry has not recorded any events yet.
              </div>
            )}
          </div>
        </div>
      </section>

      <section className={`${styles.sectionCard} border border-slate-800 bg-slate-950/85 p-5 shadow-2xl backdrop-blur`}>
        <div className="mb-4">
          <div className={`${styles.kickerWide} font-black text-slate-500`}>Execution Bus</div>
          <div className="mt-1 text-lg font-semibold text-white">Live internal signal stream</div>
        </div>

        <div className={styles.feedViewport}>
          {runtimeFeed.length === 0 ? (
            <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/70 px-4 py-6 text-sm text-slate-500">
              Waiting for SignalR runtime events.
            </div>
          ) : (
            <div className="space-y-2">
              {runtimeFeed.map(item => (
                <div
                  key={item.id}
                  className={`rounded-2xl border px-3 py-3 ${
                    item.kind === 'thought'
                      ? 'border-cyan-500/20 bg-cyan-500/5'
                      : 'border-emerald-500/20 bg-emerald-500/5'
                  }`}
                >
                  <div className={`mb-1 flex items-center justify-between gap-3 text-slate-500 ${styles.kickerFeed}`}>
                    <span className={item.kind === 'thought' ? 'text-cyan-300' : 'text-emerald-300'}>{item.tag}</span>
                    <span>{formatFeedTime(item.timestamp)}</span>
                  </div>
                  <div className="text-sm leading-6 text-slate-200">{item.text}</div>
                </div>
              ))}
            </div>
          )}
        </div>
      </section>

      <section className={`${styles.sectionCard} border border-slate-800 bg-slate-950/85 p-5 shadow-2xl backdrop-blur`}>
        <div className="mb-4">
          <div className={`${styles.kickerWide} font-black text-slate-500`}>Alert Stack</div>
          <div className="mt-1 text-lg font-semibold text-white">Aggregated warnings</div>
        </div>

        <div className="space-y-2">
          {alerts.length === 0 ? (
            <div className="rounded-2xl border border-emerald-500/20 bg-emerald-500/5 px-4 py-3 text-sm text-emerald-200">
              No active control-plane or log-source alerts.
            </div>
          ) : (
            alerts.map(alert => (
              <div key={alert} className="rounded-2xl border border-amber-500/20 bg-amber-500/5 px-4 py-3 text-sm text-amber-100">
                {alert}
              </div>
            ))
          )}
        </div>
      </section>

      <CapabilityCoveragePanel
        snapshot={snapshot}
        error={error}
        isRefreshing={isRefreshing}
        compact
        title="Operator Capability Coverage"
      />
    </div>
  );
}
