import type { VirtualFile, VirtualFolder } from '../types';

type WorkspaceFolderLike = {
  name: string;
  path: string;
  files: Array<{ name: string; path: string; language: string }>;
  folders: WorkspaceFolderLike[];
};

const languageMap: Record<string, VirtualFile['language']> = {
  typescript: 'typescript',
  python: 'python',
  swift: 'swift',
  kotlin: 'kotlin',
  json: 'json',
  xml: 'xml',
  xaml: 'xaml',
  text: 'text',
  csharp: 'csharp',
  markdown: 'markdown',
};

export function toVirtualFolder(folder: WorkspaceFolderLike): VirtualFolder {
  return {
    name: folder.name,
    path: normalizeRelativePath(folder.path),
    files: folder.files.map(file => ({
      name: file.name,
      path: normalizeRelativePath(file.path),
      content: '',
      language: normalizeLanguage(file.language),
    })),
    folders: folder.folders.map(toVirtualFolder),
  };
}

export function buildVirtualFolder(projectName: string, flatFiles: string[]): VirtualFolder {
  const root: VirtualFolder = { name: projectName, path: '', files: [], folders: [] };

  for (const filePath of flatFiles) {
    const normalizedPath = normalizeRelativePath(filePath);
    const parts = normalizedPath.split('/').filter(Boolean);
    let currentFolder = root;

    for (let index = 0; index < parts.length - 1; index += 1) {
      const folderName = parts[index];
      const folderPath = parts.slice(0, index + 1).join('/');
      let nextFolder = currentFolder.folders.find(folder => folder.path === folderPath);
      if (!nextFolder) {
        nextFolder = { name: folderName, path: folderPath, files: [], folders: [] };
        currentFolder.folders.push(nextFolder);
      }
      currentFolder = nextFolder;
    }

    const fileName = parts[parts.length - 1];
    currentFolder.files.push({
      name: fileName,
      path: normalizedPath,
      content: '',
      language: inferLanguageFromFileName(fileName),
    });
  }

  return root;
}

export function normalizeRelativePath(path: string): string {
  return path.replace(/\\/g, '/').replace(/^\.\/+/, '').replace(/^\/+/, '').replace(/\/+$/, '');
}

export function getParentRelativePath(path: string): string {
  const normalized = normalizeRelativePath(path);
  if (!normalized.includes('/')) {
    return '';
  }

  return normalized.slice(0, normalized.lastIndexOf('/'));
}

function inferLanguageFromFileName(fileName: string): VirtualFile['language'] {
  const lower = fileName.toLowerCase();
  if (lower.endsWith('.xaml')) return 'xaml';
  if (lower.endsWith('.cs')) return 'csharp';
  if (lower.endsWith('.json')) return 'json';
  if (lower.endsWith('.xml')) return 'xml';
  if (lower.endsWith('.md')) return 'markdown';
  if (lower.endsWith('.ts') || lower.endsWith('.tsx')) return 'typescript';
  if (lower.endsWith('.py')) return 'python';
  if (lower.endsWith('.swift')) return 'swift';
  if (lower.endsWith('.kt')) return 'kotlin';
  return 'text';
}

function normalizeLanguage(language: string): VirtualFile['language'] {
  return languageMap[language.toLowerCase()] ?? 'text';
}
