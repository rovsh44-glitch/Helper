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
