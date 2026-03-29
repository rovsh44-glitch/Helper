import React from 'react';
import {
  DOMAIN_LABELS,
  formatBytes,
  formatDateTime,
  formatPreciseDuration,
  SCOPE_LABELS,
} from '../../utils/runtimeLogIntelligence';
import {
  FilterRow,
  InfoPill,
  IntelCard,
  LogLine,
} from './RuntimeConsolePresentation';
import styles from './runtimeConsole.module.css';
import type { RuntimeConsoleModel } from '../../hooks/useRuntimeConsoleModel';

type RuntimeConsoleLogsPanelProps = Pick<
  RuntimeConsoleModel,
  | 'activeSource'
  | 'domainFilter'
  | 'domainOptions'
  | 'filteredEntries'
  | 'filteredIntel'
  | 'hasFilters'
  | 'logsSnapshot'
  | 'resetFilters'
  | 'scopeFilter'
  | 'scopeOptions'
  | 'searchTokens'
  | 'semanticsCoverage'
  | 'setDomainFilter'
  | 'setScopeFilter'
  | 'setSelectedSourceId'
  | 'setSeverityFilter'
  | 'setTextFilter'
  | 'severityFilter'
  | 'severityOptions'
  | 'sourceEntries'
  | 'sourceIntel'
  | 'textFilter'
>;

