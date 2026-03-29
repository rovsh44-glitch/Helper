import React from 'react';
import { ThoughtNode } from '../types';

interface ThoughtTreeProps {
  node: ThoughtNode;
  depth?: number;
}

export const ThoughtTree: React.FC<ThoughtTreeProps> = ({ node, depth = 0 }) => {
  const getScoreColor = (score: number) => {
    if (score >= 0.8) return 'text-green-400 border-green-900/50 bg-green-950/20';
    if (score >= 0.5) return 'text-yellow-400 border-yellow-900/50 bg-yellow-950/20';
    return 'text-red-400 border-red-900/50 bg-red-950/20';
  };

  return (
    <div className={`mt-2 border-l-2 ml-2 pl-4 transition-all hover:border-primary-500/50 ${depth === 0 ? 'border-primary-500/20' : 'border-slate-800'}`}>
      <div className={`p-3 rounded-lg border mb-2 text-xs font-mono shadow-sm ${getScoreColor(node.score)}`}>
        <div className="flex justify-between items-center mb-1 opacity-70">
          <span className="uppercase tracking-tighter text-[9px]">Level {depth} | Confidence {Math.round(node.score * 100)}%</span>
        </div>
        <div className="whitespace-pre-wrap leading-relaxed">{node.content}</div>
      </div>
      
      {node.children && node.children.length > 0 && (
        <div className="space-y-2 mt-3">
          {node.children.map((child, idx) => (
            <ThoughtTree key={idx} node={child} depth={depth + 1} />
          ))}
        </div>
      )}
    </div>
  );
};
