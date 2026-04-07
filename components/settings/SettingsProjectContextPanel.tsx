import React from 'react';
import type { ContinuityBackgroundTask, ContinuityLiveVoiceSession, ContinuityProactiveTopic } from '../../services/settingsContinuityContracts';

type SettingsProjectContextPanelProps = {
  projectId: string;
  projectLabel: string;
  projectInstructions: string;
  projectMemoryEnabled: boolean;
  backgroundResearchEnabled: boolean;
  proactiveUpdatesEnabled: boolean;
  referenceArtifacts: string[];
  backgroundTasks: ContinuityBackgroundTask[];
  proactiveTopics: ContinuityProactiveTopic[];
  liveVoiceSession: ContinuityLiveVoiceSession | null;
  onSetProjectId: (value: string) => void;
  onSetProjectLabel: (value: string) => void;
  onSetProjectInstructions: (value: string) => void;
  onSetProjectMemoryEnabled: (value: boolean) => void;
  onSetBackgroundResearchEnabled: (value: boolean) => void;
  onSetProactiveUpdatesEnabled: (value: boolean) => void;
  onSaveProjectContext: () => void;
  onSaveContinuityControls: () => void;
  onCancelBackgroundTask: (taskId: string) => void;
  onSetProactiveTopicEnabled: (topicId: string, enabled: boolean) => void;
};

