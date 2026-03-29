import React from 'react';

export const SettingsSecurityPanel: React.FC<{ status: string }> = ({ status }) => (
  <div className="bg-slate-900 p-6 rounded-xl border border-slate-800">
    <h3 className="text-sm font-bold text-primary-400 uppercase mb-4">Security</h3>
    <div className="space-y-2">
      <label className="text-xs text-slate-500 uppercase">API Connectivity</label>
      <div className="flex-1 bg-black/40 border border-slate-800 rounded px-3 py-2 font-mono text-xs text-green-500">
        {status}
      </div>
      <p className="text-[10px] text-slate-600 italic">API key is provided by backend environment and never exposed in UI.</p>
    </div>
  </div>
);
