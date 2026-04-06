import React from 'react';
import type { ChatAttachment } from '../types';
import { useLiveVoiceRuntime } from '../hooks/useLiveVoiceRuntime';
import type { LiveVoiceTurnPayload } from '../services/liveVoiceRuntime';
import { formatLiveVoiceDuration } from '../services/liveVoiceRuntime';

interface VoiceInterfaceProps {
  conversationId?: string;
  ensureConversationId?: () => string;
  isProcessing: boolean;
  preferredLanguage: string;
  referenceAttachments: ChatAttachment[];
  onSendVoiceTurn: (payload: LiveVoiceTurnPayload) => void;
  lastAssistantVoiceReply?: string;
}

export const VoiceInterface: React.FC<VoiceInterfaceProps> = ({
  conversationId,
  ensureConversationId,
  isProcessing,
  preferredLanguage,
  referenceAttachments,
  onSendVoiceTurn,
  lastAssistantVoiceReply,
}) => {
  const runtime = useLiveVoiceRuntime({
    conversationId,
    ensureConversationId,
    preferredLanguage,
    referenceAttachments,
    onSubmit: onSendVoiceTurn,
  });

  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-950/80 p-4 shadow-inner lg:w-[19rem]">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">Voice</div>
          <div className="mt-1 text-sm text-slate-100">Live Voice Runtime</div>
          <div className="mt-1 text-xs text-slate-400">
            Live turns stream capture chunks into a server-backed session, preserve continuity across hold and resume, and travel with active visual references.
          </div>
        </div>
        <span className={`rounded-full border px-2 py-1 text-[10px] uppercase tracking-wide ${
          runtime.status === 'capturing'
            ? 'border-red-700 bg-red-950/40 text-red-200'
            : runtime.status === 'held'
              ? 'border-amber-700 bg-amber-950/40 text-amber-200'
              : 'border-slate-700 bg-slate-900/70 text-slate-400'
        }`}>
          {runtime.status}
        </span>
      </div>

      <div className="mt-4 flex gap-2">
        <button
          onClick={() => void runtime.startSession()}
          disabled={isProcessing || !runtime.captureSupported || runtime.status === 'capturing'}
          className="rounded-xl border border-slate-700 bg-slate-900 px-3 py-2 text-xs text-slate-100 transition-colors hover:border-primary-500 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Start Live Mic
        </button>
        <button
          onClick={runtime.status === 'held' ? runtime.resumeSession : runtime.holdSession}
          disabled={runtime.status !== 'capturing' && runtime.status !== 'held'}
          className="rounded-xl border border-slate-700 bg-slate-900 px-3 py-2 text-xs text-slate-300 transition-colors hover:border-slate-500"
        >
          {runtime.status === 'held' ? 'Resume' : 'Hold'}
        </button>
        <button
          onClick={runtime.stopSession}
          disabled={runtime.status !== 'capturing' && runtime.status !== 'held'}
          className="rounded-xl border border-slate-700 bg-slate-900 px-3 py-2 text-xs text-slate-300 transition-colors hover:border-slate-500 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Stop Capture
        </button>
      </div>

      <div className="mt-3 space-y-2 text-[11px] text-slate-400">
        <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2">
          Runtime: <span className="text-slate-200">{runtime.captureSupported ? `Session-driven live capture · ${runtime.voiceLanguage}` : 'unsupported'}</span>
        </div>
        <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2">
          Session: <span className="text-slate-200">{runtime.audioChunkCount > 0 ? `${runtime.audioChunkCount} streamed chunk(s)` : 'ready'}</span>
        </div>
        <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2">
          Duration: <span className="text-slate-200">{formatLiveVoiceDuration(runtime.approximateDurationMs)}</span>
        </div>
        <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2">
          Interruptions: <span className="text-slate-200">{runtime.holdCount}</span>
        </div>
        <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2">
          References: <span className="text-slate-200">{runtime.attachedReferenceSummary}</span>
        </div>
        <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2">
          <div className="text-[10px] uppercase tracking-wide text-slate-500">Intent Hint</div>
          <textarea
            value={runtime.intentHint}
            onChange={(event) => runtime.setIntentHint(event.target.value)}
            rows={2}
            placeholder="Optional: steer the live voice turn if the runtime needs a correction or extra intent hint."
            className="mt-2 w-full rounded-lg border border-slate-800 bg-slate-950 px-3 py-2 text-xs text-slate-200 outline-none transition-colors focus:border-primary-500"
          />
          <div className="mt-2 text-[11px] text-slate-500">
            The runtime can stream a voice turn without a manual transcript. This hint only corrects or sharpens the captured intent when needed.
          </div>
          <div className="mt-2 flex gap-2">
            <button
              type="button"
              onClick={runtime.saveTranscriptSegment}
              disabled={runtime.audioChunkCount === 0 && !runtime.intentHint.trim()}
              className="rounded-full border border-slate-700 px-2 py-1 text-[10px] uppercase tracking-wide text-slate-300 hover:border-slate-500 disabled:opacity-50"
            >
              Mark Segment
            </button>
            <button
              type="button"
              onClick={runtime.sendTurn}
              disabled={isProcessing || (runtime.audioChunkCount === 0 && runtime.transcriptSegments.length === 0 && !runtime.intentHint.trim())}
              className="rounded-full border border-primary-500/40 px-2 py-1 text-[10px] uppercase tracking-wide text-primary-100 hover:border-primary-400/70 disabled:opacity-50"
            >
              Send Voice Turn
            </button>
            <button
              type="button"
              onClick={runtime.discardSession}
              className="rounded-full border border-slate-700 px-2 py-1 text-[10px] uppercase tracking-wide text-slate-300 hover:border-slate-500"
            >
              Discard
            </button>
          </div>
        </div>
        {runtime.sessionTimeline.length > 0 && (
          <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2">
            <div className="text-[10px] uppercase tracking-wide text-slate-500">Session Timeline</div>
            <div className="mt-1 space-y-1 text-xs text-slate-300">
              {runtime.sessionTimeline.slice(0, 4).map((item, index) => (
                <div key={`${index}:${item}`} className="line-clamp-2">
                  {item}
                </div>
              ))}
            </div>
          </div>
        )}
        {lastAssistantVoiceReply && (
          <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2">
            <div className="text-[10px] uppercase tracking-wide text-slate-500">Last Voice Reply</div>
            <div className="mt-1 line-clamp-4 text-xs text-slate-200">{lastAssistantVoiceReply}</div>
          </div>
        )}
        {runtime.errorMessage && (
          <div className="rounded-xl border border-amber-800 bg-amber-950/20 px-3 py-2 text-amber-200">
            {runtime.errorMessage}
          </div>
        )}
      </div>
    </div>
  );
};
