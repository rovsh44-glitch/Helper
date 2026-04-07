import { useState, type Dispatch, type SetStateAction } from 'react';
import type { BuilderWorkspaceSelection, GeneratedProject, VirtualFile } from '../types';
import { projectService } from '../services/projectService';
import type { BuilderNodeSheetState } from '../components/views/BuilderNodeSheet';

type UseBuilderNodeSheetFlowArgs = {
  project: GeneratedProject | null;
  selectedNode: BuilderWorkspaceSelection | null;
  selectedFile: VirtualFile | null;
  setSelectedNode: Dispatch<SetStateAction<BuilderWorkspaceSelection | null>>;
  setSelectedFilePath: Dispatch<SetStateAction<string | null>>;
  setEditorContent: Dispatch<SetStateAction<string>>;
  setIsDirty: Dispatch<SetStateAction<boolean>>;
  setWorkspaceError: Dispatch<SetStateAction<string | null>>;
  setBuildLogs: Dispatch<SetStateAction<string[]>>;
  syncProjectFromService: (nextSelection?: BuilderWorkspaceSelection | null) => void;
  onStructureActivity?: (summary: string, detail?: string, tone?: 'neutral' | 'success' | 'warning' | 'danger', relatedPath?: string) => void;
};

export function useBuilderNodeSheetFlow({
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
  onStructureActivity,
}: UseBuilderNodeSheetFlowArgs) {
  const [nodeSheet, setNodeSheet] = useState<BuilderNodeSheetState | null>(null);

  const openCreateNodeSheet = (isFolder: boolean) => {
    if (!project) {
      return;
    }

    const parentPath = selectedNode?.kind === 'folder'
      ? selectedNode.path
      : selectedNode?.kind === 'file'
        ? projectService.getParentPath(selectedNode.path)
        : '';

    setNodeSheet({
      mode: isFolder ? 'create_folder' : 'create_file',
      value: '',
      parentPath,
    });
  };

  const submitCreateNode = async (name: string, isFolder: boolean, parentPath: string) => {
    if (!project) {
      return;
    }

    try {
      setWorkspaceError(null);
      const trimmedName = name.trim();
      if (!trimmedName) {
        return;
      }

      const createdPath = parentPath ? `${parentPath}/${trimmedName}` : trimmedName;
      await projectService.createWorkspaceNode(parentPath, trimmedName, isFolder);
      syncProjectFromService({ kind: isFolder ? 'folder' : 'file', path: createdPath, label: trimmedName });
      setBuildLogs(prev => [...prev, `${isFolder ? '📁' : '📄'} Created ${trimmedName} in ${parentPath || project.name}`]);
      onStructureActivity?.(isFolder ? 'Folder created' : 'File created', createdPath, 'success', createdPath);
      setNodeSheet(null);
    } catch (error) {
      setWorkspaceError(error instanceof Error ? error.message : 'Workspace create failed.');
    }
  };

  const openRenameNodeSheet = () => {
    if (!selectedNode || selectedNode.path === '') {
      setWorkspaceError('Select a workspace file or folder before renaming.');
      return;
    }

    setNodeSheet({
      mode: 'rename',
      value: selectedNode.label,
      targetPath: selectedNode.path,
      targetLabel: selectedNode.label,
      targetKind: selectedNode.kind,
    });
  };

  const submitRenameNode = async (
    newName: string,
    targetPath: string,
    targetLabel: string,
    targetKind: BuilderWorkspaceSelection['kind'],
  ) => {
    if (!targetPath) {
      setWorkspaceError('Select a workspace file or folder before renaming.');
      return;
    }

    if (!newName.trim() || newName.trim() === targetLabel) {
      return;
    }

    try {
      setWorkspaceError(null);
      const parentPath = projectService.getParentPath(targetPath);
      const trimmedName = newName.trim();
      const nextPath = parentPath ? `${parentPath}/${trimmedName}` : trimmedName;
      await projectService.renameWorkspaceNode(targetPath, trimmedName);
      if (selectedFile?.path === targetPath) {
        setSelectedFilePath(null);
        setEditorContent('');
        setIsDirty(false);
      }
      syncProjectFromService({ kind: targetKind, path: nextPath, label: trimmedName });
      setBuildLogs(prev => [...prev, `✏️ Renamed ${targetLabel} to ${trimmedName}`]);
      onStructureActivity?.('Node renamed', `${targetPath} -> ${nextPath}`, 'neutral', nextPath);
      setNodeSheet(null);
    } catch (error) {
      setWorkspaceError(error instanceof Error ? error.message : 'Workspace rename failed.');
    }
  };

  const openDeleteNodeSheet = () => {
    if (!selectedNode || selectedNode.path === '') {
      setWorkspaceError('Select a workspace file or folder before deleting.');
      return;
    }

    setNodeSheet({
      mode: 'delete',
      targetPath: selectedNode.path,
      targetLabel: selectedNode.label,
      targetKind: selectedNode.kind,
    });
  };

  const submitDeleteNode = async (
    targetPath: string,
    targetLabel: string,
    targetKind: BuilderWorkspaceSelection['kind'],
  ) => {
    if (!targetPath) {
      setWorkspaceError('Select a workspace file or folder before deleting.');
      return;
    }

    try {
      setWorkspaceError(null);
      await projectService.deleteWorkspaceNode(targetPath);
      if (selectedFile?.path === targetPath || (selectedFile && selectedFile.path.startsWith(`${targetPath}/`))) {
        setSelectedFilePath(null);
        setEditorContent('');
        setIsDirty(false);
      }
      setSelectedNode(null);
      syncProjectFromService();
      setBuildLogs(prev => [...prev, `🗑 Deleted ${targetKind}: ${targetLabel}`]);
      onStructureActivity?.('Node deleted', `${targetKind}: ${targetLabel}`, 'warning', targetPath);
      setNodeSheet(null);
    } catch (error) {
      setWorkspaceError(error instanceof Error ? error.message : 'Workspace delete failed.');
    }
  };

  const handleSubmitNodeSheet = () => {
    if (!nodeSheet) {
      return;
    }

    if (nodeSheet.mode === 'rename') {
      void submitRenameNode(nodeSheet.value, nodeSheet.targetPath, nodeSheet.targetLabel, nodeSheet.targetKind);
      return;
    }

    if (nodeSheet.mode === 'delete') {
      void submitDeleteNode(nodeSheet.targetPath, nodeSheet.targetLabel, nodeSheet.targetKind);
      return;
    }

    void submitCreateNode(nodeSheet.value, nodeSheet.mode === 'create_folder', nodeSheet.parentPath);
  };

  const handleNodeSheetValueChange = (value: string) => {
    setNodeSheet(previous => previous && previous.mode !== 'delete' ? { ...previous, value } : previous);
  };

  return {
    nodeSheet,
    setNodeSheet,
    openCreateNodeSheet,
    openRenameNodeSheet,
    openDeleteNodeSheet,
    handleNodeSheetValueChange,
    handleSubmitNodeSheet,
  };
}
