import { useEffect, useState, type Dispatch, type SetStateAction } from 'react';
import type { BuilderWorkspaceSelection, GeneratedProject } from '../types';
import { projectService } from '../services/projectService';

type UseBuilderWorkspaceRefreshArgs = {
  project: GeneratedProject | null;
  setProject: Dispatch<SetStateAction<GeneratedProject | null>>;
  setSelectedNode: Dispatch<SetStateAction<BuilderWorkspaceSelection | null>>;
  setSelectedFilePath: Dispatch<SetStateAction<string | null>>;
  setEditorContent: Dispatch<SetStateAction<string>>;
  setIsDirty: Dispatch<SetStateAction<boolean>>;
  setBuildLogs: Dispatch<SetStateAction<string[]>>;
  workspacePathInput: string;
  setWorkspacePathInput: Dispatch<SetStateAction<string>>;
  setWorkspaceError: Dispatch<SetStateAction<string | null>>;
  clearWorkspaceSession: () => void;
  syncProjectFromService: () => void;
  clearPreviewMutation: () => void;
};

export function useBuilderWorkspaceRefresh({
  project,
  setProject,
  setSelectedNode,
  setSelectedFilePath,
  setEditorContent,
  setIsDirty,
  setBuildLogs,
  workspacePathInput,
  setWorkspacePathInput,
  setWorkspaceError,
  clearWorkspaceSession,
  syncProjectFromService,
  clearPreviewMutation,
}: UseBuilderWorkspaceRefreshArgs) {
  const [isBuilding, setIsBuilding] = useState(false);
  const [isRefreshing, setIsRefreshing] = useState(false);

  useEffect(() => {
    setWorkspacePathInput(project?.fullPath ?? '');
  }, [project?.fullPath, setWorkspacePathInput]);

  const handleBuild = async () => {
    setIsBuilding(true);
    setBuildLogs(['> Initializing build chain...', `> Target: ${project?.fullPath}`]);
    try {
      const result = await projectService.runBuild();
      if (result.success) {
        setBuildLogs(prev => [...prev, '✅ Build Successful!']);
      } else {
        const errors = result.errors.map((entry: { code: string; message: string; line: number }) =>
          `❌ ${entry.code}: ${entry.message} at line ${entry.line}`);
        setBuildLogs(prev => [...prev, ...errors]);
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Build failed.';
      setBuildLogs(prev => [...prev, `❌ ${message}`]);
    }
    setIsBuilding(false);
  };

  const handleResetProject = () => {
    projectService.clearCurrentProject();
    clearWorkspaceSession();
    clearPreviewMutation();
  };

  const handleOpenExistingProject = async () => {
    const path = workspacePathInput.trim();
    if (!path) {
      setWorkspaceError('Enter an existing workspace path to open it in Builder.');
      return;
    }

    setIsRefreshing(true);
    setWorkspaceError(null);
    try {
      const openedProject = await projectService.openProject(path);
      setProject({ ...openedProject });
      setSelectedFilePath(null);
      setSelectedNode(null);
      setEditorContent('');
      setIsDirty(false);
      setBuildLogs(prev => [...prev, `📂 Workspace opened: ${openedProject.fullPath}`]);
    } catch (error) {
      setWorkspaceError(error instanceof Error ? error.message : 'Failed to open workspace.');
    } finally {
      setIsRefreshing(false);
    }
  };

  const handleRefreshWorkspace = async () => {
    if (!project) {
      return;
    }

    setIsRefreshing(true);
    setWorkspaceError(null);
    try {
      await projectService.refreshProject();
      syncProjectFromService();
      setBuildLogs(prev => [...prev, `🔄 Workspace refreshed: ${project.fullPath}`]);
    } catch (error) {
      setWorkspaceError(error instanceof Error ? error.message : 'Workspace refresh failed.');
    } finally {
      setIsRefreshing(false);
    }
  };

  return {
    isBuilding,
    isRefreshing,
    handleBuild,
    handleResetProject,
    handleOpenExistingProject,
    handleRefreshWorkspace,
  };
}
