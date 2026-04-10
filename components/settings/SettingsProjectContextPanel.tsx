import React from 'react';
import type { ContinuityBackgroundTask, ContinuityProactiveTopic } from '../../services/settingsContinuityContracts';

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
}) => {
  const hasActiveProject = projectId.trim().length > 0;

  return (
  <div className="bg-slate-900 p-6 rounded-xl border border-slate-800 space-y-6">
    <div className="flex items-center justify-between gap-3">
      <div>
        <h3 className="text-sm font-bold text-primary-400 uppercase">Project Context</h3>
        <p className="text-[11px] text-slate-500 mt-1">Project-scoped instructions, memory boundary, and non-voice follow-through state for long-running collaboration.</p>
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
          emptyText={hasActiveProject
            ? 'No shared references have been attached to the active project yet.'
            : 'Set and save a project id to scope shared references to one active project.'}
          items={referenceArtifacts.map((item) => ({ id: item, label: item }))}
        />
        <ContinuityCard
          title="Background Tasks"
          emptyText={hasActiveProject
            ? 'No queued research follow-through tasks for the active project.'
            : 'No active project. Project-scoped background tasks appear here after a project context is saved.'}
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
          emptyText={hasActiveProject
            ? 'No proactive follow-up topics are registered for the active project.'
            : 'No active project. Project-scoped proactive topics appear here after a project context is saved.'}
          items={proactiveTopics.map((topic) => ({
            id: topic.id,
            label: topic.topic,
            meta: [topic.enabled ? 'enabled' : 'disabled', topic.frequency, topic.projectId].filter(Boolean).join(' · '),
            actionLabel: topic.enabled ? 'Disable' : 'Enable',
            onAction: () => onSetProactiveTopicEnabled(topic.id, !topic.enabled),
          }))}
        />
      </div>
    </div>
  </div>
  );
};

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
