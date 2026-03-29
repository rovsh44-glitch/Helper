import React from 'react';

type BuilderProjectLauncherProps = {
  createPrompt: string;
  createError: string | null;
  workspacePathInput: string;
  workspaceError: string | null;
  isCreating: boolean;
  isRefreshing: boolean;
  onCreatePromptChange: (value: string) => void;
  onCreate: () => void;
  onWorkspacePathChange: (value: string) => void;
  onOpenExistingProject: () => void;
};

export const BuilderProjectLauncher: React.FC<BuilderProjectLauncherProps> = ({
  createPrompt,
  createError,
  workspacePathInput,
  workspaceError,
  isCreating,
  isRefreshing,
  onCreatePromptChange,
  onCreate,
  onWorkspacePathChange,
  onOpenExistingProject,
}) => (
  <div className="p-10 h-full flex items-center justify-center">
    <div className="w-full max-w-5xl grid grid-cols-1 xl:grid-cols-2 gap-6">
      <div className="p-8 bg-slate-900 border border-slate-800 rounded-2xl shadow-2xl">
        <div className="text-lg font-bold text-white uppercase tracking-widest">Initialize Project Engine</div>
        <p className="text-xs text-slate-500 mt-2">Provide a concrete project request to start the Helper generation cycle.</p>
        <textarea
          value={createPrompt}
          onChange={(e) => onCreatePromptChange(e.target.value)}
          placeholder="Example: Build a C# console app for invoice tracking with SQLite and unit tests."
          className="mt-4 w-full h-36 rounded-xl bg-slate-950 border border-slate-700 text-slate-100 p-4 text-sm focus:outline-none focus:border-primary-500"
        />
        {createError && <p className="mt-3 text-xs text-rose-400">{createError}</p>}
        <div className="mt-4 flex justify-end">
          <button
            onClick={onCreate}
            disabled={isCreating}
            className="px-5 py-2 bg-primary-600 border border-primary-500 rounded-xl hover:bg-primary-500 transition-all text-white font-bold"
          >
            {isCreating ? 'Creating...' : 'Create Project'}
          </button>
        </div>
      </div>

      <div className="p-8 bg-slate-900 border border-slate-800 rounded-2xl shadow-2xl">
        <div className="text-lg font-bold text-white uppercase tracking-widest">Open Existing Workspace</div>
        <p className="text-xs text-slate-500 mt-2">Load an existing generated project from an allowed workspace path.</p>
        <input
          value={workspacePathInput}
          onChange={(e) => onWorkspacePathChange(e.target.value)}
          placeholder="Example: D:\\HELPER_DATA\\PROJECTS\\FORGE_OUTPUT\\Template_PdfEpubConverter_a0d52a"
          className="mt-4 w-full rounded-xl bg-slate-950 border border-slate-700 text-slate-100 p-4 text-sm focus:outline-none focus:border-primary-500"
        />
        {workspaceError && <p className="mt-3 text-xs text-rose-400">{workspaceError}</p>}
        <div className="mt-4 flex justify-end">
          <button
            onClick={onOpenExistingProject}
            disabled={isRefreshing}
            className="px-5 py-2 border border-primary-500/50 rounded-xl hover:bg-primary-500/10 transition-all text-primary-100 font-bold"
          >
            {isRefreshing ? 'Opening...' : 'Open Workspace'}
          </button>
        </div>
      </div>
    </div>
  </div>
);
