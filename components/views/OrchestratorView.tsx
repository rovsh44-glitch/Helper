import React, { useEffect, useMemo, useRef, useState } from 'react';
import { ChatAttachment, LiveWebMode, Message, MutationProposal, ProgressLogEntry, StrategicPlan } from '../../types';
import type { RepairConversationDraft } from '../../services/conversationApi';
import { projectService } from '../../services/projectService';
import { DiffViewer } from '../DiffViewer';
import { VoiceInterface } from '../VoiceInterface';
import { PanelResizeHandle } from '../layout/PanelResizeHandle';
import { usePersistentPanelSize } from '../../hooks/usePersistentPanelSize';
import { ActiveStreamingMessage } from './ActiveStreamingMessage';
import { InlineActionSheet } from './InlineActionSheet';
import { MessageList } from './MessageList';

interface OrchestratorViewProps {
  messages: Message[];
  input: string;
  setInput: (val: string) => void;
  handleSendMessage: () => void;
  handleVoiceInput: (transcript: string) => void;
  isProcessing: boolean;
  progressEntries?: ProgressLogEntry[];
  currentPlan?: StrategicPlan | null;
  activeMutation?: MutationProposal | null;
  onDismissMutation: () => void;
  activeBranchId: string;
  availableBranches: string[];
  pendingAttachments: ChatAttachment[];
  onAttachFiles: (files: FileList | null) => void;
  onClearAttachments: () => void;
  onSwitchBranch: (branchId: string) => void;
  onMergeIntoActive: (sourceBranchId: string) => void;
  onRegenerate: (turnId: string) => void;
  onCreateBranch: (turnId: string) => void;
  onRepairTurn: (turnId: string, draft: RepairConversationDraft) => Promise<boolean>;
  onRateMessage: (turnId: string | undefined, rating: number) => void;
  onArchiveConversationSnapshot: () => Promise<string>;
  onResetConversation: () => Promise<string | null>;
  canArchiveConversation?: boolean;
  canResetConversation?: boolean;
  conversationSessionEpoch: number;
  streamingMessageId?: string;
  startupState?: 'booting' | 'ready' | 'degraded';
  startupAlert?: string | null;
  liveWebMode: LiveWebMode;
  onChangeLiveWebMode: (value: LiveWebMode) => void;
  preferredLanguage: string;
  canResumeLastTurn?: boolean;
  onResumeLastTurn: () => void;
}

