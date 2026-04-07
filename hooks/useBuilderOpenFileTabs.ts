import { useEffect, useMemo, useRef, useState } from 'react';
import type { GeneratedProject, VirtualFile } from '../types';
import { findFileByPath } from '../services/builderTree';
import { readRouteQueryParam, updateRouteQueryParams } from '../services/appShellRoute';

type UseBuilderOpenFileTabsArgs = {
  project: GeneratedProject | null;
  selectedFile: VirtualFile | null;
  selectedFilePath: string | null;
  isDirty: boolean;
  onSelectFile: (file: VirtualFile) => void;
  onClearSelection: () => void;
};

export function useBuilderOpenFileTabs({
  project,
  selectedFile,
  selectedFilePath,
  isDirty,
  onSelectFile,
  onClearSelection,
}: UseBuilderOpenFileTabsArgs) {
  const [openFilePaths, setOpenFilePaths] = useState<string[]>([]);
  const routeFileHydratedForProject = useRef<string | null>(null);

  useEffect(() => {
    if (!selectedFile?.path) {
      return;
    }

    setOpenFilePaths(previous => previous.includes(selectedFile.path)
      ? previous
      : [...previous, selectedFile.path]);
  }, [selectedFile?.path]);

  useEffect(() => {
    if (!project) {
      setOpenFilePaths([]);
      routeFileHydratedForProject.current = null;
      return;
    }

    setOpenFilePaths(previous => previous.filter(path => findFileByPath(project.root, path) !== null));
    if (routeFileHydratedForProject.current === project.fullPath) {
      return;
    }

    const requestedFilePath = readRouteQueryParam('file');
    if (!requestedFilePath) {
      routeFileHydratedForProject.current = project.fullPath;
      return;
    }

    const requestedFile = findFileByPath(project.root, requestedFilePath);
    if (requestedFile) {
      onSelectFile(requestedFile);
      setOpenFilePaths(previous => previous.includes(requestedFile.path)
        ? previous
        : [...previous, requestedFile.path]);
    }

    routeFileHydratedForProject.current = project.fullPath;
  }, [onSelectFile, project]);

  useEffect(() => {
    updateRouteQueryParams({
      file: selectedFilePath,
    }, { replace: true });
  }, [selectedFilePath]);

  const openFileTabs = useMemo(
    () => (project
      ? openFilePaths
        .map(path => findFileByPath(project.root, path))
        .filter((file): file is NonNullable<typeof file> => file !== null)
      : []),
    [openFilePaths, project],
  );

  const handleSelectTrackedFile = (file: VirtualFile) => {
    onSelectFile(file);
    setOpenFilePaths(previous => previous.includes(file.path)
      ? previous
      : [...previous, file.path]);
  };

  const handleCloseFileTab = (path: string) => {
    if (selectedFilePath === path && isDirty) {
      const confirmed = window.confirm('Close the selected file tab and discard unsaved changes?');
      if (!confirmed) {
        return;
      }
    }

    setOpenFilePaths(previous => {
      const remaining = previous.filter(entry => entry !== path);

      if (selectedFilePath === path) {
        const fallbackPath = remaining[remaining.length - 1] ?? null;
        if (!fallbackPath || !project) {
          onClearSelection();
          return remaining;
        }

        const fallbackFile = findFileByPath(project.root, fallbackPath);
        if (!fallbackFile) {
          onClearSelection();
          return remaining;
        }

        onSelectFile(fallbackFile);
      }

      return remaining;
    });
  };

  return {
    openFileTabs,
    handleSelectTrackedFile,
    handleCloseFileTab,
  };
}
