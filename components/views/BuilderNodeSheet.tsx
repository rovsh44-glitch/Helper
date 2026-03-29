import React from 'react';
import type { BuilderWorkspaceSelection } from '../../types';
import { InlineActionSheet } from './InlineActionSheet';

export type BuilderNodeSheetState =
  | {
      mode: 'create_file' | 'create_folder';
      value: string;
      parentPath: string;
    }
  | {
      mode: 'rename';
      value: string;
      targetPath: string;
      targetLabel: string;
      targetKind: BuilderWorkspaceSelection['kind'];
    }
  | {
      mode: 'delete';
      targetPath: string;
      targetLabel: string;
      targetKind: BuilderWorkspaceSelection['kind'];
    };

interface BuilderNodeSheetProps {
  projectName: string;
  sheet: BuilderNodeSheetState;
  onChangeValue: (value: string) => void;
  onSubmit: () => void;
  onClose: () => void;
}

export function BuilderNodeSheet({
  projectName,
  sheet,
  onChangeValue,
  onSubmit,
  onClose,
}: BuilderNodeSheetProps) {
  return (
    <InlineActionSheet
      title={sheet.mode === 'rename' ? 'Rename Workspace Node' : sheet.mode === 'create_folder' ? 'Create Folder' : sheet.mode === 'create_file' ? 'Create File' : 'Delete Workspace Node'}
      description={
        sheet.mode === 'rename'
          ? `Rename the selected ${sheet.targetKind} without leaving Builder.`
          : sheet.mode === 'delete'
            ? `Confirm deletion of the selected ${sheet.targetKind} from the current workspace.`
            : `Create a new ${sheet.mode === 'create_folder' ? 'folder' : 'file'} in the selected workspace scope.`
      }
      submitLabel={sheet.mode === 'rename' ? 'Apply Rename' : sheet.mode === 'delete' ? 'Delete Node' : 'Create Node'}
      submitTone={sheet.mode === 'delete' ? 'danger' : 'primary'}
      submitDisabled={
        sheet.mode === 'delete'
          ? false
          : !sheet.value.trim() || (sheet.mode === 'rename' && sheet.value.trim() === sheet.targetLabel)
      }
      onSubmit={onSubmit}
      onClose={onClose}
    >
      {sheet.mode === 'delete' ? (
        <div className="space-y-3">
          <div className="rounded-xl border border-rose-500/20 bg-rose-500/10 px-3 py-3 text-[11px] leading-relaxed text-rose-100">
            This will permanently delete the selected {sheet.targetKind} from the current workspace.
          </div>
          <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2 text-[11px] text-slate-300">
            <div className="uppercase tracking-wide text-slate-500">Target</div>
            <div className="mt-1 break-all">{sheet.targetPath}</div>
          </div>
        </div>
      ) : (
        <div className="space-y-3">
          <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2 text-[11px] text-slate-400">
            {sheet.mode === 'rename'
              ? `Current ${sheet.targetKind}: ${sheet.targetPath}`
              : `Target location: ${sheet.parentPath || projectName}`}
          </div>
          <label className="grid gap-1.5">
            <span className="text-[11px] uppercase tracking-wide text-slate-500">
              {sheet.mode === 'rename' ? 'New Name' : 'Node Name'}
            </span>
            <input
              value={sheet.value}
              onChange={(event) => onChangeValue(event.target.value)}
              placeholder={sheet.mode === 'create_folder' ? 'docs' : sheet.mode === 'create_file' ? 'notes.md' : sheet.targetLabel}
              className="w-full rounded-xl border border-slate-800 bg-slate-950 px-3 py-2 text-sm text-slate-200 outline-none transition-colors focus:border-primary-500"
            />
          </label>
        </div>
      )}
    </InlineActionSheet>
  );
}
