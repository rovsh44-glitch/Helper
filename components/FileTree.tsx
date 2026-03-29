
import React, { useState } from 'react';
import { VirtualFolder, VirtualFile } from '../types';

interface FileTreeProps {
  structure: VirtualFolder;
  onSelectFile: (file: VirtualFile) => void;
  onSelectFolder?: (folder: VirtualFolder) => void;
  selectedPath?: string;
  selectedKind?: 'file' | 'folder';
}

const FileIcon = ({ name }: { name: string }) => {
  if (name.endsWith('.swift')) return <span className="text-orange-500">Swift</span>;
  if (name.endsWith('.kt')) return <span className="text-purple-500">Kt</span>;
  if (name.endsWith('.cpp')) return <span className="text-blue-500">C++</span>;
  if (name.endsWith('.cs')) return <span className="text-green-500">C#</span>;
  if (name.endsWith('.xaml')) return <span className="text-indigo-400">XAML</span>;
  if (name.endsWith('.json') || name.endsWith('.xml')) return <span className="text-yellow-500">{}</span>;
  return <span className="text-slate-400">📄</span>;
};

const FolderNode: React.FC<{
  folder: VirtualFolder;
  onSelectFile: (f: VirtualFile) => void;
  onSelectFolder?: (folder: VirtualFolder) => void;
  selectedPath?: string;
  selectedKind?: 'file' | 'folder';
  depth: number;
}> = ({ folder, onSelectFile, onSelectFolder, selectedPath, selectedKind, depth }) => {
  const [isOpen, setIsOpen] = useState(true);
  const isSelected = selectedKind === 'folder' && selectedPath === folder.path;

  return (
    <div style={{ paddingLeft: `${depth * 12}px` }}>
      <div 
        className={`flex items-center gap-2 py-1 cursor-pointer rounded px-2 text-slate-300 select-none ${isSelected ? 'bg-primary-950/40 text-primary-100' : 'hover:bg-slate-800'}`}
        onClick={() => {
          setIsOpen(!isOpen);
          onSelectFolder?.(folder);
        }}
      >
        <span className="text-xs text-slate-500">{isOpen ? '📂' : '📁'}</span>
        <span className="text-sm font-medium">{folder.name}</span>
      </div>
      
      {isOpen && (
        <div className="border-l border-slate-800 ml-2">
          {folder.folders.map((sub, i) => (
            <FolderNode
              key={sub.path || `${sub.name}-${i}`}
              folder={sub}
              onSelectFile={onSelectFile}
              onSelectFolder={onSelectFolder}
              selectedPath={selectedPath}
              selectedKind={selectedKind}
              depth={depth + 1}
            />
          ))}
          {folder.files.map((file, i) => (
            <div 
              key={file.path || `${file.name}-${i}`} 
              className={`flex items-center gap-2 py-1 pl-4 cursor-pointer text-sm ${selectedKind === 'file' && selectedPath === file.path ? 'bg-primary-950/40 text-primary-100' : 'text-slate-400 hover:bg-slate-800 hover:text-primary-400'}`}
              onClick={() => onSelectFile(file)}
            >
              <FileIcon name={file.name} />
              <span>{file.name.split(/[/\\]/).pop()}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export const FileTree: React.FC<FileTreeProps> = ({ structure, onSelectFile, onSelectFolder, selectedPath, selectedKind }) => {
  return (
    <div className="font-mono text-sm">
      <FolderNode
        folder={structure}
        onSelectFile={onSelectFile}
        onSelectFolder={onSelectFolder}
        selectedPath={selectedPath}
        selectedKind={selectedKind}
        depth={0}
      />
    </div>
  );
};
