import React from 'react';
import type { ConversationMemoryItemDto } from '../../services/conversationApi';

type SettingsMemoryItemsPanelProps = {
  isLoadingMemory: boolean;
  memoryItems: ConversationMemoryItemDto[];
  isDeletingMemory: string | null;
  onDeleteMemoryItem: (memoryId: string) => void;
};

export const SettingsMemoryItemsPanel: React.FC<SettingsMemoryItemsPanelProps> = ({
  isLoadingMemory,
  memoryItems,
  isDeletingMemory,
  onDeleteMemoryItem,
}) => (
  <div className="bg-slate-900 p-6 rounded-xl border border-slate-800">
    <h3 className="text-sm font-bold text-primary-400 uppercase mb-4">Stored Memory Items</h3>
    {isLoadingMemory ? (
      <p className="text-xs text-slate-500">Loading memory items...</p>
    ) : memoryItems.length === 0 ? (
      <p className="text-xs text-slate-500">No active memory items.</p>
    ) : (
      <div className="space-y-2">
        {memoryItems.slice(0, 40).map(item => (
          <div key={item.id} className="bg-black/30 border border-slate-800 rounded p-3 flex items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="text-[10px] uppercase text-slate-500">
                {item.type} {item.isPersonal ? 'personal' : 'shared'}
              </div>
              <div className="text-xs text-slate-200 break-words">{item.content}</div>
              <div className="text-[10px] text-slate-500 mt-1">
                created: {new Date(item.createdAt).toLocaleString()} {item.expiresAt ? ` | expires: ${new Date(item.expiresAt).toLocaleString()}` : ''}
              </div>
            </div>
            <button
              type="button"
              onClick={() => onDeleteMemoryItem(item.id)}
              disabled={isDeletingMemory === item.id}
              className="px-2 py-1 text-[10px] rounded border border-red-900 text-red-300 hover:bg-red-950/40 disabled:opacity-50"
            >
              {isDeletingMemory === item.id ? 'Deleting...' : 'Delete'}
            </button>
          </div>
        ))}
      </div>
    )}
  </div>
);
