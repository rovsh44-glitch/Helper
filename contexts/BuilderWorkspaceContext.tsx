import React, { createContext, useContext, useMemo, useState, type Dispatch, type ReactNode, type SetStateAction } from 'react';
import type { BuilderWorkspaceSelection, GeneratedProject } from '../types';
import { projectService } from '../services/projectService';

type BuilderWorkspaceContextValue = {
  project: GeneratedProject | null;
  setProject: Dispatch<SetStateAction<GeneratedProject | null>>;
  selectedNode: BuilderWorkspaceSelection | null;
  setSelectedNode: Dispatch<SetStateAction<BuilderWorkspaceSelection | null>>;
  selectedFilePath: string | null;
  setSelectedFilePath: Dispatch<SetStateAction<string | null>>;
  editorContent: string;
  setEditorContent: Dispatch<SetStateAction<string>>;
  isDirty: boolean;
  setIsDirty: Dispatch<SetStateAction<boolean>>;
  buildLogs: string[];
  setBuildLogs: Dispatch<SetStateAction<string[]>>;
  createPrompt: string;
  setCreatePrompt: Dispatch<SetStateAction<string>>;
  createError: string | null;
  setCreateError: Dispatch<SetStateAction<string | null>>;
  workspacePathInput: string;
  setWorkspacePathInput: Dispatch<SetStateAction<string>>;
  workspaceError: string | null;
  setWorkspaceError: Dispatch<SetStateAction<string | null>>;
  clearWorkspaceSession: () => void;
};

const BuilderWorkspaceContext = createContext<BuilderWorkspaceContextValue | null>(null);

export function BuilderWorkspaceProvider({ children }: { children: ReactNode }) {
  const [project, setProject] = useState<GeneratedProject | null>(projectService.getCurrentProject());
  const [selectedNode, setSelectedNode] = useState<BuilderWorkspaceSelection | null>(null);
  const [selectedFilePath, setSelectedFilePath] = useState<string | null>(null);
  const [editorContent, setEditorContent] = useState('');
  const [isDirty, setIsDirty] = useState(false);
  const [buildLogs, setBuildLogs] = useState<string[]>([]);
  const [createPrompt, setCreatePrompt] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);
  const [workspacePathInput, setWorkspacePathInput] = useState(project?.fullPath ?? '');
  const [workspaceError, setWorkspaceError] = useState<string | null>(null);

  const value = useMemo<BuilderWorkspaceContextValue>(() => ({
    project,
    setProject,
    selectedNode,
    setSelectedNode,
    selectedFilePath,
    setSelectedFilePath,
    editorContent,
    setEditorContent,
    isDirty,
    setIsDirty,
    buildLogs,
    setBuildLogs,
    createPrompt,
    setCreatePrompt,
    createError,
    setCreateError,
    workspacePathInput,
    setWorkspacePathInput,
    workspaceError,
    setWorkspaceError,
    clearWorkspaceSession: () => {
      setProject(null);
      setSelectedNode(null);
      setSelectedFilePath(null);
      setEditorContent('');
      setIsDirty(false);
      setBuildLogs([]);
      setCreateError(null);
      setWorkspaceError(null);
      setWorkspacePathInput('');
    },
  }), [
    buildLogs,
    createError,
    createPrompt,
    editorContent,
    isDirty,
    project,
    selectedFilePath,
    selectedNode,
    workspaceError,
    workspacePathInput,
  ]);

  return (
    <BuilderWorkspaceContext.Provider value={value}>
      {children}
    </BuilderWorkspaceContext.Provider>
  );
}

export function useBuilderWorkspace() {
  const context = useContext(BuilderWorkspaceContext);
  if (!context) {
    throw new Error('useBuilderWorkspace must be used inside BuilderWorkspaceProvider.');
  }

  return context;
}
