import { useState } from 'react';
import type { BuilderLaunchRequest, DeploymentPlatform } from '../types';
import { useBuilderWorkspace } from '../contexts/BuilderWorkspaceContext';
import { projectService } from '../services/projectService';
import { useBuilderFileSelection } from './useBuilderFileSelection';
import { useBuilderLaunchHandoff } from './useBuilderLaunchHandoff';
import { useBuilderMutationFlow, type BuilderVisibleMutation } from './useBuilderMutationFlow';
import { useBuilderNodeSheetFlow } from './useBuilderNodeSheetFlow';
import { useBuilderWorkspaceRefresh } from './useBuilderWorkspaceRefresh';

type UseBuilderWorkspaceSessionArgs = {
  launchRequest?: BuilderLaunchRequest | null;
  onLaunchConsumed?: () => void;
};

export function useBuilderWorkspaceSession({
  launchRequest = null,
  onLaunchConsumed,
}: UseBuilderWorkspaceSessionArgs) {
  const {
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
    clearWorkspaceSession,
  } = useBuilderWorkspace();
  const [isCreating, setIsCreating] = useState(false);

  const {
    selectedFile,
    syncProjectFromService,
    handleSelectFile,
    handleSelectFolder,
    handleSave,
  } = useBuilderFileSelection({
    project,
    selectedNode,
    selectedFilePath,
    editorContent,
    isDirty,
    setProject,
    setSelectedNode,
    setSelectedFilePath,
    setEditorContent,
    setIsDirty,
    setWorkspaceError,
  });

  const mutationFlow = useBuilderMutationFlow({
    selectedFile,
    setBuildLogs,
    setEditorContent,
    setIsDirty,
  });

  const refreshFlow = useBuilderWorkspaceRefresh({
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
    syncProjectFromService: () => syncProjectFromService(),
    clearPreviewMutation: mutationFlow.clearPreviewMutation,
  });

  const nodeSheetFlow = useBuilderNodeSheetFlow({
    project,
    selectedNode,
    selectedFile,
    setSelectedNode,
    setSelectedFilePath,
    setEditorContent,
    setIsDirty,
    setWorkspaceError,
    setBuildLogs,
    syncProjectFromService,
  });

  const createProjectFromPrompt = async (
    prompt: string,
    platform: DeploymentPlatform,
    request?: BuilderLaunchRequest,
  ) => {
    setIsCreating(true);
    setCreateError(null);
    setWorkspaceError(null);

    try {
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
    } catch (error) {
      setCreateError(error instanceof Error ? error.message : 'Project generation failed.');
    } finally {
      setIsCreating(false);
      onLaunchConsumed?.();
    }
  };

  useBuilderLaunchHandoff({
    launchRequest,
    onLaunchConsumed,
    project,
    setCreatePrompt,
    setBuildLogs,
    createProjectFromPrompt,
  });

  const handleCreate = async () => {
    const prompt = createPrompt.trim();
    if (!prompt) {
      setCreateError('Enter a project request before starting the generation cycle.');
      return;
    }

    await createProjectFromPrompt(prompt, DeploymentPlatform.CLI);
  };

  return {
    project,
    selectedNode,
    selectedFile,
    editorContent,
    isDirty,
    buildLogs,
    createPrompt,
    createError,
    workspacePathInput,
    workspaceError,
    isCreating,
    isBuilding: refreshFlow.isBuilding,
    isRefreshing: refreshFlow.isRefreshing,
    nodeSheet: nodeSheetFlow.nodeSheet,
    visibleMutation: mutationFlow.visibleMutation,
    setCreatePrompt,
    setWorkspacePathInput,
    setEditorContent,
    setIsDirty,
    setNodeSheet: nodeSheetFlow.setNodeSheet,
    handleCreate,
    handleOpenExistingProject: refreshFlow.handleOpenExistingProject,
    handleRefreshWorkspace: refreshFlow.handleRefreshWorkspace,
    handleResetProject: refreshFlow.handleResetProject,
    handleBuild: refreshFlow.handleBuild,
    handleSave,
    handleProposeMutation: mutationFlow.handleProposeMutation,
    handleApproveMutation: mutationFlow.handleApproveMutation,
    handleRejectMutation: mutationFlow.handleRejectMutation,
    handleSelectFile,
    handleSelectFolder,
    openCreateNodeSheet: nodeSheetFlow.openCreateNodeSheet,
    openRenameNodeSheet: nodeSheetFlow.openRenameNodeSheet,
    openDeleteNodeSheet: nodeSheetFlow.openDeleteNodeSheet,
    handleNodeSheetValueChange: nodeSheetFlow.handleNodeSheetValueChange,
    handleSubmitNodeSheet: nodeSheetFlow.handleSubmitNodeSheet,
  };
}

export type { BuilderVisibleMutation };
