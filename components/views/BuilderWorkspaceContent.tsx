import React from 'react';
import type { BuilderWorkspaceSelection, VirtualFile } from '../../types';
import { DiffViewer } from '../DiffViewer';
import type { BuilderVisibleMutation } from '../../hooks/useBuilderWorkspaceSession';

type BuilderWorkspaceContentProps = {
  selectedFile: VirtualFile | null;
  selectedNode: BuilderWorkspaceSelection | null;
  editorContent: string;
  isDirty: boolean;
  buildLogs: string[];
  visibleMutation: BuilderVisibleMutation | null;
  onEditorContentChange: (value: string) => void;
  onSave: () => void;
  onApproveMutation: () => void;
  onRejectMutation: () => void;
};

function BuilderLogPanel({ buildLogs }: { buildLogs: string[] }) {
  if (buildLogs.length === 0) {
    return null;
  }

  return (
    <div className="h-40 bg-black border-t border-slate-800 p-4 overflow-y-auto font-mono text-[11px] text-green-500/90 selection:bg-primary-500/30">
      {buildLogs.map((log, index) => <div key={index} className="mb-1">{log}</div>)}
    </div>
  );
}

export const BuilderWorkspaceContent: React.FC<BuilderWorkspaceContentProps> = ({
  selectedFile,
  selectedNode,
  editorContent,
  isDirty,
  buildLogs,
  visibleMutation,
  onEditorContentChange,
  onSave,
  onApproveMutation,
  onRejectMutation,
}) => (
  <div className="flex-1 flex flex-col relative">
    {visibleMutation && (
      <div className="absolute inset-0 z-50 p-6 bg-slate-950/90 backdrop-blur-sm">
        <DiffViewer
          filename={visibleMutation.filePath}
          oldCode={visibleMutation.oldCode}
          newCode={visibleMutation.newCode}
          onApprove={onApproveMutation}
          onReject={onRejectMutation}
        />
      </div>
    )}

    {selectedFile ? (
      <div className="h-full flex flex-col">
        <div className="p-2 px-4 bg-slate-900 border-b border-slate-800 flex justify-between items-center">
          <div className="flex items-center gap-2">
            <span className="text-[10px] text-slate-500 font-mono">{selectedFile.path}</span>
            {isDirty && <span className="w-1.5 h-1.5 bg-yellow-500 rounded-full animate-pulse"></span>}
          </div>
          <button
            onClick={onSave}
            disabled={!isDirty}
            className={`text-[10px] px-4 py-1.5 rounded font-bold transition-all ${isDirty ? 'bg-green-600 text-white shadow-lg shadow-green-900/20' : 'bg-slate-800 text-slate-500'}`}
          >
            SAVE CHANGES
          </button>
        </div>
        <textarea
          className="flex-1 w-full bg-transparent text-slate-300 font-mono text-sm p-6 focus:outline-none resize-none leading-relaxed"
          spellCheck={false}
          value={editorContent}
          onChange={(e) => onEditorContentChange(e.target.value)}
        />
        <BuilderLogPanel buildLogs={buildLogs} />
      </div>
    ) : (
      <div className="flex-1 flex flex-col">
        <div className="flex-1 flex flex-col items-center justify-center text-slate-600 opacity-50 px-8 text-center">
          <div className="text-6xl mb-4">{selectedNode?.kind === 'folder' ? '🗂️' : '📂'}</div>
          <div className="text-sm font-mono uppercase tracking-tighter">
            {selectedNode?.kind === 'folder'
              ? `Folder selected: ${selectedNode.label}`
              : 'Select a file to examine or modify source'}
          </div>
          <div className="mt-3 max-w-lg text-xs text-slate-500">
            {selectedNode?.kind === 'folder'
              ? 'Use the workspace actions on the left to create, rename, or delete nodes inside the current selection.'
              : 'Builder can now open existing workspaces, refresh them from disk, and manage a minimal file lifecycle.'}
          </div>
        </div>
        <BuilderLogPanel buildLogs={buildLogs} />
      </div>
    )}
  </div>
);
