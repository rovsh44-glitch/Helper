import React from 'react';

interface DiffViewerProps {
  filename: string;
  oldCode: string;
  newCode: string;
  onApprove: () => void;
  onReject: () => void;
}

export const DiffViewer: React.FC<DiffViewerProps> = ({ filename, oldCode, newCode, onApprove, onReject }) => {
  // Simple line-by-line diff visualization
  const oldLines = oldCode.split('\n');
  const newLines = newCode.split('\n');

  return (
    <div className="flex flex-col h-full bg-slate-900 border border-indigo-500/30 rounded-lg overflow-hidden shadow-2xl shadow-indigo-500/10">
      <div className="bg-slate-800 p-3 border-b border-indigo-500/30 flex justify-between items-center">
        <span className="text-indigo-300 font-mono text-sm">Proposed Mutation: {filename}</span>
        <div className="flex gap-2">
          <button 
            onClick={onReject}
            className="px-3 py-1 bg-red-500/20 hover:bg-red-500/40 text-red-300 border border-red-500/50 rounded text-xs transition-colors"
          >
            Reject
          </button>
          <button 
            onClick={onApprove}
            className="px-3 py-1 bg-green-500/20 hover:bg-green-500/40 text-green-300 border border-green-500/50 rounded text-xs transition-colors"
          >
            Apply & Restart
          </button>
        </div>
      </div>
      
      <div className="flex-1 overflow-auto p-4 font-mono text-xs flex">
        {/* Left Side: Old */}
        <div className="flex-1 border-r border-slate-700 pr-2">
            <div className="text-slate-500 mb-2 uppercase tracking-tighter">Current</div>
            {oldLines.map((line, i) => (
                <div key={i} className="whitespace-pre hover:bg-slate-800/50 px-1">
                    <span className="text-slate-600 w-8 inline-block select-none">{i+1}</span>
                    <span className="text-slate-300">{line}</span>
                </div>
            ))}
        </div>
        {/* Right Side: New */}
        <div className="flex-1 pl-2">
            <div className="text-indigo-400 mb-2 uppercase tracking-tighter">Proposed</div>
            {newLines.map((line, i) => {
                const isDifferent = line !== oldLines[i];
                return (
                    <div key={i} className={`whitespace-pre px-1 ${isDifferent ? 'bg-green-500/10' : 'hover:bg-slate-800/50'}`}>
                        <span className="text-slate-600 w-8 inline-block select-none">{i+1}</span>
                        <span className={isDifferent ? 'text-green-300' : 'text-slate-300'}>{line}</span>
                    </div>
                );
            })}
        </div>
      </div>
    </div>
  );
};
