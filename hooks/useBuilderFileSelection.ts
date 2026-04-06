import { useEffect, useMemo, type Dispatch, type SetStateAction } from 'react';
import type { BuilderWorkspaceSelection, GeneratedProject, VirtualFile } from '../types';
import { findFileByPath, findFolderByPath } from '../services/builderTree';
import { projectService } from '../services/projectService';

type UseBuilderFileSelectionArgs = {
  project: GeneratedProject | null;
  selectedNode: BuilderWorkspaceSelection | null;
  selectedFilePath: string | null;
  editorContent: string;
  isDirty: boolean;
  setProject: Dispatch<SetStateAction<GeneratedProject | null>>;
  setSelectedNode: Dispatch<SetStateAction<BuilderWorkspaceSelection | null>>;
  setSelectedFilePath: Dispatch<SetStateAction<string | null>>;
  setEditorContent: Dispatch<SetStateAction<string>>;
  setIsDirty: Dispatch<SetStateAction<boolean>>;
  setWorkspaceError: Dispatch<SetStateAction<string | null>>;
  onSaveActivity?: (filePath: string) => void;
  onSelectFileActivity?: (filePath: string) => void;
};

export function useBuilderFileSelection({
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
  onSaveActivity,
  onSelectFileActivity,
}: UseBuilderFileSelectionArgs) {
  const selectedFile = useMemo(
    () => (project && selectedFilePath ? findFileByPath(project.root, selectedFilePath) : null),
    [project, selectedFilePath],
  );

  useEffect(() => {
    if (selectedFile && !isDirty) {
      projectService.getFileContent(selectedFile.path).then(content => {
        setEditorContent(content);
        setIsDirty(false);
      });
    }
  }, [isDirty, selectedFile, setEditorContent, setIsDirty]);

  const syncProjectFromService = (nextSelection: BuilderWorkspaceSelection | null = selectedNode) => {
    const nextProject = projectService.getCurrentProject();
    if (!nextProject) {
      setProject(null);
      setSelectedFilePath(null);
      setSelectedNode(null);
      setEditorContent('');
      setIsDirty(false);
      return;
    }

    setProject({ ...nextProject });

    if (!nextSelection) {
      setSelectedFilePath(null);
      setSelectedNode(null);
      setEditorContent('');
      setIsDirty(false);
      return;
    }

    if (nextSelection.kind === 'file') {
      const refreshedFile = findFileByPath(nextProject.root, nextSelection.path);
      if (refreshedFile) {
        setSelectedFilePath(refreshedFile.path);
        setSelectedNode({ kind: 'file', path: refreshedFile.path, label: refreshedFile.name });
        return;
      }
    } else {
      const refreshedFolder = findFolderByPath(nextProject.root, nextSelection.path);
      if (refreshedFolder) {
        setSelectedFilePath(null);
        setSelectedNode({ kind: 'folder', path: refreshedFolder.path, label: refreshedFolder.name });
        setEditorContent('');
        setIsDirty(false);
        return;
      }
    }

    setSelectedFilePath(null);
    setSelectedNode(null);
    setEditorContent('');
    setIsDirty(false);
  };

  const handleSelectFile = (file: VirtualFile) => {
    setSelectedFilePath(file.path);
    setSelectedNode({ kind: 'file', path: file.path, label: file.name });
    setWorkspaceError(null);
    onSelectFileActivity?.(file.path);
  };

  const handleSelectFolder = (folder: { path: string; name: string }) => {
    setSelectedFilePath(null);
    setSelectedNode({ kind: 'folder', path: folder.path, label: folder.name });
    setEditorContent('');
    setIsDirty(false);
    setWorkspaceError(null);
  };

  const handleSave = async () => {
    if (!selectedFile || !project) {
      return;
    }

    await projectService.updateFileContent(selectedFile.path, editorContent);
    setIsDirty(false);
    onSaveActivity?.(selectedFile.path);
  };

  return {
    selectedFile,
    syncProjectFromService,
    handleSelectFile,
    handleSelectFolder,
    handleSave,
  };
}
