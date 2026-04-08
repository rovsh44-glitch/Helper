import React from 'react';
import type { ConversationMemoryItemDto } from '../../services/conversationApi';
import { InlineActionSheet } from '../views/InlineActionSheet';

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
}) => {
  const [pendingDeleteId, setPendingDeleteId] = React.useState<string | null>(null);
  const pendingDeleteItem = memoryItems.find(item => item.id === pendingDeleteId) ?? null;

  React.useEffect(() => {
    if (!pendingDeleteId || isDeletingMemory === pendingDeleteId) {
      return;
    }

    const stillExists = memoryItems.some(item => item.id === pendingDeleteId);
    if (!stillExists) {
      setPendingDeleteId(null);
    }
  }, [isDeletingMemory, memoryItems, pendingDeleteId]);

  return (
    <div className="bg-slate-900 p-6 rounded-xl border border-slate-800">
      <div className="mb-4 flex items-center justify-between gap-3">
        <h3 className="text-sm font-bold text-primary-400 uppercase">Stored Memory Items</h3>
        <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">Delete requires confirmation</div>
      </div>
      {isLoadingMemory ? (
        <p className="text-xs text-slate-500">Loading memory items...</p>
      ) : memoryItems.length === 0 ? (
        <p className="text-xs text-slate-500">No active memory items.</p>
      ) : (
        <div className="space-y-2">
          {memoryItems.slice(0, 40).map(item => (
            <div key={item.id} className="rounded border border-slate-800 bg-black/30 p-3">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="text-[10px] uppercase text-slate-500">
                    {item.type} {item.isPersonal ? 'personal' : 'shared'}
                  </div>
                  <div className="text-xs text-slate-200 break-words">{item.content}</div>
                  <div className="mt-2 flex flex-wrap gap-2 text-[10px] uppercase tracking-[0.16em] text-slate-500">
                    <span className="rounded-full border border-slate-800 px-2 py-0.5">
                      visibility: {item.isPersonal ? 'personal' : 'shared'}
                    </span>
                    <span className="rounded-full border border-slate-800 px-2 py-0.5">
                      source: {item.sourceTurnId ? `turn ${item.sourceTurnId.slice(0, 8)}` : 'policy/manual'}
                    </span>
                  </div>
                  <div className="mt-1 text-[10px] text-slate-500">
                    created: {new Date(item.createdAt).toLocaleString()} {item.expiresAt ? ` | expires: ${new Date(item.expiresAt).toLocaleString()}` : ''}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => setPendingDeleteId(item.id)}
                  disabled={isDeletingMemory === item.id}
                  className="px-2 py-1 text-[10px] rounded border border-red-900 text-red-300 hover:bg-red-950/40 disabled:opacity-50"
                >
                  {isDeletingMemory === item.id ? 'Deleting...' : 'Delete'}
                </button>
              </div>
              {pendingDeleteItem?.id === item.id && (
                <div className="mt-3">
                  <InlineActionSheet
                    title="Delete memory entry"
                    description="This removal updates the active conversation memory ledger immediately."
                    submitLabel={isDeletingMemory === item.id ? 'Deleting...' : 'Delete entry'}
                    cancelLabel="Keep entry"
                    submitTone="danger"
                    disabled={isDeletingMemory === item.id}
                    onSubmit={() => onDeleteMemoryItem(item.id)}
                    onClose={() => setPendingDeleteId(null)}
                  >
                    <div className="rounded-xl border border-slate-800 bg-slate-950/70 px-3 py-2 text-xs leading-6 text-slate-300">
                      <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">Pending deletion</div>
                      <div className="mt-1 break-words">{item.content}</div>
                      <div className="mt-2 text-[11px] text-slate-500">
                        Visibility: {item.isPersonal ? 'personal' : 'shared'}
                        {item.sourceTurnId ? ` · Source turn: ${item.sourceTurnId}` : ' · Source: policy/manual'}
                        {item.expiresAt ? ` · Expires: ${new Date(item.expiresAt).toLocaleString()}` : ' · No explicit expiry'}
                      </div>
                    </div>
                  </InlineActionSheet>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
