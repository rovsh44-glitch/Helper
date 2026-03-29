import React, { Suspense, lazy } from 'react';
import { useRuntimeConsoleModel } from '../../hooks/useRuntimeConsoleModel';
import styles from '../runtime-console/runtimeConsole.module.css';

const RuntimeConsoleLogsPanel = lazy(() => import('../runtime-console/RuntimeConsoleLogsPanel'));
const RuntimeConsoleSidebar = lazy(() => import('../runtime-console/RuntimeConsoleSidebar'));
const RuntimeConsoleTelemetryDeck = lazy(() => import('../runtime-console/RuntimeConsoleTelemetryDeck'));

export default function RuntimeConsoleView() {
  const {
    activeSource,
    bootStages,
    controlPlane,
    domainFilter,
    domainOptions,
    error,
    filteredEntries,
    filteredIntel,
    hasFilters,
    logsSnapshot,
    resetFilters,
    scopeFilter,
    scopeOptions,
    searchTokens,
    semanticsCoverage,
    setDomainFilter,
    setScopeFilter,
    setSelectedSourceId,
    setSeverityFilter,
    setTextFilter,
    severityFilter,
    severityOptions,
    sourceEntries,
    sourceIntel,
    telemetryOverview,
    textFilter,
    updatedLabel,
  } = useRuntimeConsoleModel();

  return (
    <div className="relative h-full overflow-y-auto bg-slate-950 text-slate-100">
      <div className="pointer-events-none absolute inset-0 overflow-hidden">
        <div className="absolute -left-24 top-0 h-72 w-72 rounded-full bg-cyan-500/10 blur-3xl" />
        <div className="absolute right-0 top-16 h-96 w-96 rounded-full bg-blue-500/10 blur-3xl" />
        <div className="absolute bottom-0 left-1/3 h-80 w-80 rounded-full bg-emerald-500/10 blur-3xl" />
      </div>

      <div className="relative z-10 space-y-6 p-6">
        <header className={`${styles.sectionCardLarge} border border-cyan-500/20 bg-slate-950/85 px-6 py-5 shadow-2xl backdrop-blur`}>
          <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
            <div>
              <div className={`${styles.kickerUltra} font-black text-cyan-400/80`}>Runtime Console</div>
              <h1 className="mt-2 text-4xl font-black tracking-tight text-white">Machine Log Review</h1>
              <p className="mt-2 max-w-3xl text-sm text-slate-400">
                Structured runtime semantics, live control-plane state, and current log tails from the active Helper backend.
              </p>
            </div>
          </div>
        </header>

        {error && (
          <div className="space-y-1 rounded-2xl border border-rose-500/30 bg-rose-950/20 px-4 py-3 text-sm text-rose-200">
            {error && <div>{error}</div>}
          </div>
        )}

        <div className={styles.layoutGrid}>
          <Suspense fallback={<RuntimeConsolePanelFallback label="Loading log surface..." />}>
            <RuntimeConsoleLogsPanel
              activeSource={activeSource}
              domainFilter={domainFilter}
              domainOptions={domainOptions}
              filteredEntries={filteredEntries}
              filteredIntel={filteredIntel}
              hasFilters={hasFilters}
              logsSnapshot={logsSnapshot}
              resetFilters={resetFilters}
              scopeFilter={scopeFilter}
              scopeOptions={scopeOptions}
              searchTokens={searchTokens}
              semanticsCoverage={semanticsCoverage}
              setDomainFilter={setDomainFilter}
              setScopeFilter={setScopeFilter}
              setSelectedSourceId={setSelectedSourceId}
              setSeverityFilter={setSeverityFilter}
              setTextFilter={setTextFilter}
              severityFilter={severityFilter}
              severityOptions={severityOptions}
              sourceEntries={sourceEntries}
              sourceIntel={sourceIntel}
              textFilter={textFilter}
            />
          </Suspense>
          <Suspense fallback={<RuntimeConsolePanelFallback label="Loading operator surfaces..." />}>
            <RuntimeConsoleSidebar
              activeSource={activeSource}
              controlPlane={controlPlane}
              filteredEntries={filteredEntries}
              logsSnapshot={logsSnapshot}
              semanticsCoverage={semanticsCoverage}
              sourceEntries={sourceEntries}
              telemetryOverview={telemetryOverview}
              updatedLabel={updatedLabel}
            />
          </Suspense>
        </div>

        <Suspense fallback={<RuntimeConsolePanelFallback label="Loading telemetry deck..." />}>
          <RuntimeConsoleTelemetryDeck bootStages={bootStages} controlPlane={controlPlane} />
        </Suspense>
      </div>
    </div>
  );
}

function RuntimeConsolePanelFallback({ label }: { label: string }) {
  return (
    <div className={`${styles.sectionCard} border border-dashed border-slate-800 bg-slate-950/70 px-5 py-8 text-sm text-slate-500`}>
      {label}
    </div>
  );
}
