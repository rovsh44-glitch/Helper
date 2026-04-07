import type { VirtualFile, VirtualFolder } from '../types';

export function findFileByPath(folder: VirtualFolder, path: string): VirtualFile | null {
  const directMatch = folder.files.find(file => file.path === path);
  if (directMatch) {
    return directMatch;
  }

  for (const child of folder.folders) {
    const nestedMatch = findFileByPath(child, path);
    if (nestedMatch) {
      return nestedMatch;
    }
  }

  return null;
}

export function findFolderByPath(folder: VirtualFolder, path: string): VirtualFolder | null {
  if (folder.path === path) {
    return folder;
  }

  for (const child of folder.folders) {
    const nestedMatch = findFolderByPath(child, path);
    if (nestedMatch) {
      return nestedMatch;
    }
  }

  return null;
}

export function findFileByBuildPath(folder: VirtualFolder, buildPath: string): VirtualFile | null {
  const normalizedBuildPath = normalizePath(buildPath);
  const directMatch = folder.files.find(file => {
    const normalizedFilePath = normalizePath(file.path);
    return normalizedFilePath === normalizedBuildPath
      || normalizedBuildPath.endsWith(`/${normalizedFilePath}`)
      || normalizedBuildPath.endsWith(`\\${file.path.replace(/\//g, '\\')}`);
  });

  if (directMatch) {
    return directMatch;
  }

  for (const child of folder.folders) {
    const nestedMatch = findFileByBuildPath(child, buildPath);
    if (nestedMatch) {
      return nestedMatch;
    }
  }

  return null;
}

function normalizePath(value: string) {
  return value.replace(/\\/g, '/').replace(/\/+$/, '');
}
