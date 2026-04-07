import type { BuilderLaunchRequest } from '../types';
import { useBuilderWorkspace } from '../contexts/BuilderWorkspaceContext';
import { useBuilderFileSelection } from './useBuilderFileSelection';
import { useBuilderLaunchHandoff } from './useBuilderLaunchHandoff';
import { useBuilderActivityTimeline } from './useBuilderActivityTimeline';
import { useBuilderWorkspaceCommands } from './useBuilderWorkspaceCommands';
import { useBuilderMutationFlow, type BuilderVisibleMutation } from './useBuilderMutationFlow';
import { useBuilderNodeSheetFlow } from './useBuilderNodeSheetFlow';
import { useBuilderOpenFileTabs } from './useBuilderOpenFileTabs';
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
  const activityTimeline = useBuilderActivityTimeline();

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
    onSaveActivity: (filePath) => activityTimeline.recordActivity({
      kind: 'file',
      summary: 'File saved',
      detail: filePath,
      relatedPath: filePath,
      tone: 'success',
    }),
    onSelectFileActivity: (filePath) => activityTimeline.recordActivity({
      kind: 'file',
      summary: 'File opened',
      detail: filePath,
      relatedPath: filePath,
      tone: 'neutral',
    }),
  });

  const mutationFlow = useBuilderMutationFlow({
    selectedFile,
    setBuildLogs,
    setEditorContent,
    setIsDirty,
    onMutationActivity: (summary, detail, tone, relatedPath) => activityTimeline.recordActivity({
      kind: 'mutation',
      summary,
      detail,
      tone,
      relatedPath,
    }),
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
    onWorkspaceActivity: (summary, detail, tone, relatedPath) => activityTimeline.recordActivity({
      kind: 'workspace',
      summary,
      detail,
      tone,
      relatedPath,
    }),
  });

  const clearSelection = () => {
    setSelectedFilePath(null);
    setSelectedNode(null);
    setEditorContent('');
    setIsDirty(false);
  };

  const {
    openFileTabs,
    handleSelectTrackedFile,
    handleCloseFileTab,
  } = useBuilderOpenFileTabs({
    project,
    selectedFile,
    selectedFilePath,
    isDirty,
    onSelectFile: handleSelectFile,
    onClearSelection: clearSelection,
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
    onStructureActivity: (summary, detail, tone, relatedPath) => activityTimeline.recordActivity({
      kind: 'structure',
      summary,
      detail,
      tone,
      relatedPath,
    }),
  });

  const commands = useBuilderWorkspaceCommands({
    createPrompt,
    onLaunchConsumed,
    project,
    buildSummary: refreshFlow.buildSummary,
    resetWorkspace: refreshFlow.handleResetProject,
    setProject: (nextProject) => setProject(nextProject),
    setSelectedFilePath,
    setSelectedNode: () => setSelectedNode(null),
    setEditorContent,
    setIsDirty,
    setBuildLogs,
    setCreateError,
    setWorkspaceError,
    handleSelectFile: handleSelectTrackedFile,
    clearActivity: activityTimeline.clearActivity,
    recordActivity: activityTimeline.recordActivity,
  });

  useBuilderLaunchHandoff({
    launchRequest,
    onLaunchConsumed,
    project,
    setCreatePrompt,
    setBuildLogs,
    createProjectFromPrompt: commands.createProjectFromPrompt,
  });

  return {
    project,
    selectedNode,
    selectedFile,
    openFileTabs,
    editorContent,
    isDirty,
    buildLogs,
    buildSummary: refreshFlow.buildSummary,
    activityEntries: activityTimeline.activityEntries,
    createPrompt,
    createError,
    workspacePathInput,
    workspaceError,
    isCreating: commands.isCreating,
    isBuilding: refreshFlow.isBuilding,
    isRefreshing: refreshFlow.isRefreshing,
    nodeSheet: nodeSheetFlow.nodeSheet,
    visibleMutation: mutationFlow.visibleMutation,
    setCreatePrompt,
    setWorkspacePathInput,
    setEditorContent,
    setIsDirty,
    setNodeSheet: nodeSheetFlow.setNodeSheet,
    handleCreate: commands.handleCreate,
    handleOpenPrimaryBuildError: commands.handleOpenPrimaryBuildError,
    handleOpenExistingProject: refreshFlow.handleOpenExistingProject,
    handleRefreshWorkspace: refreshFlow.handleRefreshWorkspace,
    handleResetProject: commands.handleResetProject,
    handleBuild: refreshFlow.handleBuild,
    handleSave,
    handleProposeMutation: mutationFlow.handleProposeMutation,
    handleApproveMutation: mutationFlow.handleApproveMutation,
    handleRejectMutation: mutationFlow.handleRejectMutation,
    handleSelectFile: handleSelectTrackedFile,
    handleSelectFileTab: handleSelectTrackedFile,
    handleCloseFileTab,
    handleSelectFolder,
    openCreateNodeSheet: nodeSheetFlow.openCreateNodeSheet,
    openRenameNodeSheet: nodeSheetFlow.openRenameNodeSheet,
    openDeleteNodeSheet: nodeSheetFlow.openDeleteNodeSheet,
    handleNodeSheetValueChange: nodeSheetFlow.handleNodeSheetValueChange,
    handleSubmitNodeSheet: nodeSheetFlow.handleSubmitNodeSheet,
  };
}

export type { BuilderVisibleMutation };
