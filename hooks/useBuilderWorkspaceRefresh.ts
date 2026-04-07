import type { BuildError } from '../types';
import { useEffect, useState, type Dispatch, type SetStateAction } from 'react';
import type { BuilderWorkspaceSelection, GeneratedProject } from '../types';
import { projectService } from '../services/projectService';

export type BuilderBuildSummary = {
  status: 'idle' | 'running' | 'success' | 'error';
  label: string;
  errorCount: number;
  warningCount: number;
  primaryError: BuildError | null;
  completedAtUtc: string | null;
  durationMs: number | null;
};

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
  onWorkspaceActivity?: (summary: string, detail?: string, tone?: 'neutral' | 'success' | 'warning' | 'danger', relatedPath?: string) => void;
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
  onWorkspaceActivity,
}: UseBuilderWorkspaceRefreshArgs) {
  const [isBuilding, setIsBuilding] = useState(false);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [buildSummary, setBuildSummary] = useState<BuilderBuildSummary>({
    status: 'idle',
    label: 'No build has been executed in this session yet.',
    errorCount: 0,
    warningCount: 0,
    primaryError: null,
    completedAtUtc: null,
    durationMs: null,
  });

  useEffect(() => {
    setWorkspacePathInput(project?.fullPath ?? '');
  }, [project?.fullPath, setWorkspacePathInput]);

  const handleBuild = async () => {
    const startedAt = Date.now();
    setIsBuilding(true);
    setBuildSummary({
      status: 'running',
      label: 'Build is running...',
      errorCount: 0,
      warningCount: 0,
      primaryError: null,
      completedAtUtc: null,
      durationMs: null,
    });
    setBuildLogs(['> Initializing build chain...', `> Target: ${project?.fullPath}`]);
    try {
      const result = await projectService.runBuild();
      const completedAtUtc = new Date().toISOString();
      const durationMs = Date.now() - startedAt;
      if (result.success) {
        setBuildLogs(prev => [...prev, '✅ Build Successful!']);
        setBuildSummary({
          status: 'success',
          label: 'Build completed successfully.',
          errorCount: 0,
          warningCount: 0,
          primaryError: null,
          completedAtUtc,
          durationMs,
        });
        onWorkspaceActivity?.('Build completed', project?.fullPath ?? 'Workspace build', 'success', project?.fullPath);
      } else {
        const errors = result.errors.map((entry: BuildError) =>
          `❌ ${entry.code}: ${entry.message} at line ${entry.line}`);
        setBuildLogs(prev => [...prev, ...errors]);
        setBuildSummary({
          status: 'error',
          label: `Build failed with ${result.errors.length} error(s).`,
          errorCount: result.errors.length,
          warningCount: 0,
          primaryError: result.errors[0] ?? null,
          completedAtUtc,
          durationMs,
        });
        onWorkspaceActivity?.(
          'Build failed',
          result.errors[0]?.message ?? `${result.errors.length} build errors`,
          'danger',
          result.errors[0]?.file,
        );
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Build failed.';
      setBuildLogs(prev => [...prev, `❌ ${message}`]);
      setBuildSummary({
        status: 'error',
        label: message,
        errorCount: 1,
        warningCount: 0,
        primaryError: null,
        completedAtUtc: new Date().toISOString(),
        durationMs: Date.now() - startedAt,
      });
      onWorkspaceActivity?.('Build failed', message, 'danger', project?.fullPath);
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
      onWorkspaceActivity?.('Workspace opened', openedProject.fullPath, 'success', openedProject.fullPath);
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
      onWorkspaceActivity?.('Workspace refreshed', project.fullPath, 'neutral', project.fullPath);
    } catch (error) {
      setWorkspaceError(error instanceof Error ? error.message : 'Workspace refresh failed.');
    } finally {
      setIsRefreshing(false);
    }
  };

  return {
    buildSummary,
    isBuilding,
    isRefreshing,
    handleBuild,
    handleResetProject,
    handleOpenExistingProject,
    handleRefreshWorkspace,
  };
}
