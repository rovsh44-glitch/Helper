import React, { memo } from 'react';
import type { ProgressLogEntry } from '../../types';

export const ProgressPanel = memo(function ProgressPanel({ entries }: { entries: ProgressLogEntry[] }) {
  if (entries.length === 0) {
    return null;
  }

  return (
    <div className="bg-black/40 rounded p-3 font-mono text-[11px] text-green-400/80 max-h-60 overflow-y-auto border border-white/5">
      {entries.map(entry => (
        <div key={entry.id} className="mb-1">
          <span className="text-slate-600 mr-2">[{new Date(entry.timestamp).toLocaleTimeString()}]</span>
          {entry.message}
        </div>
      ))}
    </div>
  );
});
