import { useState } from 'react';
import type { BuilderLaunchRequest, DeploymentPlatform, GeneratedProject } from '../types';
import { projectService } from '../services/projectService';
import { findFileByBuildPath } from '../services/builderTree';
import type { BuilderBuildSummary } from './useBuilderWorkspaceRefresh';

type UseBuilderWorkspaceCommandsArgs = {
  createPrompt: string;
  onLaunchConsumed?: () => void;
  project: GeneratedProject | null;
  buildSummary: BuilderBuildSummary;
  resetWorkspace: () => void;
  setProject: (project: GeneratedProject) => void;
  setSelectedFilePath: (value: string | null) => void;
  setSelectedNode: (value: null) => void;
  setEditorContent: (value: string) => void;
  setIsDirty: (value: boolean) => void;
  setBuildLogs: (value: string[]) => void;
  setCreateError: (value: string | null) => void;
  setWorkspaceError: (value: string | null) => void;
  handleSelectFile: (file: { path: string; name: string; content: string; language: string }) => void;
  clearActivity: () => void;
  recordActivity: (entry: {
    kind: 'workspace' | 'build';
    summary: string;
    detail?: string;
    relatedPath?: string;
    tone?: 'neutral' | 'success' | 'warning' | 'danger';
  }) => void;
};

export function useBuilderWorkspaceCommands({
  createPrompt,
  onLaunchConsumed,
  project,
  buildSummary,
  resetWorkspace,
  setProject,
  setSelectedFilePath,
  setSelectedNode,
  setEditorContent,
  setIsDirty,
  setBuildLogs,
  setCreateError,
  setWorkspaceError,
  handleSelectFile,
  clearActivity,
  recordActivity,
}: UseBuilderWorkspaceCommandsArgs) {
  const [isCreating, setIsCreating] = useState(false);

  const createProjectFromPrompt = async (
    prompt: string,
    platform: DeploymentPlatform,
    request?: BuilderLaunchRequest,
  ) => {
    setIsCreating(true);
    setCreateError(null);
    setWorkspaceError(null);

    try {
      clearActivity();
      const createdProject = request
        ? await projectService.createProject(prompt, platform, request)
        : await projectService.createProject(prompt, platform);

      setProject({ ...createdProject });
      setSelectedFilePath(null);
      setSelectedNode(null);
      setEditorContent('');
      setIsDirty(false);
      setBuildLogs(request
        ? [
            `> Builder launch source: ${request.source}`,
            `> Route hint: ${request.routeTemplateId || 'none'}`,
            `> Blueprint: ${request.blueprintName || 'not specified'}`,
            `✅ Generated project: ${createdProject.name}`,
          ]
        : []);
      recordActivity({
        kind: 'workspace',
        summary: 'Builder launched',
        detail: createdProject.fullPath,
        relatedPath: createdProject.fullPath,
        tone: 'success',
      });
    } catch (error) {
      setCreateError(error instanceof Error ? error.message : 'Project generation failed.');
    } finally {
      setIsCreating(false);
      onLaunchConsumed?.();
    }
  };

  const handleCreate = async () => {
    const prompt = createPrompt.trim();
    if (!prompt) {
      setCreateError('Enter a project request before starting the generation cycle.');
      return;
    }

    await createProjectFromPrompt(prompt, DeploymentPlatform.CLI);
  };

  const handleResetProject = () => {
    resetWorkspace();
    clearActivity();
    recordActivity({
      kind: 'workspace',
      summary: 'Builder reset',
      detail: 'Workspace session cleared.',
      tone: 'neutral',
    });
  };

  const handleOpenPrimaryBuildError = () => {
    if (!project || !buildSummary.primaryError?.file) {
      return;
    }

    const file = findFileByBuildPath(project.root, buildSummary.primaryError.file);
    if (!file) {
      return;
    }

    handleSelectFile(file);
    recordActivity({
      kind: 'build',
      summary: 'Opened primary build error',
      detail: `${file.path}:${buildSummary.primaryError.line}`,
      relatedPath: file.path,
      tone: 'warning',
    });
  };

  return {
    isCreating,
    createProjectFromPrompt,
    handleCreate,
    handleResetProject,
    handleOpenPrimaryBuildError,
  };
}
