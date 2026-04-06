import React from 'react';
import type { BuilderWorkspaceSelection, GeneratedProject, VirtualFile, VirtualFolder } from '../../types';
import { BuilderNodeSheet, type BuilderNodeSheetState } from './BuilderNodeSheet';
import { FileTree } from '../FileTree';
import { navigateToTab } from '../../services/appShellRoute';

type BuilderWorkspaceSidebarProps = {
  project: GeneratedProject;
  selectedNode: BuilderWorkspaceSelection | null;
  workspacePathInput: string;
  workspaceError: string | null;
  isRefreshing: boolean;
  isBuilding: boolean;
  nodeSheet: BuilderNodeSheetState | null;
  onWorkspacePathChange: (value: string) => void;
  onOpenExistingProject: () => void;
  onRefreshWorkspace: () => void;
  onResetProject: () => void;
  onBuild: () => void;
  onOpenCreateNodeSheet: (isFolder: boolean) => void;
  onOpenRenameNodeSheet: () => void;
  onOpenDeleteNodeSheet: () => void;
  onNodeSheetValueChange: (value: string) => void;
  onSubmitNodeSheet: () => void;
  onCloseNodeSheet: () => void;
  onSelectFile: (file: VirtualFile) => void;
  onSelectFolder: (folder: VirtualFolder) => void;
  onProposeMutation: () => void;
};