export default function RuntimeConsoleLogsPanel({
  activeSource,
  domainFilter,
  domainOptions,
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
  textFilter,
}: RuntimeConsoleLogsPanelProps) {
  return (
    <section className={`${styles.sectionCard} border border-slate-800 bg-slate-950/85 shadow-2xl backdrop-blur`}>
      <div className="flex flex-col gap-4 border-b border-slate-800 px-5 py-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <div className={`${styles.kickerWide} font-black text-slate-500`}>Log Sources</div>
          <div className="mt-1 text-lg font-semibold text-white">Real runtime tail</div>
          <p className="mt-2 text-xs text-slate-500">
            Scope, domain, route, latency, and correlation markers use DTO v2 semantics when available. Legacy lines fall back to local parsing.
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          {(logsSnapshot?.sources ?? []).map(source => (
            <button
              key={source.id}
              type="button"
              onClick={() => setSelectedSourceId(source.id)}
              className={`rounded-2xl border px-3 py-2 text-left font-bold transition-all ${styles.metaText} ${
                activeSource?.id === source.id
                  ? `border-cyan-400/50 bg-cyan-500/10 text-cyan-200 ${styles.accentGlow}`
                  : 'border-slate-700 bg-slate-900/80 text-slate-400 hover:border-slate-500 hover:text-slate-200'
              }`}
            >
              <div className={styles.kicker}>{source.label}</div>
              <div className={`mt-1 normal-case tracking-normal ${styles.kicker} ${activeSource?.id === source.id ? 'text-cyan-100/70' : 'text-slate-500'}`}>
                {source.displayPath}
              </div>
            </button>
          ))}
        </div>
      </div>

      <div className="space-y-4 border-b border-slate-900/80 px-5 py-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <div className={`font-mono text-slate-300 ${styles.metaText}`} style={{ textTransform: 'uppercase', letterSpacing: '0.18em' }}>
              {activeSource?.displayPath ?? 'No source selected'}
            </div>
            <div className={`mt-2 flex flex-wrap gap-2 text-slate-500 ${styles.metaText}`}>
              <span>{activeSource ? `${activeSource.totalLines} total lines` : '0 lines'}</span>
              <span>{activeSource ? formatBytes(activeSource.sizeBytes) : '0 B'}</span>
              <span>{activeSource?.lastWriteTimeUtc ? `write ${formatDateTime(activeSource.lastWriteTimeUtc)}` : 'no write time'}</span>
              <span>{logsSnapshot?.generatedAtUtc ? `snapshot ${formatDateTime(logsSnapshot.generatedAtUtc)}` : 'no snapshot'}</span>
            </div>
          </div>
          {hasFilters && (
            <button
              type="button"
              onClick={resetFilters}
              className={`rounded-full border border-slate-700 bg-slate-900/80 px-3 py-1.5 font-bold text-slate-300 transition-colors hover:border-slate-500 hover:text-white ${styles.kicker}`}
            >
              Reset filters
            </button>
          )}
        </div>

        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <IntelCard
            label="Pressure"
            value={`${filteredIntel.severity.error} err / ${filteredIntel.severity.warn} warn`}
            supporting={`${filteredIntel.httpFaults} http faults`}
            tone={filteredIntel.severity.error > 0 ? 'warn' : 'neutral'}
          />
          <IntelCard
            label="Scope"
            value={SCOPE_LABELS[filteredIntel.dominantScope]}
            supporting={`domain ${DOMAIN_LABELS[filteredIntel.dominantDomain].toLowerCase()}`}
            tone="info"
          />
          <IntelCard
            label="Latency"
            value={filteredIntel.avgLatencyMs ? formatPreciseDuration(filteredIntel.avgLatencyMs) : 'n/a'}
            supporting="structured latency / fallback parse"
            tone={filteredIntel.avgLatencyMs && filteredIntel.avgLatencyMs > 1500 ? 'warn' : 'neutral'}
          />
          <IntelCard
            label="Threads"
            value={String(filteredIntel.correlationCount)}
            supporting={`${filteredIntel.stackCount} stack | ${filteredIntel.jsonCount} json`}
            tone="neutral"
          />
        </div>

        <div className="flex flex-wrap gap-2">
          <InfoPill
            label={`schema v${logsSnapshot?.schemaVersion ?? 1} · semantics ${logsSnapshot?.semanticsVersion ?? 'legacy'}`}
            tone="info"
          />
          <InfoPill
            label={`structured lines ${semanticsCoverage.label}`}
            tone={semanticsCoverage.total === 0 ? 'neutral' : semanticsCoverage.structured === semanticsCoverage.total ? 'info' : 'warn'}
          />
          {(sourceIntel.markers.length > 0 ? sourceIntel.markers : [{ label: 'No stable patterns', count: 0 }]).map(marker => (
            <InfoPill
              key={marker.label}
              label={marker.count > 0 ? `${marker.label} x${marker.count}` : marker.label}
              tone={marker.label.toLowerCase().includes('http 5') ? 'warn' : 'neutral'}
            />
          ))}
        </div>
      </div>

      <div className="space-y-4 border-b border-slate-900/80 px-5 py-4">
        <FilterRow label="Severity" value={severityFilter} options={severityOptions} onChange={setSeverityFilter} />
        <FilterRow label="Scope" value={scopeFilter} options={scopeOptions} onChange={setScopeFilter} />
        <FilterRow label="Domain" value={domainFilter} options={domainOptions} onChange={setDomainFilter} />

        <div className={styles.filterGrid}>
          <div className={`pt-2 ${styles.kickerMedium} font-black text-slate-500`}>Text</div>
          <div className="space-y-2">
            <input
              value={textFilter}
              onChange={event => setTextFilter(event.target.value)}
              placeholder="Search message, route, correlation id, marker, source..."
              className="w-full rounded-2xl border border-slate-800 bg-slate-900/80 px-4 py-3 text-sm text-slate-100 outline-none transition-colors placeholder:text-slate-500 focus:border-cyan-400/50"
            />
            <div className={`${styles.metaText} text-slate-500`}>
              Search spans raw text, derived summary, scope/domain labels and extracted metadata.
            </div>
          </div>
        </div>
      </div>

      <div className={`${styles.logViewport} px-4 py-4`}>
        {sourceEntries.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/70 px-4 py-8 text-center text-slate-500">
            Runtime log tail is empty for the selected source.
          </div>
        ) : filteredEntries.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/70 px-4 py-8 text-center text-slate-500">
            No lines matched the current scope, severity, domain and text filters.
          </div>
        ) : (
          <div className="space-y-2">
            {filteredEntries.map(entry => (
              <LogLine key={`${entry.raw.sourceId}:${entry.raw.lineNumber}`} entry={entry} searchTokens={searchTokens} />
            ))}
          </div>
        )}
      </div>
    </section>
  );
}
