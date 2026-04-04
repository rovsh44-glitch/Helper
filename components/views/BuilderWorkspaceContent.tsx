import React from 'react';
import type { BuilderWorkspaceSelection, VirtualFile } from '../../types';
import { DiffViewer } from '../DiffViewer';
import type { BuilderVisibleMutation } from '../../hooks/useBuilderWorkspaceSession';
import { PanelResizeHandle } from '../layout/PanelResizeHandle';

type BuilderWorkspaceContentProps = {
  selectedFile: VirtualFile | null;
  selectedNode: BuilderWorkspaceSelection | null;
  editorContent: string;
  isDirty: boolean;
  buildLogs: string[];
  visibleMutation: BuilderVisibleMutation | null;
  activityDockWidth: number;
  onResizeActivityDock: (delta: number) => void;
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
  activityDockWidth,
  onResizeActivityDock,
  onEditorContentChange,
  onSave,
  onApproveMutation,
  onRejectMutation,
}) => (
  <div className="flex flex-1 min-h-0">
    <div className="flex min-h-0 min-w-0 flex-1 flex-col relative">
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
        <div className="h-full flex flex-col min-h-0">
          <div className="p-2 px-4 bg-slate-900 border-b border-slate-800 flex justify-between items-center">
            <div className="flex items-center gap-2 min-w-0">
              <span className="text-[10px] text-slate-500 font-mono truncate">{selectedFile.path}</span>
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
        <div className="flex-1 flex flex-col min-h-0">
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

    <PanelResizeHandle
      axis="x"
      title="Resize Builder activity dock"
      onResizeDelta={onResizeActivityDock}
    />

    <aside
      className="flex min-h-0 shrink-0 flex-col gap-4 border-l border-slate-800 bg-slate-950/90 p-4"
      style={{ width: `${activityDockWidth}px` }}
    >
      <div className="rounded-2xl border border-slate-800 bg-slate-900/80 p-4">
        <div className="text-[10px] uppercase tracking-[0.2em] text-slate-500">Workspace Activity</div>
        <div className="mt-3 space-y-2 text-xs text-slate-300">
          <div>
            <span className="text-slate-500">Selection:</span>{' '}
            {selectedFile?.path || selectedNode?.path || selectedNode?.label || 'workspace root'}
          </div>
          <div>
            <span className="text-slate-500">Mutation:</span>{' '}
            {visibleMutation ? `Pending review for ${visibleMutation.filePath}` : 'No pending mutation'}
          </div>
          <div>
            <span className="text-slate-500">Unsaved:</span>{' '}
            {isDirty ? 'editor has changes' : 'clean'}
          </div>
        </div>
      </div>

      <div className="flex min-h-0 flex-1 flex-col rounded-2xl border border-slate-800 bg-black/40">
        <div className="border-b border-slate-800 px-4 py-3">
          <div className="text-[10px] uppercase tracking-[0.2em] text-slate-500">Build Log Tail</div>
        </div>
        <div className="flex-1 overflow-y-auto p-4 font-mono text-[11px] text-green-400/85">
          {buildLogs.length === 0 ? (
            <div className="text-slate-500">No build output yet.</div>
          ) : (
            buildLogs.slice(-120).map((log, index) => (
              <div key={`${index}-${log}`} className="mb-1 whitespace-pre-wrap break-words">
                {log}
              </div>
            ))
          )}
        </div>
      </div>
    </aside>
  </div>
);
