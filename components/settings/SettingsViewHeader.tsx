import React from 'react';

type SettingsViewHeaderProps = {
  actionStatus: string | null;
  onNavigateToRuntimeConsole: () => void;
  onNavigateToHelperCore: () => void;
  onCopyGovernanceSnapshot: () => void | Promise<void>;
  onExportGovernanceSnapshot: () => void;
  onFocusSection: (sectionId: string) => void;
};

const SECTION_LINKS = [
  { id: 'settings-alerts', label: 'Alerts' },
  { id: 'settings-infrastructure', label: 'Infrastructure' },
  { id: 'settings-provider-profiles', label: 'Providers' },
  { id: 'settings-runtime-doctor', label: 'Doctor' },
  { id: 'settings-capability-coverage', label: 'Capability' },
  { id: 'settings-conversation-quality', label: 'Quality' },
  { id: 'settings-memory-policy', label: 'Memory' },
] as const;

export const SettingsViewHeader: React.FC<SettingsViewHeaderProps> = ({
  actionStatus,
  onNavigateToRuntimeConsole,
  onNavigateToHelperCore,
  onCopyGovernanceSnapshot,
  onExportGovernanceSnapshot,
  onFocusSection,
}) => (
  <div className="mb-8 border-b border-slate-800 pb-4">
    <div className="flex flex-wrap items-end justify-between gap-4">
      <h2 className="text-3xl font-bold text-white">System Settings</h2>
      <div className="flex flex-wrap gap-2">
        <button
          onClick={onNavigateToRuntimeConsole}
          className="rounded-full border border-cyan-500/30 px-3 py-1.5 text-[10px] font-bold uppercase tracking-widest text-cyan-100 hover:border-cyan-400/60"
        >
          Runtime Console
        </button>
        <button
          onClick={onNavigateToHelperCore}
          className="rounded-full border border-primary-500/30 px-3 py-1.5 text-[10px] font-bold uppercase tracking-widest text-primary-100 hover:border-primary-400/60"
        >
          Helper Core
        </button>
        <button
          onClick={() => void onCopyGovernanceSnapshot()}
          className="rounded-full border border-slate-700 px-3 py-1.5 text-[10px] font-bold uppercase tracking-widest text-slate-200 hover:border-slate-500"
        >
          Copy Snapshot
        </button>
        <button
          onClick={onExportGovernanceSnapshot}
          className="rounded-full border border-emerald-500/30 px-3 py-1.5 text-[10px] font-bold uppercase tracking-widest text-emerald-100 hover:border-emerald-400/60"
        >
          Export Json
        </button>
      </div>
    </div>
    <div className="mt-4 flex flex-wrap gap-2">
      {SECTION_LINKS.map(link => (
        <button
          key={link.id}
          onClick={() => onFocusSection(link.id)}
          className="rounded-full border border-slate-700 px-3 py-1 text-[10px] font-bold uppercase tracking-widest text-slate-300 hover:border-slate-500"
        >
          {link.label}
        </button>
      ))}
    </div>
    {actionStatus && (
      <div className="mt-3 text-[11px] text-cyan-200/80">{actionStatus}</div>
    )}
  </div>
);