export const BuilderWorkspaceSidebar: React.FC<BuilderWorkspaceSidebarProps> = ({
  project,
  selectedNode,
  workspacePathInput,
  workspaceError,
  isRefreshing,
  isBuilding,
  nodeSheet,
  onWorkspacePathChange,
  onOpenExistingProject,
  onRefreshWorkspace,
  onResetProject,
  onBuild,
  onOpenCreateNodeSheet,
  onOpenRenameNodeSheet,
  onOpenDeleteNodeSheet,
  onNodeSheetValueChange,
  onSubmitNodeSheet,
  onCloseNodeSheet,
  onSelectFile,
  onSelectFolder,
  onProposeMutation,
}) => (
  <div className="w-full h-full min-h-0 bg-slate-900 border-r border-slate-800 flex flex-col shadow-xl">
    <div className="p-4 border-b border-slate-800 bg-black/20 space-y-4">
      <div className="flex justify-between items-start gap-3">
        <div className="flex flex-col overflow-hidden">
          <span className="font-bold text-primary-400 text-[10px] uppercase tracking-[0.2em]">Live Builder</span>
          <span className="font-bold text-slate-200 text-sm truncate">{project.name}</span>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={onResetProject}
            className="text-[10px] border border-slate-700 hover:bg-slate-800 px-3 py-1.5 rounded text-slate-200 font-bold"
          >
            NEW
          </button>
          <button
            onClick={onBuild}
            disabled={isBuilding}
            className="text-[10px] bg-primary-600 hover:bg-primary-500 disabled:bg-slate-800 disabled:text-slate-500 px-3 py-1.5 rounded text-white font-bold shadow-lg shadow-primary-900/20"
          >
            {isBuilding ? '...' : 'BUILD'}
          </button>
        </div>
      </div>

      <div className="space-y-2">
        <label className="block text-[10px] uppercase tracking-[0.2em] text-slate-500">Workspace Path</label>
        <input
          value={workspacePathInput}
          onChange={(e) => onWorkspacePathChange(e.target.value)}
          className="w-full rounded-lg bg-slate-950 border border-slate-700 text-slate-100 px-3 py-2 text-xs focus:outline-none focus:border-primary-500"
          placeholder="Open an existing workspace path"
        />
        <div className="flex items-center gap-2">
          <button
            onClick={onOpenExistingProject}
            disabled={isRefreshing}
            className="flex-1 text-[10px] border border-primary-500/50 rounded-lg hover:bg-primary-500/10 disabled:opacity-50 disabled:cursor-not-allowed py-2 text-primary-100 font-bold"
          >
            {isRefreshing ? 'OPENING...' : 'OPEN'}
          </button>
          <button
            onClick={onRefreshWorkspace}
            disabled={isRefreshing}
            className="flex-1 text-[10px] border border-slate-700 rounded-lg hover:bg-slate-800 disabled:opacity-50 disabled:cursor-not-allowed py-2 text-slate-200 font-bold"
          >
            {isRefreshing ? 'REFRESHING...' : 'REFRESH'}
          </button>
        </div>
      </div>

      {workspaceError && (
        <div className="rounded-lg border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-[11px] text-rose-200">
          {workspaceError}
        </div>
      )}

      <div className="rounded-lg border border-slate-800 bg-slate-950/70 px-3 py-2">
        <div className="text-[10px] uppercase tracking-[0.2em] text-slate-500">Root</div>
        <div className="mt-1 text-[11px] text-slate-300 break-all">{project.fullPath}</div>
      </div>

      {project.launchContext && (
        <div className="rounded-lg border border-primary-500/20 bg-primary-950/20 px-3 py-3 space-y-2">
          <div className="text-[10px] uppercase tracking-[0.2em] text-primary-300">Attached Planner Context</div>
          <div className="text-[11px] text-slate-200">Route: {project.launchContext.routeTemplateId || 'none'}</div>
          <div className="text-[11px] text-slate-400">Blueprint: {project.launchContext.blueprintName || 'not specified'}</div>
          {project.launchContext.planSummary && (
            <div className="text-[11px] text-slate-400 leading-relaxed">{project.launchContext.planSummary}</div>
          )}
          <div className="flex gap-2 pt-1">
            <button
              onClick={() => navigateToTab('planner')}
              className="flex-1 rounded-lg border border-primary-500/40 px-3 py-2 text-[10px] font-bold text-primary-100 hover:bg-primary-500/10"
            >
              OPEN ARCHITECTURE
            </button>
            <button
              onClick={() => navigateToTab('strategy')}
              className="flex-1 rounded-lg border border-slate-700 px-3 py-2 text-[10px] font-bold text-slate-200 hover:bg-slate-800"
            >
              OPEN STRATEGY
            </button>
          </div>
        </div>
      )}

      <div className="rounded-lg border border-slate-800 bg-slate-950/70 px-3 py-3 space-y-3">
        <div className="text-[10px] uppercase tracking-[0.2em] text-slate-500">Workspace Actions</div>
        <div className="grid grid-cols-2 gap-2">
          <button
            onClick={() => onOpenCreateNodeSheet(false)}
            className="text-[10px] border border-slate-700 rounded-lg hover:bg-slate-800 py-2 text-slate-100 font-bold"
          >
            NEW FILE
          </button>
          <button
            onClick={() => onOpenCreateNodeSheet(true)}
            className="text-[10px] border border-slate-700 rounded-lg hover:bg-slate-800 py-2 text-slate-100 font-bold"
          >
            NEW FOLDER
          </button>
          <button
            onClick={onOpenRenameNodeSheet}
            disabled={!selectedNode || selectedNode.path === ''}
            className="text-[10px] border border-slate-700 rounded-lg hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed py-2 text-slate-100 font-bold"
          >
            RENAME
          </button>
          <button
            onClick={onOpenDeleteNodeSheet}
            disabled={!selectedNode || selectedNode.path === ''}
            className="text-[10px] border border-rose-500/30 rounded-lg hover:bg-rose-500/10 disabled:opacity-40 disabled:cursor-not-allowed py-2 text-rose-100 font-bold"
          >
            DELETE
          </button>
        </div>
        <div className="text-[11px] text-slate-400">
          {selectedNode
            ? `Selected ${selectedNode.kind}: ${selectedNode.path || selectedNode.label}`
            : `Selected workspace root: ${project.name}`}
        </div>
        {nodeSheet && (
          <BuilderNodeSheet
            projectName={project.name}
            sheet={nodeSheet}
            onChangeValue={onNodeSheetValueChange}
            onSubmit={onSubmitNodeSheet}
            onClose={onCloseNodeSheet}
          />
        )}
      </div>
    </div>

    <div className="flex-1 overflow-y-auto">
      <FileTree
        structure={project.root}
        onSelectFile={onSelectFile}
        onSelectFolder={onSelectFolder}
        selectedPath={selectedNode?.path}
        selectedKind={selectedNode?.kind}
      />
    </div>
    <div className="p-4 border-t border-slate-800 bg-black/40">
      <button
        onClick={onProposeMutation}
        className="w-full text-[9px] border border-indigo-500/50 text-indigo-400 hover:bg-indigo-500/10 py-2 rounded uppercase tracking-widest transition-all"
      >
        Propose Mutation
      </button>
    </div>
  </div>
);
