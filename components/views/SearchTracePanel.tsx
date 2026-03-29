import React, { memo } from 'react';
import type { Message } from '../../types';

interface SearchTracePanelProps {
  trace?: Message['searchTrace'];
  fallbackSources?: string[];
}

export const SearchTracePanel = memo(function SearchTracePanel({
  trace,
  fallbackSources,
}: SearchTracePanelProps) {
  const visibleFallbackSources = !trace && fallbackSources
    ? fallbackSources.filter(Boolean)
    : [];

  if (!trace && visibleFallbackSources.length === 0) {
    return null;
  }

  const traceEvents = trace?.events?.filter(Boolean) ?? [];
  const traceSignals = trace?.signals?.filter(Boolean) ?? [];
  const traceSources = trace?.sources?.filter(source => source.title || source.url) ?? [];

  return (
    <details className="mt-3 overflow-hidden rounded-xl border border-slate-700/80 bg-slate-950/40">
      <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-3 py-2.5">
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <span className="text-[10px] uppercase tracking-[0.18em] text-slate-500">
            Search Trace
          </span>
          {trace && (
            <>
              {trace.inputMode && <TraceBadge>{trace.inputMode === 'voice' ? 'Voice Turn' : 'Text Turn'}</TraceBadge>}
              <TraceBadge>{formatMode(trace.requestedMode)}</TraceBadge>
              <TraceBadge>{formatRequirement(trace.resolvedRequirement)}</TraceBadge>
              <TraceBadge>{formatStatus(trace.status)}</TraceBadge>
              <TraceBadge>{`${traceSources.length || visibleFallbackSources.length} source(s)`}</TraceBadge>
            </>
          )}
          {!trace && (
            <TraceBadge>{`${visibleFallbackSources.length} source(s)`}</TraceBadge>
          )}
        </div>
        <span className="shrink-0 text-[10px] uppercase tracking-wide text-slate-500">
          Inspect
        </span>
      </summary>

      <div className="border-t border-slate-800/80 px-3 py-3">
        <div className="space-y-3">
          {trace && (
            <div className="grid gap-2 md:grid-cols-2">
              <TraceCard label="Input mode" value={trace.inputMode ?? 'text'} mono />
              <TraceCard label="Requested mode" value={formatMode(trace.requestedMode)} />
              <TraceCard label="Resolved requirement" value={formatRequirement(trace.resolvedRequirement)} />
              <TraceCard label="Status" value={formatStatus(trace.status)} />
              <TraceCard label="Reason" value={trace.reason ?? 'n/a'} mono />
            </div>
          )}

          {traceSignals.length > 0 && (
            <section className="space-y-2">
              <div className="text-[10px] uppercase tracking-[0.16em] text-slate-500">Signals</div>
              <div className="flex flex-wrap gap-2">
                {traceSignals.map(signal => (
                  <span
                    key={signal}
                    className="rounded-full border border-slate-700 bg-slate-900/70 px-2 py-1 text-[10px] uppercase tracking-wide text-slate-300"
                  >
                    {signal}
                  </span>
                ))}
              </div>
            </section>
          )}

          <section className="space-y-2">
            <div className="text-[10px] uppercase tracking-[0.16em] text-slate-500">Sources</div>
            <div className="space-y-2">
              {traceSources.map(source => (
                <article key={`${source.ordinal}-${source.url}-${source.title}`} className="rounded-lg border border-slate-800/80 bg-slate-950/30 px-3 py-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-[10px] uppercase tracking-wide text-slate-500">
                      [{source.ordinal}]
                    </span>
                    <span className="text-sm text-slate-100">
                      {source.title}
                    </span>
                    {source.evidenceKind && (
                      <TraceBadge>{source.evidenceKind}</TraceBadge>
                    )}
                    {typeof source.passageCount === 'number' && source.passageCount > 0 && (
                      <TraceBadge>{`${source.passageCount} passage(s)`}</TraceBadge>
                    )}
                  </div>
                  {source.url && (
                    <a
                      href={source.url}
                      target="_blank"
                      rel="noreferrer"
                      className="mt-2 block break-all text-xs text-sky-300 hover:text-sky-200"
                    >
                      {source.url}
                    </a>
                  )}
                  <div className="mt-2 flex flex-wrap gap-2 text-[11px] text-slate-400">
                    {source.publishedAt && <span>Published: {source.publishedAt}</span>}
                    {source.trustLevel && <span>Trust: {source.trustLevel}</span>}
                    {source.wasSanitized && <span>Sanitized</span>}
                  </div>
                  {source.snippet && (
                    <p className="mt-2 text-xs leading-relaxed text-slate-300">
                      {source.snippet}
                    </p>
                  )}
                  {source.safetyFlags && source.safetyFlags.length > 0 && (
                    <div className="mt-2 flex flex-wrap gap-2">
                      {source.safetyFlags.map(flag => (
                        <TraceBadge key={`${source.ordinal}-${flag}`}>{flag}</TraceBadge>
                      ))}
                    </div>
                  )}
                </article>
              ))}

              {traceSources.length === 0 && visibleFallbackSources.map((source, index) => (
                <article key={`${index + 1}-${source}`} className="rounded-lg border border-slate-800/80 bg-slate-950/30 px-3 py-3">
                  <div className="text-[10px] uppercase tracking-wide text-slate-500">[{index + 1}]</div>
                  <div className="mt-1 break-all text-xs text-slate-200">{source}</div>
                </article>
              ))}
            </div>
          </section>

          {traceEvents.length > 0 && (
            <section className="space-y-2">
              <div className="text-[10px] uppercase tracking-[0.16em] text-slate-500">Progress Trace</div>
              <div className="rounded-lg border border-slate-800/80 bg-black/20 px-3 py-3">
                <div className="space-y-1 font-mono text-[11px] text-slate-300">
                  {traceEvents.map(event => (
                    <div key={event}>{event}</div>
                  ))}
                </div>
              </div>
            </section>
          )}
        </div>
      </div>
    </details>
  );
});

function TraceCard({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-lg border border-slate-800/80 bg-slate-950/30 px-3 py-2">
      <div className="text-[10px] uppercase tracking-wide text-slate-500">{label}</div>
      <div className={`mt-1 text-xs text-slate-200 ${mono ? 'font-mono' : ''}`}>{value}</div>
    </div>
  );
}

function TraceBadge({ children }: { children: React.ReactNode }) {
  return (
    <span className="rounded-full border border-slate-700 bg-slate-900/70 px-2 py-1 text-[10px] uppercase tracking-wide text-slate-300">
      {children}
    </span>
  );
}

function formatMode(mode: NonNullable<Message['searchTrace']>['requestedMode']): string {
  switch (mode) {
    case 'force_search':
      return 'Force Search';
    case 'no_web':
      return 'No Web';
    default:
      return 'Auto';
  }
}

function formatRequirement(requirement: NonNullable<Message['searchTrace']>['resolvedRequirement']): string {
  switch (requirement) {
    case 'web_required':
      return 'Web Required';
    case 'web_helpful':
      return 'Web Helpful';
    default:
      return 'No Web Needed';
  }
}

function formatStatus(status: string): string {
  switch (status) {
    case 'executed_live_web':
      return 'Executed Live Web';
    case 'used_cached_web_result':
      return 'Used Cached Result';
    case 'disabled_by_user':
      return 'Disabled By User';
    case 'web_considered_but_not_executed':
      return 'Considered, Not Executed';
    default:
      return 'Not Needed';
  }
}
