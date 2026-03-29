import React from 'react';
import { StrategicPlan } from '../types';

interface StrategicMindMapProps {
  plan: StrategicPlan | null;
}

export const StrategicMindMap: React.FC<StrategicMindMapProps> = ({ plan }) => {
  if (!plan) return (
    <div className="h-40 flex items-center justify-center border border-dashed border-slate-800 rounded-xl text-[10px] text-slate-600 uppercase tracking-widest">
      No strategy snapshot yet. Run Analyze Strategy to populate this map.
    </div>
  );

  return (
    <div className="flex flex-col gap-4">
      <div className="text-[10px] font-bold text-indigo-400 uppercase tracking-widest mb-2 flex items-center gap-2">
        <span className="w-2 h-2 bg-indigo-500 rounded-full animate-pulse"></span>
        Metacognitive Map
      </div>
      
      <div className="grid grid-cols-1 gap-3">
        {plan.options.map(option => (
          <div 
            key={option.id} 
            className={`p-4 rounded-xl border transition-all ${option.id === plan.selectedStrategyId ? 'bg-indigo-500/10 border-indigo-500/50 shadow-[0_0_20px_rgba(99,102,241,0.1)]' : 'bg-slate-900/40 border-slate-800 opacity-60'}`}
          >
            <div className="flex justify-between items-start mb-2">
              <span className="text-xs font-bold text-slate-200 uppercase">{option.id.replace('_', ' ')}</span>
              <span className="text-[10px] font-mono text-indigo-400">Confidence: {(option.confidenceScore * 100).toFixed(0)}%</span>
            </div>
            <p className="text-[10px] text-slate-400 leading-relaxed mb-3">{option.description}</p>
            
            <div className="flex flex-wrap gap-2">
              {option.suggestedTools?.map(tool => (
                <span key={tool} className="text-[8px] px-2 py-0.5 bg-black/60 border border-slate-700 rounded text-slate-500 uppercase font-mono">
                  🛠️ {tool}
                </span>
              ))}
            </div>
          </div>
        ))}
      </div>

      <div className="p-3 bg-black/40 border border-slate-800 rounded-lg">
        <div className="text-[9px] font-bold text-slate-500 uppercase mb-1">Reasoning</div>
        <p className="text-[10px] text-slate-300 italic">"{plan.reasoning}"</p>
      </div>
    </div>
  );
};
