import React, { memo } from 'react';
import type { MessageDiagnosticsDeck, MessageDiagnosticsItem } from '../../types';

interface AssistantDiagnosticsDeckProps {
  deck: MessageDiagnosticsDeck;
}

export const AssistantDiagnosticsDeck = memo(function AssistantDiagnosticsDeck({
  deck,
}: AssistantDiagnosticsDeckProps) {
  return (
    <details className="mt-3 overflow-hidden rounded-xl border border-slate-700/80 bg-slate-950/40">
      <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-3 py-2.5">
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <span className="text-[10px] uppercase tracking-[0.18em] text-slate-500">
            {deck.title}
          </span>
          {deck.badges.map((badge, index) => (
            <span
              key={`${deck.title}-badge-${index}`}
              className={`rounded-full border px-2 py-1 text-[10px] uppercase tracking-wide ${getToneClass(badge.tone)}`}
            >
              {badge.label}: {badge.value}
            </span>
          ))}
        </div>
        <span className="shrink-0 text-[10px] uppercase tracking-wide text-slate-500">
          Inspect
        </span>
      </summary>

      <div className="border-t border-slate-800/80 px-3 py-3">
        <div className="space-y-3">
          {deck.sections.map(section => (
            <section key={section.id} className="space-y-2">
              <div className="text-[10px] uppercase tracking-[0.16em] text-slate-500">
                {section.title}
              </div>
              <div className={section.layout === 'grid' ? 'grid gap-2 md:grid-cols-2' : 'space-y-2'}>
                {section.items.map((item, index) => (
                  <DiagnosticsItemCard
                    key={`${section.id}-item-${index}`}
                    item={item}
                  />
                ))}
              </div>
            </section>
          ))}
        </div>
      </div>
    </details>
  );
});

function DiagnosticsItemCard({ item }: { item: MessageDiagnosticsItem }) {
  return (
    <div className={`rounded-lg border px-3 py-2 ${getTonePanelClass(item.tone)}`}>
      <div className="text-[10px] uppercase tracking-wide text-slate-500">
        {item.label}
      </div>
      <div className={`mt-1 text-xs text-slate-200 ${item.mono ? 'font-mono' : ''}`}>
        {item.value}
      </div>
    </div>
  );
}

function getToneClass(tone?: MessageDiagnosticsItem['tone']): string {
  switch (tone) {
    case 'positive':
      return 'border-emerald-700 bg-emerald-900/20 text-emerald-200';
    case 'info':
      return 'border-sky-700 bg-sky-900/20 text-sky-200';
    case 'warning':
      return 'border-amber-700 bg-amber-900/20 text-amber-100';
    case 'critical':
      return 'border-rose-700 bg-rose-900/20 text-rose-100';
    default:
      return 'border-slate-700 bg-slate-900/70 text-slate-300';
  }
}

function getTonePanelClass(tone?: MessageDiagnosticsItem['tone']): string {
  switch (tone) {
    case 'positive':
      return 'border-emerald-800/70 bg-emerald-950/20';
    case 'info':
      return 'border-sky-800/70 bg-sky-950/20';
    case 'warning':
      return 'border-amber-800/70 bg-amber-950/20';
    case 'critical':
      return 'border-rose-800/70 bg-rose-950/20';
    default:
      return 'border-slate-800/80 bg-slate-950/30';
  }
}
