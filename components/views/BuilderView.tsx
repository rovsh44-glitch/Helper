import React from 'react';
import type { BuilderLaunchRequest } from '../../types';
import { useBuilderWorkspaceSession } from '../../hooks/useBuilderWorkspaceSession';
import { BuilderWorkspaceSidebar } from './BuilderWorkspaceSidebar';
import { BuilderProjectLauncher } from './BuilderProjectLauncher';
import { BuilderWorkspaceContent } from './BuilderWorkspaceContent';

interface BuilderViewProps {
  launchRequest?: BuilderLaunchRequest | null;
  onLaunchConsumed?: () => void;
}

export const BuilderView: React.FC<BuilderViewProps> = ({ launchRequest = null, onLaunchConsumed }) => {
  const session = useBuilderWorkspaceSession({ launchRequest, onLaunchConsumed });

  if (!session.project) {
    return (
      <BuilderProjectLauncher
        createPrompt={session.createPrompt}
        createError={session.createError}
        workspacePathInput={session.workspacePathInput}
        workspaceError={session.workspaceError}
        isCreating={session.isCreating}
        isRefreshing={session.isRefreshing}
        onCreatePromptChange={session.setCreatePrompt}
        onCreate={session.handleCreate}
        onWorkspacePathChange={session.setWorkspacePathInput}
        onOpenExistingProject={session.handleOpenExistingProject}
      />
    );
  }

  return (
    <div className="flex h-full bg-slate-950">
      <BuilderWorkspaceSidebar
        project={session.project}
        selectedNode={session.selectedNode}
        workspacePathInput={session.workspacePathInput}
        workspaceError={session.workspaceError}
        isRefreshing={session.isRefreshing}
        isBuilding={session.isBuilding}
        nodeSheet={session.nodeSheet}
        onWorkspacePathChange={session.setWorkspacePathInput}
        onOpenExistingProject={session.handleOpenExistingProject}
        onRefreshWorkspace={session.handleRefreshWorkspace}
        onResetProject={session.handleResetProject}
        onBuild={session.handleBuild}
        onOpenCreateNodeSheet={session.openCreateNodeSheet}
        onOpenRenameNodeSheet={session.openRenameNodeSheet}
        onOpenDeleteNodeSheet={session.openDeleteNodeSheet}
        onNodeSheetValueChange={session.handleNodeSheetValueChange}
        onSubmitNodeSheet={session.handleSubmitNodeSheet}
        onCloseNodeSheet={() => session.setNodeSheet(null)}
        onSelectFile={session.handleSelectFile}
        onSelectFolder={session.handleSelectFolder}
        onProposeMutation={session.handleProposeMutation}
      />

      <BuilderWorkspaceContent
        selectedFile={session.selectedFile}
        selectedNode={session.selectedNode}
        editorContent={session.editorContent}
        isDirty={session.isDirty}
        buildLogs={session.buildLogs}
        visibleMutation={session.visibleMutation}
        onEditorContentChange={(value) => {
          session.setEditorContent(value);
          session.setIsDirty(true);
        }}
        onSave={session.handleSave}
        onApproveMutation={session.handleApproveMutation}
        onRejectMutation={session.handleRejectMutation}
      />
    </div>
  );
};
