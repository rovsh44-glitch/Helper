import React, { useMemo } from 'react';
import type { HumanLikeConversationDashboardSnapshotDto } from '../services/api/runtimeApi';

type HumanLikeConversationDashboardPanelProps = {
  snapshot: HumanLikeConversationDashboardSnapshotDto | null;
  error?: string | null;
  isRefreshing?: boolean;
  title?: string;
};

export function HumanLikeConversationDashboardPanel({
  snapshot,
  error = null,
  isRefreshing = false,
  title = 'Human-Like Conversation',
}: HumanLikeConversationDashboardPanelProps) {
  const summaryCards = useMemo(() => {
    if (!snapshot) {
      return [];
    }

    const summary = snapshot.summary;
    return [
      {
        label: 'Repeated phrase rate',
        value: formatPercent(summary.repeatedPhraseRate),
        sample: `${summary.styleTurns} styled turns`,
        note: 'Lower is better. Flags template-heavy openings.',
        tone: scoreTone(summary.styleTurns === 0 ? null : summary.repeatedPhraseRate, 0.18, 'lower_better'),
      },
      {
        label: 'Mixed language rate',
        value: formatPercent(summary.mixedLanguageRate),
        sample: `${summary.styleTurns} styled turns`,
        note: 'Lower is better. Should stay at zero.',
        tone: scoreTone(summary.styleTurns === 0 ? null : summary.mixedLanguageRate, 0.0, 'lower_better'),
      },
      {
        label: 'Clarification helpfulness',
        value: formatPercent(summary.clarificationHelpfulnessRate),
        sample: `${summary.helpfulClarificationTurns}/${summary.clarificationTurns} helpful`,
        note: 'Higher is better. Based on follow-up conversion or positive feedback.',
        tone: scoreTone(summary.clarificationTurns === 0 ? null : summary.clarificationHelpfulnessRate, 0.6, 'higher_better'),
      },
      {
        label: 'Repair success',
        value: formatPercent(summary.repairSuccessRate),
        sample: `${summary.repairSucceeded}/${summary.repairAttempts} successful`,
        note: 'Higher is better. Completed repair requests.',
        tone: scoreTone(summary.repairAttempts === 0 ? null : summary.repairSuccessRate, 0.75, 'higher_better'),
      },
      {
        label: 'Style feedback',
        value: summary.styleFeedbackVotes > 0 ? `${summary.styleFeedbackAverageRating.toFixed(2)}/5` : 'n/a',
        sample: `${summary.styleFeedbackVotes} votes`,
        note: `Lower tail: ${formatPercent(summary.styleLowRatingRate)} rated 1-2.`,
        tone: scoreTone(summary.styleFeedbackVotes === 0 ? null : summary.styleFeedbackAverageRating, 4.3, 'higher_better'),
      },
    ];
  }, [snapshot]);

  return (
    <section className="rounded-2xl border border-slate-800 bg-slate-950/85 p-5 shadow-2xl backdrop-blur">
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <div className="text-[10px] font-black uppercase tracking-[0.24em] text-slate-500">Conversation Quality</div>
          <div className="mt-1 text-lg font-semibold text-white">{title}</div>
          <p className="mt-2 text-xs text-slate-500">
            Live backend telemetry for human-like communication: repetition, language lock, clarification quality, repair recovery, and user style feedback.
          </p>
        </div>
        <div className="text-right text-[11px] uppercase tracking-[0.18em] text-slate-500">
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
          Conversation quality snapshot has not been loaded yet.
        </div>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
            {summaryCards.map(card => (
              <div key={card.label} className={`rounded-2xl border px-4 py-3 ${toneClass(card.tone)}`}>
                <div className="text-[10px] font-black uppercase tracking-[0.18em] text-slate-500">{card.label}</div>
                <div className="mt-2 text-lg font-semibold text-white">{card.value}</div>
                <div className="mt-1 text-[11px] text-slate-300">{card.sample}</div>
                <div className="mt-2 text-[11px] text-slate-500">{card.note}</div>
              </div>
            ))}
          </div>

          <div className="mt-5 grid gap-5 xl:grid-cols-[1.3fr_0.7fr]">
            <div className="space-y-3">
              <div>
                <div className="text-[10px] font-black uppercase tracking-[0.18em] text-slate-500">Daily Trend</div>
                <div className="mt-1 text-[11px] text-slate-500">
                  Window: last {snapshot.windowDays} day(s). Clarification helpfulness is a proxy based on follow-up conversion or positive feedback.
                </div>
              </div>
              <div className="overflow-hidden rounded-2xl border border-slate-800 bg-slate-900/70">
                <div className="grid grid-cols-[100px_repeat(5,minmax(0,1fr))] gap-3 border-b border-slate-800 px-4 py-3 text-[10px] font-black uppercase tracking-[0.18em] text-slate-500">
                  <div>Day</div>
                  <div>Repeat</div>
                  <div>Mixed</div>
                  <div>Clarify</div>
                  <div>Repair</div>
                  <div>Feedback</div>
                </div>
                <div className="divide-y divide-slate-800">
                  {snapshot.trend.map(point => (
                    <div key={point.dateUtc} className="grid grid-cols-[100px_repeat(5,minmax(0,1fr))] gap-3 px-4 py-3 text-sm text-slate-200">
                      <div className="font-medium text-white">{point.dateUtc}</div>
                      <MetricPill value={formatPercent(point.repeatedPhraseRate)} note={`${point.styleTurns} turns`} tone={scoreTone(point.styleTurns === 0 ? null : point.repeatedPhraseRate, 0.18, 'lower_better')} />
                      <MetricPill value={formatPercent(point.mixedLanguageRate)} note={`${point.styleTurns} turns`} tone={scoreTone(point.styleTurns === 0 ? null : point.mixedLanguageRate, 0.0, 'lower_better')} />
                      <MetricPill value={formatPercent(point.clarificationHelpfulnessRate)} note={`${point.helpfulClarificationTurns}/${point.clarificationTurns}`} tone={scoreTone(point.clarificationTurns === 0 ? null : point.clarificationHelpfulnessRate, 0.6, 'higher_better')} />
                      <MetricPill value={formatPercent(point.repairSuccessRate)} note={`${point.repairAttempts} attempts`} tone={scoreTone(point.repairAttempts === 0 ? null : point.repairSuccessRate, 0.75, 'higher_better')} />
                      <MetricPill value={point.styleFeedbackVotes > 0 ? point.styleFeedbackAverageRating.toFixed(2) : 'n/a'} note={`${point.styleFeedbackVotes} votes`} tone={scoreTone(point.styleFeedbackVotes === 0 ? null : point.styleFeedbackAverageRating, 4.3, 'higher_better')} />
                    </div>
                  ))}
                </div>
              </div>
            </div>

            <div className="space-y-3">
              <div>
                <div className="text-[10px] font-black uppercase tracking-[0.18em] text-slate-500">Operational Notes</div>
                <div className="mt-1 text-[11px] text-slate-500">
                  These alerts are derived from runtime telemetry, not from static parity documents.
                </div>
              </div>
              {snapshot.alerts.length > 0 ? snapshot.alerts.map(alert => (
                <div key={alert} className="rounded-2xl border border-amber-500/20 bg-amber-500/5 px-4 py-3 text-sm text-amber-100">
                  {alert}
                </div>
              )) : (
                <div className="rounded-2xl border border-emerald-500/20 bg-emerald-500/5 px-4 py-4 text-sm text-emerald-200">
                  No active conversation-quality alerts for the current window.
                </div>
              )}

              <div className="rounded-2xl border border-cyan-500/20 bg-cyan-500/5 px-4 py-4 text-[11px] text-cyan-100/90">
                Repair success tracks completed repair requests. Style feedback uses explicit user ratings on assistant turns. Missing votes are shown as n/a, not as positive performance.
              </div>
            </div>
          </div>
        </>
      )}
    </section>
  );
}

function MetricPill({
  value,
  note,
  tone,
}: {
  value: string;
  note: string;
  tone: 'good' | 'warn' | 'neutral';
}) {
  return (
    <div className={`rounded-xl border px-3 py-2 ${toneClass(tone)}`}>
      <div className="text-sm font-semibold text-white">{value}</div>
      <div className="mt-1 text-[10px] text-slate-400">{note}</div>
    </div>
  );
}

function scoreTone(
  value: number | null,
  threshold: number,
  direction: 'higher_better' | 'lower_better',
): 'good' | 'warn' | 'neutral' {
  if (value === null) {
    return 'neutral';
  }

  if (direction === 'higher_better') {
    return value >= threshold ? 'good' : 'warn';
  }

  return value <= threshold ? 'good' : 'warn';
}

function toneClass(tone: 'good' | 'warn' | 'neutral') {
  switch (tone) {
    case 'good':
      return 'border-emerald-500/20 bg-emerald-500/5 text-emerald-100';
    case 'warn':
      return 'border-amber-500/20 bg-amber-500/5 text-amber-100';
    default:
      return 'border-slate-800 bg-slate-900/70 text-slate-200';
  }
}

function formatPercent(value: number) {
  return `${Math.round(value * 100)}%`;
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
