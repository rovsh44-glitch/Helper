import React, { memo } from 'react';
import type { ProgressLogEntry } from '../../types';
import { ProgressPanel } from './ProgressPanel';

interface ActiveStreamingMessageProps {
  isProcessing: boolean;
  progressEntries: ProgressLogEntry[];
}

export const ActiveStreamingMessage = memo(function ActiveStreamingMessage({
  isProcessing,
  progressEntries,
}: ActiveStreamingMessageProps) {
  if (!isProcessing) {
    return null;
  }

  return (
    <div className="flex justify-start">
      <div className="max-w-4xl rounded-lg p-4 shadow-xl bg-slate-900/50 border border-slate-800/50 w-full">
        <div className="flex items-center gap-3 mb-4">
          <div className="flex gap-1">
            <div className="w-1.5 h-1.5 bg-primary-500 rounded-full animate-bounce"></div>
            <div className="w-1.5 h-1.5 bg-primary-500 rounded-full animate-bounce delay-100"></div>
            <div className="w-1.5 h-1.5 bg-primary-500 rounded-full animate-bounce delay-200"></div>
          </div>
          <span className="text-xs text-slate-400 font-mono uppercase">Processing Request...</span>
        </div>
        <ProgressPanel entries={progressEntries} />
      </div>
    </div>
  );
});
