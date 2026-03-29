import React from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { ResearchResult } from '../types';

interface ResearchCardProps {
  result: ResearchResult;
}

export const ResearchCard: React.FC<ResearchCardProps> = ({ result }) => {
  return (
    <div className="mt-4 border border-slate-700 rounded-lg overflow-hidden bg-slate-900">
      <div className="bg-slate-800 p-3 border-b border-slate-700 flex justify-between items-center">
        <h3 className="font-bold text-primary-400 text-sm uppercase tracking-wider flex items-center gap-2">
          <span>🔬</span> Helper Research Report
        </h3>
        <span className="text-xs text-slate-500">{result.sources.length} Sources Analyzed</span>
      </div>
      
      <div className="p-4 grid gap-4">
        {/* Summary */}
        <div className="bg-slate-950/50 p-3 rounded border border-slate-800">
          <div className="text-xs font-bold text-slate-500 mb-1 uppercase">Executive Summary</div>
          <p className="text-sm text-slate-300 italic">{result.summary}</p>
        </div>

        {/* Findings */}
        <div>
          <div className="text-xs font-bold text-slate-500 mb-2 uppercase">Key Findings</div>
          <ul className="space-y-1">
            {result.keyFindings.map((finding, idx) => (
              <li key={idx} className="text-sm text-slate-300 flex gap-2">
                <span className="text-primary-500">•</span>
                {finding}
              </li>
            ))}
          </ul>
        </div>

        {/* Detailed Report */}
        <details className="group">
          <summary className="text-xs font-bold text-slate-500 mb-2 uppercase cursor-pointer hover:text-primary-400 transition-colors flex items-center gap-2">
            <span>Full Analysis</span>
            <span className="group-open:rotate-90 transition-transform">▶</span>
          </summary>
          <div className="prose prose-invert prose-sm max-w-none bg-black/20 p-4 rounded border border-white/5">
            <ReactMarkdown remarkPlugins={[remarkGfm]}>{result.fullReport}</ReactMarkdown>
          </div>
        </details>
      </div>
    </div>
  );
};
