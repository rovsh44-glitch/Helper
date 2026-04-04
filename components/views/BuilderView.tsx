import React from 'react';
import { PanelResizeHandle } from '../layout/PanelResizeHandle';
import { usePersistentPanelSize } from '../../hooks/usePersistentPanelSize';
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
  const { size: workspaceSidebarWidth, resizeBy: resizeWorkspaceSidebar } = usePersistentPanelSize({
    storageKey: 'builder.workspace-sidebar-width',
    defaultSize: 320,
    minSize: 272,
    maxSize: 420,
  });
  const { size: activityDockWidth, resizeBy: resizeActivityDock } = usePersistentPanelSize({
    storageKey: 'builder.activity-dock-width',
    defaultSize: 360,
    minSize: 280,
    maxSize: 520,
  });

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
    <div className="flex h-full min-h-0 bg-slate-950">
      <div className="h-full shrink-0" style={{ width: `${workspaceSidebarWidth}px` }}>
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
      </div>

      <PanelResizeHandle
        axis="x"
        title="Resize Builder workspace sidebar"
        onResizeDelta={resizeWorkspaceSidebar}
      />

      <BuilderWorkspaceContent
        selectedFile={session.selectedFile}
        selectedNode={session.selectedNode}
        editorContent={session.editorContent}
        isDirty={session.isDirty}
        buildLogs={session.buildLogs}
        visibleMutation={session.visibleMutation}
        activityDockWidth={activityDockWidth}
        onResizeActivityDock={(delta) => resizeActivityDock(-delta)}
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
