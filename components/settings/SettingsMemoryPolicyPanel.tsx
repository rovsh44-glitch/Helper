import React from 'react';

type SettingsMemoryPolicyPanelProps = {
  memoryStatus: string;
  isLoadingMemory: boolean;
  memoryEnabled: boolean;
  personalConsent: boolean;
  sessionTtlMinutes: number;
  taskTtlHours: number;
  longTermTtlDays: number;
  isSavingPreferences: boolean;
  onSetMemoryEnabled: (value: boolean) => void;
  onSetPersonalConsent: (value: boolean) => void;
  onSetSessionTtlMinutes: (value: number) => void;
  onSetTaskTtlHours: (value: number) => void;
  onSetLongTermTtlDays: (value: number) => void;
  onSavePreferences: (override?: {
    longTermMemoryEnabled?: boolean;
    personalMemoryConsentGranted?: boolean;
    sessionMemoryTtlMinutes?: number;
    taskMemoryTtlHours?: number;
    longTermMemoryTtlDays?: number;
  }) => void;
};

export const SettingsMemoryPolicyPanel: React.FC<SettingsMemoryPolicyPanelProps> = ({
  memoryStatus,
  isLoadingMemory,
  memoryEnabled,
  personalConsent,
  sessionTtlMinutes,
  taskTtlHours,
  longTermTtlDays,
  isSavingPreferences,
  onSetMemoryEnabled,
  onSetPersonalConsent,
  onSetSessionTtlMinutes,
  onSetTaskTtlHours,
  onSetLongTermTtlDays,
  onSavePreferences,
}) => (
  <div className="bg-slate-900 p-6 rounded-xl border border-slate-800">
    <h3 className="text-sm font-bold text-primary-400 uppercase mb-4">Memory Privacy</h3>
    <div className="text-[11px] text-slate-500 mb-4">
      {isLoadingMemory ? 'Refreshing memory policy...' : memoryStatus}
    </div>
    <label className="flex items-center gap-3 text-xs text-slate-300">
      <input
        type="checkbox"
        checked={memoryEnabled}
        onChange={(e) => {
          onSetMemoryEnabled(e.target.checked);
          onSavePreferences({ longTermMemoryEnabled: e.target.checked });
        }}
      />
      Enable long-term memory for this conversation
    </label>
    <label className="flex items-center gap-3 text-xs text-slate-300 mt-3">
      <input
        type="checkbox"
        checked={personalConsent}
        onChange={(e) => {
          onSetPersonalConsent(e.target.checked);
          onSavePreferences({ personalMemoryConsentGranted: e.target.checked });
        }}
      />
      Explicitly allow storing personal long-term facts ("remember: ...")
    </label>
    <div className="grid grid-cols-3 gap-3 mt-4">
      <label className="text-[10px] text-slate-500 uppercase">
        Session TTL (min)
        <input
          type="number"
          min={15}
          max={1440}
          value={sessionTtlMinutes}
          onChange={(e) => {
            const value = Number(e.target.value || 0);
            onSetSessionTtlMinutes(value);
            onSavePreferences({ sessionMemoryTtlMinutes: value });
          }}
          className="w-full mt-1 bg-black/40 border border-slate-800 rounded px-2 py-1 text-xs text-slate-200"
        />
      </label>
      <label className="text-[10px] text-slate-500 uppercase">
        Task TTL (hours)
        <input
          type="number"
          min={1}
          max={1440}
          value={taskTtlHours}
          onChange={(e) => {
            const value = Number(e.target.value || 0);
            onSetTaskTtlHours(value);
            onSavePreferences({ taskMemoryTtlHours: value });
          }}
          className="w-full mt-1 bg-black/40 border border-slate-800 rounded px-2 py-1 text-xs text-slate-200"
        />
      </label>
      <label className="text-[10px] text-slate-500 uppercase">
        Long-term TTL (days)
        <input
          type="number"
          min={1}
          max={3650}
          value={longTermTtlDays}
          onChange={(e) => {
            const value = Number(e.target.value || 0);
            onSetLongTermTtlDays(value);
            onSavePreferences({ longTermMemoryTtlDays: value });
          }}
          className="w-full mt-1 bg-black/40 border border-slate-800 rounded px-2 py-1 text-xs text-slate-200"
        />
      </label>
    </div>
    <div className="mt-4 text-[10px] text-slate-500">
      {isSavingPreferences ? 'Saving preferences...' : 'Preference updates persist to the active conversation policy.'}
    </div>
  </div>
);