export const SettingsProjectContextPanel: React.FC<SettingsProjectContextPanelProps> = ({
  projectId,
  projectLabel,
  projectInstructions,
  projectMemoryEnabled,
  backgroundResearchEnabled,
  proactiveUpdatesEnabled,
  referenceArtifacts,
  backgroundTasks,
  proactiveTopics,
  liveVoiceSession,
  onSetProjectId,
  onSetProjectLabel,
  onSetProjectInstructions,
  onSetProjectMemoryEnabled,
  onSetBackgroundResearchEnabled,
  onSetProactiveUpdatesEnabled,
  onSaveProjectContext,
  onSaveContinuityControls,
  onCancelBackgroundTask,
  onSetProactiveTopicEnabled,
}) => (
  <div className="bg-slate-900 p-6 rounded-xl border border-slate-800 space-y-6">
    <div className="flex items-center justify-between gap-3">
      <div>
        <h3 className="text-sm font-bold text-primary-400 uppercase">Project Context</h3>
        <p className="text-[11px] text-slate-500 mt-1">Project-scoped instructions, async follow-through controls, and continuity state for long-running collaboration.</p>
      </div>
      <button
        type="button"
        onClick={onSaveProjectContext}
        className="rounded-full border border-emerald-500/30 px-3 py-1.5 text-[10px] font-bold uppercase tracking-widest text-emerald-100 hover:border-emerald-400/60"
      >
        Save Project Context
      </button>
    </div>

    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      <label className="space-y-2">
        <span className="text-xs text-slate-500 uppercase">Project Id</span>
        <input value={projectId} onChange={(e) => onSetProjectId(e.target.value)} className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200" />
      </label>
      <label className="space-y-2">
        <span className="text-xs text-slate-500 uppercase">Project Label</span>
        <input value={projectLabel} onChange={(e) => onSetProjectLabel(e.target.value)} className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200" />
      </label>
      <label className="space-y-2 md:col-span-2">
        <span className="text-xs text-slate-500 uppercase">Project Instructions</span>
        <textarea value={projectInstructions} onChange={(e) => onSetProjectInstructions(e.target.value)} rows={4} className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200" />
      </label>
    </div>

    <label className="flex items-center gap-3 text-xs text-slate-300">
      <input type="checkbox" checked={projectMemoryEnabled} onChange={(e) => onSetProjectMemoryEnabled(e.target.checked)} />
      Keep memory boundary project-scoped when this project is active
    </label>

    <div className="rounded-xl border border-slate-800 bg-black/20 p-4 space-y-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <div className="text-xs font-semibold uppercase tracking-wide text-slate-300">Async Follow-Through</div>
          <p className="mt-1 text-[11px] text-slate-500">Opt into queued background research and transparent proactive re-engagement.</p>
        </div>
        <button
          type="button"
          onClick={onSaveContinuityControls}
          className="rounded-full border border-primary-500/30 px-3 py-1.5 text-[10px] font-bold uppercase tracking-widest text-primary-100 hover:border-primary-400/60"
        >
          Save Follow-Through
        </button>
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <label className="flex items-center gap-3 text-xs text-slate-300">
          <input type="checkbox" checked={backgroundResearchEnabled} onChange={(e) => onSetBackgroundResearchEnabled(e.target.checked)} />
          Queue background research follow-through when this conversation enters research mode
        </label>
        <label className="flex items-center gap-3 text-xs text-slate-300">
          <input type="checkbox" checked={proactiveUpdatesEnabled} onChange={(e) => onSetProactiveUpdatesEnabled(e.target.checked)} />
          Allow proactive updates for saved follow-up topics in this conversation
        </label>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <ContinuityCard
          title="Reference Artifacts"
          emptyText="No shared multimodal references have been attached to the active project yet."
          items={referenceArtifacts.map((item) => ({ id: item, label: item }))}
        />
        <ContinuityCard
          title="Background Tasks"
          emptyText="No queued research follow-through tasks."
          items={backgroundTasks.map((task) => ({
            id: task.id,
            label: task.title,
            meta: [task.status, task.projectId].filter(Boolean).join(' · '),
            actionLabel: task.status === 'completed' || task.status === 'canceled' ? undefined : 'Cancel',
            onAction: task.status === 'completed' || task.status === 'canceled'
              ? undefined
              : () => onCancelBackgroundTask(task.id),
          }))}
        />
        <ContinuityCard
          title="Proactive Topics"
          emptyText="No proactive follow-up topics are registered."
          items={proactiveTopics.map((topic) => ({
            id: topic.id,
            label: topic.topic,
            meta: [topic.enabled ? 'enabled' : 'disabled', topic.frequency, topic.projectId].filter(Boolean).join(' · '),
            actionLabel: topic.enabled ? 'Disable' : 'Enable',
            onAction: () => onSetProactiveTopicEnabled(topic.id, !topic.enabled),
          }))}
        />
      </div>

      <div className="rounded-xl border border-slate-800 bg-slate-950/60 px-4 py-3 text-xs text-slate-300">
        <div className="text-[10px] uppercase tracking-wide text-slate-500">Live Voice Session</div>
        {liveVoiceSession ? (
          <div className="mt-2 space-y-1">
            <div>Status: <span className="text-slate-100">{liveVoiceSession.status}</span></div>
            <div>Language: <span className="text-slate-100">{liveVoiceSession.language}</span></div>
            <div>Runtime: <span className="text-slate-100">{liveVoiceSession.runtimeKind}</span></div>
            <div>Interruptions: <span className="text-slate-100">{liveVoiceSession.interruptionsEnabled ? 'enabled' : 'disabled'}</span></div>
            <div>Hold state: <span className="text-slate-100">{liveVoiceSession.isHeld ? 'held' : 'active'}</span></div>
            <div>Attached references: <span className="text-slate-100">{liveVoiceSession.attachedReferenceCount}</span></div>
            <div>Capture chunks: <span className="text-slate-100">{liveVoiceSession.captureChunkCount}</span></div>
            <div>Approx duration: <span className="text-slate-100">{Math.max(0, Math.round(liveVoiceSession.approximateDurationMs / 1000))}s</span></div>
            <div>Hold checkpoints: <span className="text-slate-100">{liveVoiceSession.holdCount}</span></div>
            <div>Resume checkpoints: <span className="text-slate-100">{liveVoiceSession.resumeCount}</span></div>
            {liveVoiceSession.lastTranscript && (
              <div>Last transcript: <span className="text-slate-100">{liveVoiceSession.lastTranscript}</span></div>
            )}
            {liveVoiceSession.lastReferenceSummary && (
              <div>Last reference summary: <span className="text-slate-100">{liveVoiceSession.lastReferenceSummary}</span></div>
            )}
            {liveVoiceSession.transcriptSegments && liveVoiceSession.transcriptSegments.length > 0 && (
              <div>
                Transcript continuity:
                <div className="mt-1 space-y-1">
                  {liveVoiceSession.transcriptSegments.slice(0, 4).map((segment, index) => (
                    <div key={`${index}:${segment}`} className="text-slate-100">{segment}</div>
                  ))}
                </div>
              </div>
            )}
            {liveVoiceSession.recentChunks && liveVoiceSession.recentChunks.length > 0 && (
              <div>
                Recent chunks:
                <div className="mt-1 space-y-1">
                  {liveVoiceSession.recentChunks.slice(-4).map((chunk) => (
                    <div key={`${chunk.sequence}:${chunk.capturedAtUtc}`} className="text-slate-100">
                      #{chunk.sequence} · {Math.max(0, Math.round(chunk.durationMs / 1000))}s · {chunk.byteCount} bytes
                      {chunk.transcript ? ` · ${chunk.transcript}` : ''}
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        ) : (
          <div className="mt-2 text-slate-500">No live voice continuity has been captured for this conversation.</div>
        )}
      </div>
    </div>
  </div>
);

type ContinuityCardProps = {
  title: string;
  emptyText: string;
  items: Array<{ id: string; label: string; meta?: string; actionLabel?: string; onAction?: () => void }>;
};

const ContinuityCard: React.FC<ContinuityCardProps> = ({ title, emptyText, items }) => (
  <div className="rounded-xl border border-slate-800 bg-slate-950/60 px-4 py-3">
    <div className="text-[10px] uppercase tracking-wide text-slate-500">{title}</div>
    {items.length === 0 ? (
      <div className="mt-2 text-xs text-slate-500">{emptyText}</div>
    ) : (
      <div className="mt-2 space-y-2">
        {items.slice(0, 6).map((item) => (
          <div key={item.id} className="rounded-lg border border-slate-800 bg-black/20 px-3 py-2 text-xs text-slate-200">
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0 flex-1">{item.label}</div>
              {item.actionLabel && item.onAction && (
                <button
                  type="button"
                  onClick={item.onAction}
                  className="shrink-0 rounded-full border border-slate-700 px-2 py-0.5 text-[10px] uppercase tracking-wide text-slate-300 hover:border-slate-500"
                >
                  {item.actionLabel}
                </button>
              )}
            </div>
            {item.meta && <div className="mt-1 text-[11px] text-slate-500">{item.meta}</div>}
          </div>
        ))}
      </div>
    )}
  </div>
);