export const OrchestratorView: React.FC<OrchestratorViewProps> = ({
  messages,
  input,
  setInput,
  handleSendMessage,
  handleVoiceInput,
  isProcessing,
  progressEntries = [],
  currentPlan = null,
  activeMutation = null,
  onDismissMutation,
  activeBranchId,
  availableBranches,
  pendingAttachments,
  onAttachFiles,
  onClearAttachments,
  onSwitchBranch,
  onMergeIntoActive,
  onRegenerate,
  onCreateBranch,
  onRepairTurn,
  onRateMessage,
  onArchiveConversationSnapshot,
  onResetConversation,
  canArchiveConversation = false,
  canResetConversation = false,
  conversationSessionEpoch,
  streamingMessageId,
  startupState = 'ready',
  startupAlert,
  liveWebMode,
  onChangeLiveWebMode,
  preferredLanguage,
  canResumeLastTurn = false,
  onResumeLastTurn,
}) => {
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const scrollHostRef = useRef<HTMLDivElement>(null);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [isApplyingMutation, setIsApplyingMutation] = useState(false);
  const [isMergeSheetOpen, setIsMergeSheetOpen] = useState(false);
  const [isResetSheetOpen, setIsResetSheetOpen] = useState(false);
  const [isResettingConversation, setIsResettingConversation] = useState(false);
  const [isArchivingConversation, setIsArchivingConversation] = useState(false);
  const [archiveConversationNotice, setArchiveConversationNotice] = useState<string | null>(null);
  const [resetConversationNotice, setResetConversationNotice] = useState<string | null>(null);
  const [mergeSourceBranchId, setMergeSourceBranchId] = useState('');
  const lastMessageId = messages[messages.length - 1]?.id;
  const visibleProgressEntries = useMemo(
    () => progressEntries.slice(-80),
    [progressEntries],
  );
  const mergeSourceOptions = useMemo(
    () => availableBranches.filter(branch => branch !== activeBranchId),
    [activeBranchId, availableBranches],
  );
  const lastVoiceAssistantMessage = useMemo(
    () => [...messages]
      .reverse()
      .find(message => message.role === 'assistant' && message.inputMode === 'voice' && message.content.trim().length > 0),
    [messages],
  );
  const { size: voiceRailWidth, resizeBy: resizeVoiceRail } = usePersistentPanelSize({
    storageKey: 'helper-core.voice-rail-width',
    defaultSize: 304,
    minSize: 272,
    maxSize: 420,
  });

  useEffect(() => {
    setMutationError(null);
    setIsApplyingMutation(false);
  }, [activeMutation?.id]);

  useEffect(() => {
    setIsResetSheetOpen(false);
    setIsResettingConversation(false);
  }, [conversationSessionEpoch]);

  useEffect(() => {
    if (!isMergeSheetOpen) {
      return;
    }

    if (mergeSourceOptions.length === 0) {
      setIsMergeSheetOpen(false);
      setMergeSourceBranchId('');
      return;
    }

    setMergeSourceBranchId(previous => (
      previous && mergeSourceOptions.includes(previous)
        ? previous
        : mergeSourceOptions[0]
    ));
  }, [isMergeSheetOpen, mergeSourceOptions]);

  useEffect(() => {
    const host = scrollHostRef.current;
    const marker = messagesEndRef.current;
    if (!host || !marker) {
      return;
    }

    const distanceFromBottom = host.scrollHeight - host.scrollTop - host.clientHeight;
    if (distanceFromBottom > 180) {
      return;
    }

    const timer = window.setTimeout(() => {
      marker.scrollIntoView({ behavior: 'auto', block: 'end' });
    }, 60);

    return () => window.clearTimeout(timer);
  }, [lastMessageId]);

  return (
    <div className="flex h-full bg-slate-950 overflow-hidden relative">
      {activeMutation && (
        <div className="absolute inset-0 p-12 bg-slate-950/90 backdrop-blur-md flex items-center justify-center" style={{ zIndex: 100 }}>
          <div className="w-full max-w-6xl" style={{ height: '80vh' }}>
            <DiffViewer
              filename={activeMutation.filePath}
              oldCode={activeMutation.originalCode}
              newCode={activeMutation.proposedCode}
              onApprove={async () => {
                setIsApplyingMutation(true);
                setMutationError(null);
                try {
                  await projectService.applyMutation(activeMutation);
                  onDismissMutation();
                } catch (mutationApplyError) {
                  setMutationError(mutationApplyError instanceof Error ? mutationApplyError.message : 'Mutation apply failed.');
                } finally {
                  setIsApplyingMutation(false);
                }
              }}
              onReject={onDismissMutation}
            />
            {(mutationError || isApplyingMutation) && (
              <div className="mt-3 rounded-xl border border-slate-800 bg-black/50 px-4 py-3 text-xs text-slate-300">
                {isApplyingMutation ? 'Applying mutation to workspace...' : mutationError}
              </div>
            )}
          </div>
        </div>
      )}

      <div className="flex-1 flex flex-col relative shadow-2xl">
        <div ref={scrollHostRef} className="flex-1 overflow-y-auto p-6 space-y-6">
          {startupState !== 'ready' && (
            <div className={`rounded-lg border px-4 py-3 text-sm ${
              startupState === 'booting'
                ? 'border-blue-800 bg-blue-950/30 text-blue-200'
                : 'border-amber-800 bg-amber-950/30 text-amber-200'
            }`}>
              <div>
                {startupState === 'booting'
                  ? 'Backend startup is still in progress. Chat unlocks after readiness completes.'
                  : (startupAlert || 'Backend is available with warnings. Expect degraded latency until warmup stabilizes.')}
              </div>
              {canResumeLastTurn && startupState !== 'booting' && (
                <button
                  onClick={onResumeLastTurn}
                  className="mt-3 text-[11px] px-3 py-1 rounded border border-amber-700 hover:border-amber-500 text-amber-100"
                >
                  Resume Last Turn
                </button>
              )}
            </div>
          )}

          {resetConversationNotice && (
            <div className="rounded-lg border border-amber-800 bg-amber-950/20 px-4 py-3 text-sm text-amber-200">
              {resetConversationNotice}
            </div>
          )}

          {archiveConversationNotice && (
            <div className="rounded-lg border border-blue-800 bg-blue-950/20 px-4 py-3 text-sm text-blue-200">
              {archiveConversationNotice}
            </div>
          )}

          {currentPlan && (
            <div className="rounded-lg border border-indigo-800/70 bg-indigo-950/20 px-4 py-3">
              <div className="text-[10px] uppercase tracking-wide text-indigo-300 mb-2">Current Strategy</div>
              <div className="text-sm text-slate-200">{currentPlan.reasoning}</div>
              {currentPlan.options.length > 0 && (
                <div className="mt-3 flex flex-wrap gap-2">
                  {currentPlan.options.map(option => (
                    <span
                      key={option.id}
                      className={`rounded border px-2 py-1 text-[11px] ${
                        option.id === currentPlan.selectedStrategyId
                          ? 'border-indigo-500 bg-indigo-500/10 text-indigo-200'
                          : 'border-slate-700 bg-slate-900/50 text-slate-400'
                      }`}
                    >
                      {option.description}
                    </span>
                  ))}
                </div>
              )}
            </div>
          )}

          {messages.length === 0 && (
            <div className="flex flex-col items-center justify-center h-full text-slate-500 opacity-50">
              <div className="text-6xl mb-4">✨</div>
              <p className="text-lg font-light tracking-widest">HELPER CORE ONLINE</p>
              <p className="text-xs font-mono mt-2">Strategic generation runtime ready.</p>
            </div>
          )}

          <MessageList
            messages={messages}
            isProcessing={isProcessing}
            streamingMessageId={streamingMessageId}
            onRegenerate={onRegenerate}
            onCreateBranch={onCreateBranch}
            onRepairTurn={onRepairTurn}
            onRateMessage={onRateMessage}
          />

          <ActiveStreamingMessage isProcessing={isProcessing} progressEntries={visibleProgressEntries} />

          <div ref={messagesEndRef} />
        </div>

        <div className="p-6 bg-slate-900 border-t border-slate-800 shadow-[0_-10px_40px_rgba(0,0,0,0.2)]">
          <div className="mb-3 flex flex-wrap items-center gap-3">
            <label className="text-[11px] uppercase tracking-wide text-slate-500">Branch</label>
            <select
              value={activeBranchId}
              onChange={(e) => onSwitchBranch(e.target.value)}
              className="bg-black/40 border border-slate-700 rounded px-2 py-1 text-xs text-slate-200"
            >
              {availableBranches.map(branch => (
                <option key={branch} value={branch}>{branch}</option>
              ))}
            </select>
            {availableBranches.length > 1 && (
              <>
                <button
                  onClick={() => setIsMergeSheetOpen(true)}
                  className="text-[11px] px-2 py-1 rounded border border-slate-600 text-slate-300 hover:border-slate-400"
                >
                  Merge Into Active
                </button>
                {isMergeSheetOpen && (
                  <div className="basis-full">
                    <InlineActionSheet
                      title="Merge Branch"
                      description="Pick a source branch and merge it into the currently active branch without leaving the conversation surface."
                      submitLabel="Merge Branch"
                      submitDisabled={!mergeSourceBranchId || mergeSourceBranchId === activeBranchId}
                      disabled={isProcessing}
                      onSubmit={() => {
                        if (!mergeSourceBranchId || mergeSourceBranchId === activeBranchId) {
                          return;
                        }

                        onMergeIntoActive(mergeSourceBranchId);
                        setIsMergeSheetOpen(false);
                        setMergeSourceBranchId('');
                      }}
                      onClose={() => {
                        setIsMergeSheetOpen(false);
                        setMergeSourceBranchId('');
                      }}
                    >
                      <div className="space-y-3">
                        <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2 text-[11px] text-slate-400">
                          Active target branch: <span className="text-slate-200">{activeBranchId}</span>
                        </div>
                        <label className="grid gap-1.5">
                          <span className="text-[11px] uppercase tracking-wide text-slate-500">Source Branch</span>
                          <select
                            value={mergeSourceBranchId}
                            onChange={(event) => setMergeSourceBranchId(event.target.value)}
                            disabled={isProcessing}
                            className="rounded-xl border border-slate-800 bg-slate-950 px-3 py-2 text-sm text-slate-200 outline-none transition-colors focus:border-primary-500 disabled:opacity-50"
                          >
                            {mergeSourceOptions.map(branch => (
                              <option key={branch} value={branch}>{branch}</option>
                            ))}
                          </select>
                        </label>
                      </div>
                    </InlineActionSheet>
                  </div>
                )}
              </>
            )}
            <button
              onClick={() => {
                setArchiveConversationNotice(null);
                setIsArchivingConversation(true);
                void onArchiveConversationSnapshot()
                  .then((notice) => setArchiveConversationNotice(notice))
                  .finally(() => setIsArchivingConversation(false));
              }}
              disabled={isProcessing || !canArchiveConversation || isArchivingConversation}
              className="text-[11px] px-2 py-1 rounded border border-blue-800/70 text-blue-200 hover:border-blue-500 disabled:opacity-40"
            >
              {isArchivingConversation ? 'Saving Snapshot...' : 'Archive / Save Snapshot'}
            </button>
            <button
              onClick={() => {
                setResetConversationNotice(null);
                setIsResetSheetOpen(true);
              }}
              disabled={isProcessing || !canResetConversation || isResettingConversation}
              className="text-[11px] px-2 py-1 rounded border border-amber-800/70 text-amber-200 hover:border-amber-500 disabled:opacity-40"
            >
              Reset Dialog
            </button>
            <label className="text-[11px] uppercase tracking-wide text-slate-500">Attachments</label>
            <input
              type="file"
              multiple
              onChange={(e) => onAttachFiles(e.target.files)}
              className="text-xs text-slate-300"
            />
            {pendingAttachments.length > 0 && (
              <>
                <div className="text-xs text-slate-400">
                  {pendingAttachments.map(item => item.name).join(', ')}
                </div>
                <button
                  onClick={onClearAttachments}
                  className="text-[11px] px-2 py-1 rounded border border-slate-600 text-slate-300 hover:border-slate-400"
                >
                  Clear
                </button>
              </>
            )}
            <label className="text-[11px] uppercase tracking-wide text-slate-500">Live Web</label>
            <select
              value={liveWebMode}
              onChange={(event) => onChangeLiveWebMode(event.target.value as LiveWebMode)}
              className="bg-black/40 border border-slate-700 rounded px-2 py-1 text-xs text-slate-200"
            >
              <option value="auto">Auto</option>
              <option value="force_search">Force Search</option>
              <option value="no_web">No Web</option>
            </select>
            <div className="text-[11px] text-slate-500">
              {liveWebMode === 'force_search'
                ? 'Always run live web research for this turn.'
                : liveWebMode === 'no_web'
                  ? 'Keep this turn local, even if live web would help.'
                  : 'Let Helper decide when live web is needed.'}
            </div>
            {isResetSheetOpen && (
              <div className="basis-full">
                <InlineActionSheet
                  title="Reset Dialog"
                  description="Clear the current conversation surface and start a fresh dialog. The existing backend conversation will be deleted when possible."
                  submitLabel={isResettingConversation ? 'Resetting...' : 'Start Fresh Dialog'}
                  submitDisabled={isResettingConversation}
                  disabled={isResettingConversation}
                  onSubmit={() => {
                    setIsResettingConversation(true);
                    setArchiveConversationNotice(null);
                    void onResetConversation()
                      .then((warning) => {
                        setResetConversationNotice(warning ?? 'Started a fresh dialog.');
                        setIsResetSheetOpen(false);
                      })
                      .finally(() => setIsResettingConversation(false));
                  }}
                  onClose={() => {
                    if (isResettingConversation) {
                      return;
                    }

                    setIsResetSheetOpen(false);
                  }}
                >
                  <div className="rounded-xl border border-amber-900/60 bg-amber-950/20 px-3 py-3 text-xs text-amber-100">
                    This clears visible messages, branches, attachments, pending turn state, and voice session state for the current chat surface.
                  </div>
                </InlineActionSheet>
              </div>
            )}
          </div>
          <div className="flex flex-col gap-4 lg:flex-row lg:items-stretch">
            <div className="flex min-w-0 flex-1 flex-col gap-4">
              <div className="relative flex flex-1 gap-4">
                <input
                  type="text"
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleSendMessage()}
                  placeholder={startupState === 'booting' ? 'Waiting for backend readiness...' : 'Instruct Helper...'}
                  disabled={startupState === 'booting'}
                  className="flex-1 bg-slate-950 border border-slate-800 rounded-lg px-5 py-4 text-slate-200 focus:outline-none focus:border-primary-500 focus:ring-1 focus:ring-primary-500 transition-all shadow-inner disabled:opacity-60"
                />
                <button
                  onClick={handleSendMessage}
                  disabled={startupState === 'booting' || isProcessing || !input.trim()}
                  className="bg-gradient-to-r from-primary-600 to-primary-500 hover:from-primary-500 hover:to-primary-400 text-white px-8 py-3 rounded-lg font-bold uppercase tracking-wide text-xs transition-all disabled:opacity-50 shadow-lg"
                >
                  Execute
                </button>
              </div>

              <div className="lg:hidden">
                <VoiceInterface
                  key={`voice-mobile-${conversationSessionEpoch}`}
                  isProcessing={isProcessing}
                  preferredLanguage={preferredLanguage}
                  onInput={handleVoiceInput}
                  lastMessage={lastVoiceAssistantMessage?.content}
                  lastMessageInputMode={lastVoiceAssistantMessage?.inputMode}
                />
              </div>
            </div>
            <div className="hidden lg:block">
              <PanelResizeHandle
                axis="x"
                title="Resize Helper Core voice rail"
                onResizeDelta={(delta) => resizeVoiceRail(-delta)}
              />
            </div>
            <div className="hidden lg:block shrink-0" style={{ width: `${voiceRailWidth}px` }}>
              <VoiceInterface
                key={`voice-desktop-${conversationSessionEpoch}`}
                isProcessing={isProcessing}
                preferredLanguage={preferredLanguage}
                onInput={handleVoiceInput}
                lastMessage={lastVoiceAssistantMessage?.content}
                lastMessageInputMode={lastVoiceAssistantMessage?.inputMode}
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
