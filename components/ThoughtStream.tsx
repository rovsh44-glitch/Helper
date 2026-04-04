import React, { memo } from 'react';
import { useHelperHubContext } from '../hooks/useHelperHubContext';

export const ThoughtStream = memo(function ThoughtStream() {
  const { thoughts } = useHelperHubContext();
  return (
    <div className="h-full min-h-0 w-full flex flex-col bg-slate-900 border-l border-slate-800">
      <div className="p-4 border-b border-slate-800 bg-black/20">
        <h3 className="text-xs font-bold text-primary-400 uppercase tracking-widest flex items-center gap-2">
          <span className="w-2 h-2 bg-primary-500 rounded-full animate-pulse"></span>
          Inner Monologue
        </h3>
      </div>
      <div className="flex-1 overflow-y-auto p-4 space-y-3 font-mono text-[10px]">
        {thoughts.length === 0 && (
          <div className="text-slate-600 italic">System is quiet. Prometheus is observing...</div>
        )}
        {thoughts.map((thought) => (
          <div key={thought.id} className="text-slate-400 border-l border-slate-700 pl-3 py-1">
            <span className="text-slate-600 block mb-1">[{new Date(thought.timestamp).toLocaleTimeString()}]</span>
            {thought.content}
          </div>
        ))}
      </div>
    </div>
  );
});
