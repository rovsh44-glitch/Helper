import React from 'react';
import {
  BootTimeline,
  MetricCell,
  MiniStat,
  PolicyPill,
  PoolStat,
} from './RuntimeConsolePresentation';
import { formatDateTime, formatDuration } from '../../utils/runtimeLogIntelligence';
import styles from './runtimeConsole.module.css';
import type { RuntimeConsoleModel } from '../../hooks/useRuntimeConsoleModel';

type RuntimeConsoleTelemetryDeckProps = Pick<RuntimeConsoleModel, 'bootStages' | 'controlPlane'>;

export default function RuntimeConsoleTelemetryDeck({
  bootStages,
  controlPlane,
}: RuntimeConsoleTelemetryDeckProps) {
  return (
    <div className={styles.lowerDeckGrid}>
      <section className={`${styles.sectionCard} border border-slate-800 bg-slate-950/85 p-5 shadow-2xl backdrop-blur`}>
        <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <div className={`${styles.kickerWide} font-black text-slate-500`}>Readiness Ledger</div>
            <div className="mt-1 text-lg font-semibold text-white">Animated boot timeline</div>
          </div>
          <div className="text-xs text-slate-500">
            {controlPlane?.readiness.lastTransitionUtc ? `last transition ${formatDateTime(controlPlane.readiness.lastTransitionUtc)}` : 'no transition recorded'}
          </div>
        </div>
        <BootTimeline stages={bootStages} status={controlPlane?.readiness.status} />
        <div className="mt-4 grid grid-cols-3 gap-2 text-center text-[11px]">
          <MiniStat label="listen" value={formatDuration(controlPlane?.readiness.timeToListeningMs)} />
          <MiniStat label="ready" value={formatDuration(controlPlane?.readiness.timeToReadyMs)} />
          <MiniStat label="warm" value={formatDuration(controlPlane?.readiness.timeToWarmReadyMs)} />
        </div>
      </section>

      <section className={`${styles.sectionCard} border border-slate-800 bg-slate-950/85 p-5 shadow-2xl backdrop-blur`}>
        <div className="mb-4">
          <div className={`${styles.kickerWide} font-black text-slate-500`}>Model Gateway</div>
          <div className="mt-1 text-lg font-semibold text-white">Pool telemetry</div>
        </div>
        <div className="space-y-3">
          {(controlPlane?.modelGateway.pools ?? []).map(pool => (
            <div key={pool.pool} className="rounded-2xl border border-slate-800 bg-slate-900/80 p-3">
              <div className="flex items-center justify-between">
                <span className="text-sm font-semibold text-slate-100">{pool.pool}</span>
                <span className={`${styles.kicker} text-slate-500`}>in-flight {pool.inFlight}</span>
              </div>
              <div className={`mt-3 grid grid-cols-4 gap-2 text-slate-400 ${styles.metaText}`}>
                <PoolStat label="total" value={pool.totalCalls} />
                <PoolStat label="fail" value={pool.failedCalls} />
                <PoolStat label="timeout" value={pool.timeoutCalls} />
                <PoolStat label="avg" value={`${Math.round(pool.avgLatencyMs)}ms`} />
              </div>
            </div>
          ))}
          {(controlPlane?.modelGateway.pools.length ?? 0) === 0 && (
            <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/70 px-4 py-6 text-sm text-slate-500">
              Model pool telemetry is not available yet.
            </div>
          )}
        </div>
      </section>

      <section className={`${styles.sectionCard} border border-slate-800 bg-slate-950/85 p-5 shadow-2xl backdrop-blur`}>
        <div className="mb-4">
          <div className={`${styles.kickerWide} font-black text-slate-500`}>Policy Lattice</div>
          <div className="mt-1 text-lg font-semibold text-white">Runtime invariants</div>
        </div>

        <div className="flex flex-wrap gap-2">
          <PolicyPill label="Research" enabled={controlPlane?.policies.researchEnabled ?? false} />
          <PolicyPill label="Grounding" enabled={controlPlane?.policies.groundingEnabled ?? false} />
          <PolicyPill label="Sync Critic" enabled={controlPlane?.policies.synchronousCriticEnabled ?? false} />
          <PolicyPill label="Async Audit" enabled={controlPlane?.policies.asyncAuditEnabled ?? false} />
          <PolicyPill label="Shadow Mode" enabled={controlPlane?.policies.shadowModeEnabled ?? false} />
          <PolicyPill label="Safe Fallback" enabled={controlPlane?.policies.safeFallbackResponsesOnly ?? false} />
        </div>

        <div className="mt-5 grid grid-cols-2 gap-3 text-sm">
          <MetricCell label="Journal writes" value={formatDateTime(controlPlane?.persistence.lastJournalWriteAtUtc)} />
          <MetricCell label="Snapshot" value={formatDateTime(controlPlane?.persistence.lastSnapshotAtUtc)} />
          <MetricCell label="Queue flush" value={formatDateTime(controlPlane?.persistenceQueue.lastFlushedAtUtc)} />
          <MetricCell label="Audit processed" value={formatDateTime(controlPlane?.auditQueue.lastProcessedAt)} />
        </div>
      </section>
    </div>
  );
}
