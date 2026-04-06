import React from 'react';

export type SettingsAlertItem = {
  id: string;
  title: string;
  message: string;
  tone: 'critical' | 'warning' | 'info';
  actionLabel?: string;
  onAction?: () => void;
};

type SettingsAlertsPanelProps = {
  items: SettingsAlertItem[];
};

export const SettingsAlertsPanel: React.FC<SettingsAlertsPanelProps> = ({ items }) => {
  if (items.length === 0) {
    return null;
  }

  return (
    <div className="rounded-xl border border-rose-900/40 bg-rose-950/20 p-4">
      <div className="mb-3 text-[10px] font-bold uppercase tracking-[0.2em] text-rose-300">Operator Alerts</div>
      <div className="space-y-3">
        {items.map(item => (
          <div
            key={item.id}
            className={`rounded-xl border px-4 py-3 ${
              item.tone === 'critical'
                ? 'border-rose-500/40 bg-rose-500/10 text-rose-100'
                : item.tone === 'warning'
                  ? 'border-amber-500/30 bg-amber-500/10 text-amber-100'
                  : 'border-cyan-500/30 bg-cyan-500/10 text-cyan-100'
            }`}
          >
            <div className="flex items-start justify-between gap-3">
              <div>
                <div className="text-[11px] font-bold uppercase tracking-[0.18em]">{item.title}</div>
                <div className="mt-1 text-sm leading-6">{item.message}</div>
              </div>
              {item.onAction && item.actionLabel && (
                <button
                  type="button"
                  onClick={item.onAction}
                  className="rounded-full border border-current/30 px-3 py-1 text-[10px] font-bold uppercase tracking-wide hover:bg-white/5"
                >
                  {item.actionLabel}
                </button>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};
