import React, { memo, useCallback, useMemo, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import type { Message } from '../../types';
import {
  createRepairConversationDraft,
  getRepairQuickActionPresets,
  resolveRepairUiLanguage,
  type RepairConversationDraft,
  type RepairQuickActionId,
} from '../../services/conversationApi';
import { readPreferredLanguage } from '../../services/conversationSession';
import { shouldRenderSupplementalNextStep } from '../../utils/conversationUi';
import { ThoughtTree } from '../ThoughtTree';
import { ResearchCard } from '../ResearchCard';
import { AssistantDiagnosticsDeck } from './AssistantDiagnosticsDeck';
import { RepairIntentSheet } from './RepairIntentSheet';
import { SearchTracePanel } from './SearchTracePanel';

interface OrchestratorMessageCardProps {
  message: Message;
  isStreaming: boolean;
  isBusy: boolean;
  onRegenerate: (turnId: string) => void;
  onCreateBranch: (turnId: string) => void;
  onRepairTurn: (turnId: string, draft: RepairConversationDraft) => Promise<boolean>;
  onRateMessage: (turnId: string | undefined, rating: number) => void;
}

export const OrchestratorMessageCard = memo(function OrchestratorMessageCard({
  message,
  isStreaming,
  isBusy,
  onRegenerate,
  onCreateBranch,
  onRepairTurn,
  onRateMessage,
}: OrchestratorMessageCardProps) {
  const showSupplementalNextStep = shouldRenderSupplementalNextStep(message);
  const repairLanguage = useMemo(
    () => resolveRepairUiLanguage(readPreferredLanguage(), message.content),
    [message.content],
  );
  const repairQuickActions = useMemo(
    () => getRepairQuickActionPresets(repairLanguage),
    [repairLanguage],
  );
  const [isRepairSheetOpen, setIsRepairSheetOpen] = useState(false);
  const [repairDraft, setRepairDraft] = useState<RepairConversationDraft>(() => createRepairConversationDraft(repairLanguage));

  const openRepairSheet = useCallback((actionId?: RepairQuickActionId) => {
    setRepairDraft(createRepairConversationDraft(repairLanguage, actionId));
    setIsRepairSheetOpen(true);
  }, [repairLanguage]);

  const closeRepairSheet = useCallback(() => {
    setIsRepairSheetOpen(false);
    setRepairDraft(createRepairConversationDraft(repairLanguage));
  }, [repairLanguage]);

  const submitRepairSheet = useCallback(async () => {
    if (!message.turnId) {
      return;
    }

    const succeeded = await onRepairTurn(message.turnId, repairDraft);
    if (succeeded) {
      closeRepairSheet();
    }
  }, [closeRepairSheet, message.turnId, onRepairTurn, repairDraft]);

  const messagePreview = useMemo(() => {
    const collapsed = message.content.replace(/\s+/g, ' ').trim();
    return collapsed.length > 220 ? `${collapsed.slice(0, 217)}...` : collapsed;
  }, [message.content]);

  return (
    <div className={`flex ${message.role === 'user' ? 'justify-end' : 'justify-start'}`}>
      <div className={`max-w-4xl rounded-lg p-4 shadow-xl ${
        message.role === 'user'
          ? 'bg-primary-600 text-white'
          : message.role === 'system'
            ? 'bg-red-900/20 text-red-300 border border-red-900/50 font-mono text-xs'
            : 'bg-slate-800 border border-slate-700 text-slate-200'
      }`}>
        {message.role !== 'system' && message.inputMode === 'voice' && (
          <div className="mb-3 flex items-center gap-2">
            <span className="rounded-full border border-sky-700 bg-sky-950/30 px-2 py-1 text-[10px] uppercase tracking-wide text-sky-200">
              Voice Turn
            </span>
          </div>
        )}

        {message.thoughtTree && (
          <div className="mb-4">
            <ThoughtTree node={message.thoughtTree} />
          </div>
        )}

        {message.researchResult && (
          <ResearchCard result={message.researchResult} />
        )}

        {message.attachments && message.attachments.length > 0 && (
          <div className="mb-3 text-[11px] text-slate-400">
            Attachments: {message.attachments.map(item => item.name).join(', ')}
          </div>
        )}

        {isStreaming ? (
          <div className="whitespace-pre-wrap text-sm leading-relaxed text-slate-200">
            {message.content}
          </div>
        ) : (
          <div className="prose prose-invert prose-xs max-w-none prose-p:leading-relaxed prose-pre:bg-black/50">
            <ReactMarkdown remarkPlugins={[remarkGfm]}>
              {message.content}
            </ReactMarkdown>
          </div>
        )}

        {message.role === 'assistant' && message.requiresConfirmation && (
          <div className="mt-3 text-xs text-amber-300 bg-amber-900/20 border border-amber-700 rounded px-3 py-2">
            Confirmation required before risky action.
          </div>
        )}

        {message.role === 'assistant' && showSupplementalNextStep && (
          <div className="mt-3 text-xs text-slate-400 border-t border-slate-700 pt-2">
            Next step: {message.nextStep}
          </div>
        )}

        {message.role === 'assistant' && (
          <SearchTracePanel trace={message.searchTrace} fallbackSources={message.searchTrace ? message.sources : undefined} />
        )}

        {message.role === 'assistant' && message.diagnosticsDeck && (
          <AssistantDiagnosticsDeck deck={message.diagnosticsDeck} />
        )}

        {message.role === 'assistant' && (
          <div className="mt-3 flex flex-wrap gap-2">
            {message.turnId && (
              <>
                <button
                  onClick={() => onRegenerate(message.turnId!)}
                  disabled={isBusy}
                  className="text-[11px] px-3 py-1 rounded border border-slate-600 hover:border-primary-500 text-slate-200"
                >
                  Regenerate
                </button>
                <button
                  onClick={() => onCreateBranch(message.turnId!)}
                  disabled={isBusy}
                  className="text-[11px] px-3 py-1 rounded border border-slate-600 hover:border-primary-500 text-slate-200"
                >
                  Branch From Turn
                </button>
                <button
                  onClick={() => openRepairSheet()}
                  disabled={isBusy}
                  className="text-[11px] px-3 py-1 rounded border border-amber-700 hover:border-amber-500 text-amber-300"
                >
                  Repair Understanding
                </button>
                {repairQuickActions.map(action => (
                  <button
                    key={`${message.id}-${action.id}`}
                    onClick={() => openRepairSheet(action.id)}
                    className="text-[11px] px-3 py-1 rounded-full border border-slate-700 hover:border-amber-700 text-slate-300 hover:text-amber-200"
                    disabled={isBusy}
                    title={action.description}
                  >
                    {action.label}
                  </button>
                ))}
              </>
            )}
            <div className="flex items-center gap-1">
              {[1, 2, 3, 4, 5].map(score => (
                <button
                  key={`${message.id}-rate-${score}`}
                  onClick={() => onRateMessage(message.turnId, score)}
                  className={`text-[11px] px-2 py-1 rounded border ${
                    message.rating === score
                      ? 'border-green-500 text-green-300'
                      : 'border-slate-600 text-slate-400 hover:border-slate-400'
                  }`}
                >
                  {score}
                </button>
              ))}
            </div>
          </div>
        )}

        {message.role === 'assistant' && message.turnId && isRepairSheetOpen && (
          <RepairIntentSheet
            language={repairLanguage}
            messagePreview={messagePreview}
            draft={repairDraft}
            quickActions={repairQuickActions}
            disabled={isBusy}
            onSelectQuickAction={(actionId) => setRepairDraft(createRepairConversationDraft(repairLanguage, actionId))}
            onChangeCorrectedIntent={(value) => setRepairDraft(previous => ({ ...previous, correctedIntent: value, selectedActionId: previous.selectedActionId }))}
            onChangeRepairNote={(value) => setRepairDraft(previous => ({ ...previous, repairNote: value }))}
            onSubmit={() => void submitRepairSheet()}
            onClose={closeRepairSheet}
          />
        )}
      </div>
    </div>
  );
});
