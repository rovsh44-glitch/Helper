import React, { useMemo, useState } from 'react';
import { Brain, Activity, Zap, Shield, Cpu, Play, Pause, Square, RotateCcw } from 'lucide-react';
import { useOperationsRuntime } from '../../contexts/OperationsRuntimeContext';
import { useHelperHubContext } from '../../hooks/useHelperHubContext';
import { useControlPlaneTelemetry } from '../../hooks/useControlPlaneTelemetry';
import { buildRouteTelemetryOverview } from '../../services/runtimeTelemetry';
import { InlineActionSheet } from './InlineActionSheet';

const EvolutionDashboard = () => {
  const [isResetSheetOpen, setIsResetSheetOpen] = useState(false);
  const { thoughts } = useHelperHubContext();
  const {
    status,
    library,
    error,
    startEvolution,
    pauseEvolution,
    stopEvolution,
    resetEvolution,
  } = useOperationsRuntime();
  const {
    controlPlane,
    error: telemetryError,
    isRefreshing: isRefreshingTelemetry,
  } = useControlPlaneTelemetry();
  const thoughtHistory = useMemo(
    () => thoughts
      .filter(thought => thought.type === 'prometheus' || thought.type === 'generation' || thought.type === 'research')
      .slice(0, 20),
    [thoughts],
  );
  const routeTelemetry = useMemo(() => buildRouteTelemetryOverview(controlPlane), [controlPlane]);
  const runtimeAlerts = useMemo(
    () => Array.from(new Set([
      ...(controlPlane?.alerts ?? []),
      ...routeTelemetry.alerts,
    ])),
    [controlPlane, routeTelemetry.alerts],
  );

  const progressPercent = status?.totalFiles > 0 
    ? Math.round((status.processedFiles / status.totalFiles) * 100) 
    : 0;

  const handleResetEvolution = () => {
    setIsResetSheetOpen(false);
    void resetEvolution();
  };

  return (
    <div className="p-6 space-y-6 bg-slate-900 min-h-screen text-slate-100 overflow-y-auto">
      <div className="flex items-center justify-between">
        <h1 className="text-3xl font-bold flex items-center gap-3">
          <Brain className="text-blue-400 w-10 h-10" />
          Helper Evolution Dashboard <span className="text-sm font-mono text-blue-500 bg-blue-500/10 px-2 py-1 rounded">v12.7 AGI</span>
        </h1>
        <div className="flex gap-4">
          <div className="flex bg-slate-800 p-1 rounded-lg border border-slate-700 items-center">
             <button 
                onClick={() => void startEvolution()}
                className={`p-2 rounded-md hover:bg-emerald-500/20 transition-all ${status?.isLearning ? 'text-emerald-400' : 'text-slate-500'}`}
                title="Start Evolution"
             >
                <Play fill={status?.isLearning ? "currentColor" : "none"} size={20} />
             </button>
             <button 
                onClick={() => void pauseEvolution()}
                className={`p-2 rounded-md hover:bg-yellow-500/20 transition-all ${status?.currentPhase === 'Paused' ? 'text-yellow-400' : 'text-slate-500'}`}
                title="Pause Evolution"
             >
                <Pause fill={status?.currentPhase === 'Paused' ? "currentColor" : "none"} size={20} />
             </button>
             <button 
                onClick={() => void stopEvolution()}
                className={`p-2 rounded-md hover:bg-red-500/20 transition-all ${!status?.isLearning && status?.currentPhase !== 'Paused' ? 'text-red-400' : 'text-slate-500'}`}
                title="Stop Runtime"
             >
                <Square fill={!status?.isLearning && status?.currentPhase !== 'Paused' ? "currentColor" : "none"} size={20} />
             </button>
             <div className="w-px h-6 bg-slate-700 mx-1"></div>
             <button 
                onClick={() => setIsResetSheetOpen(true)}
                className="p-2 rounded-md text-slate-500 hover:text-white hover:bg-slate-700 transition-all"
                title="Reset Evolution Queue"
             >
                <RotateCcw size={20} />
             </button>
          </div>
          <div className="bg-slate-800 p-3 rounded-lg border border-slate-700 flex items-center gap-3">
             <Cpu className="text-purple-400" />
             <div>
                <p className="text-xs text-slate-400">Structured Routing</p>
                <p className="font-mono text-sm">{routeTelemetry.totalEvents > 0 ? `${routeTelemetry.totalEvents} events` : 'Idle snapshot'}</p>
                <p className="text-[10px] text-slate-500 mt-1">
                  {routeTelemetry.totalEvents > 0
                    ? `${routeTelemetry.dominantOperationKind} via ${routeTelemetry.dominantChannel}`
                    : 'No route telemetry recorded yet.'}
                </p>
             </div>
          </div>
        </div>
      </div>

      {isResetSheetOpen && (
        <div className="max-w-xl">
          <InlineActionSheet
            title="Reset Evolution Queue"
            description="Clear the current evolution queue and restart runtime progress from a clean state."
            submitLabel="Reset Queue"
            submitTone="danger"
            onSubmit={handleResetEvolution}
            onClose={() => setIsResetSheetOpen(false)}
          >
            <div className="rounded-xl border border-rose-900/40 bg-rose-950/20 px-4 py-3 text-sm text-rose-100">
              Pending queue items and current progress indicators will be discarded. Use this only when you want a fresh evolution pass.
            </div>
          </InlineActionSheet>
        </div>
      )}

      {(error || telemetryError) && (
        <div className="rounded-xl border border-rose-900/40 bg-rose-950/20 px-4 py-3 text-sm text-rose-200 space-y-1">
          {error && <div>{error}</div>}
          {telemetryError && <div>{telemetryError}</div>}
        </div>
      )}

      <div className="rounded-xl border border-slate-800 bg-slate-800/60 px-4 py-3 text-xs text-slate-400">
        Runtime status and library queue are shared with the `Library Indexing Core` view. Structured route telemetry and control-plane guardrails are now shown from the same backend snapshot instead of placeholder cards.
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {/* Status Card */}
        <div className="bg-slate-800 rounded-xl p-6 border border-blue-500/30 shadow-lg shadow-blue-500/5">
          <h2 className="text-xl font-semibold mb-4 flex items-center gap-2">
            <Activity className="text-blue-400" /> Indexing Progress
          </h2>
          <div className="space-y-4">
            <div className="flex justify-between text-sm">
              <span className="text-slate-400">Knowledge Ingested</span>
              <span className="font-mono">{status?.processedFiles} / {status?.totalFiles} files</span>
            </div>
            <div className="h-3 bg-slate-700 rounded-full overflow-hidden border border-slate-600">
              <div 
                className="h-full bg-gradient-to-r from-blue-600 to-emerald-500 transition-all duration-1000 ease-out" 
                style={{ width: `${progressPercent}%` }}
              ></div>
            </div>
            <div>
              <p className="text-slate-400 text-xs mb-1 uppercase tracking-wider">Current Target</p>
              <p className="text-sm font-medium truncate text-blue-300" title={status?.activeTask}>
                {status?.activeTask !== 'None' ? status?.activeTask.split('\\').pop() : 'Waiting for command...'}
              </p>
            </div>
          </div>
        </div>

        {/* Learning Card */}
        <div className="bg-slate-800 rounded-xl p-6 border border-emerald-500/30 shadow-lg shadow-emerald-500/5">
          <h2 className="text-xl font-semibold mb-4 flex items-center gap-2">
            <Zap className="text-emerald-400" /> Insights & Lessons
          </h2>
          <div className="space-y-2 max-h-40 overflow-y-auto custom-scrollbar">
            {status?.recentLearnings && status.recentLearnings.length > 0 ? (
              status.recentLearnings.map((l: string, i: number) => (
                <div key={i} className="bg-emerald-500/5 border border-emerald-500/10 p-2 rounded text-xs text-emerald-200 flex gap-2">
                  <div className="mt-1"><Brain size={12} className="text-emerald-500" /></div>
                  <span>{l}</span>
                </div>
              ))
            ) : (
              <p className="text-slate-500 italic text-sm">No insights extracted in current session.</p>
            )}
          </div>
        </div>

        {/* Guard Card */}
        <div className="bg-slate-800 rounded-xl p-6 border border-red-500/30 shadow-lg shadow-red-500/5">
          <h2 className="text-xl font-semibold mb-4 flex items-center gap-2">
            <Shield className="text-red-400" /> Runtime Guardrails
          </h2>
          <div className="space-y-3">
            <GuardrailRow
              label="Configuration"
              value={controlPlane ? (controlPlane.configuration.isValid ? 'VALID' : 'DEGRADED') : 'UNAVAILABLE'}
              tone={controlPlane?.configuration.isValid ? 'good' : 'warn'}
            />
            <GuardrailRow
              label="Safe Fallback"
              value={controlPlane?.policies.safeFallbackResponsesOnly ? 'ENFORCED' : 'OFF'}
              tone={controlPlane?.policies.safeFallbackResponsesOnly ? 'good' : 'warn'}
            />
            <GuardrailRow
              label="Route Quality"
              value={routeTelemetry.totalEvents > 0 ? routeTelemetry.dominantQuality.toUpperCase() : 'NO DATA'}
              tone={routeTelemetry.failedCount > 0 || routeTelemetry.blockedCount > 0 ? 'warn' : 'good'}
            />
            <GuardrailRow
              label="Audit Queue"
              value={`${controlPlane?.auditQueue.pending ?? 0} pending`}
              tone={(controlPlane?.auditQueue.pending ?? 0) > 0 ? 'warn' : 'good'}
            />
            <p className="text-[10px] text-slate-500 leading-relaxed uppercase tracking-tighter">
              {isRefreshingTelemetry ? 'Refreshing structured telemetry snapshot.' : 'Derived from backend control plane and route telemetry snapshot.'}
            </p>
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="bg-slate-800 rounded-xl p-6 border border-slate-700">
          <h2 className="text-xl font-semibold mb-4 flex items-center gap-2">
            <Cpu className="text-cyan-400" /> Route Telemetry Snapshot
          </h2>
          <div className="grid grid-cols-2 gap-3">
            <TelemetryMetric label="Top channel" value={routeTelemetry.dominantChannel} />
            <TelemetryMetric label="Top op" value={routeTelemetry.dominantOperationKind} />
            <TelemetryMetric label="Top route" value={routeTelemetry.dominantRoute} />
            <TelemetryMetric
              label="Degraded"
              value={`${routeTelemetry.degradedCount + routeTelemetry.failedCount + routeTelemetry.blockedCount}`}
              tone={routeTelemetry.failedCount > 0 || routeTelemetry.blockedCount > 0 ? 'warn' : 'neutral'}
            />
          </div>
        </div>

        <div className="bg-slate-800 rounded-xl p-6 border border-slate-700">
          <h2 className="text-xl font-semibold mb-4 flex items-center gap-2">
            <Shield className="text-amber-400" /> Runtime Alerts
          </h2>
          <div className="space-y-2">
            {runtimeAlerts.length > 0 ? runtimeAlerts.slice(0, 5).map(alert => (
              <div key={alert} className="rounded-xl border border-amber-500/20 bg-amber-500/5 px-3 py-2 text-sm text-amber-100">
                {alert}
              </div>
            )) : (
              <div className="rounded-xl border border-emerald-500/20 bg-emerald-500/5 px-3 py-2 text-sm text-emerald-200">
                No active control-plane or route telemetry alerts.
              </div>
            )}
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Thought Stream */}
        <div className="bg-slate-800 rounded-xl p-6 border border-slate-700 flex flex-col" style={{ height: '500px' }}>
          <h2 className="text-xl font-semibold mb-4 flex justify-between items-center">
            Real-time Thought Stream
            <span className="text-[10px] font-mono text-slate-500">Hub feed</span>
          </h2>
          <div className="flex-1 overflow-y-auto font-mono text-xs space-y-2 pr-2 custom-scrollbar">
            {thoughtHistory.length > 0 ? (
              thoughtHistory.map((thought) => (
                <div key={thought.id} className="text-slate-300 border-l-2 border-blue-500/30 pl-3 py-1 bg-blue-500/5 animate-in fade-in slide-in-from-left-2 duration-300">
                  <span className="text-blue-500/70">[{new Date(thought.timestamp).toLocaleTimeString()}]</span> {thought.content}
                </div>
              ))
            ) : (
              <p className="text-slate-500 italic">Listening for neural activity...</p>
            )}
          </div>
        </div>

        {/* Library Card */}
        <div className="bg-slate-800 rounded-xl p-6 border border-slate-700 flex flex-col" style={{ height: '500px' }}>
          <h2 className="text-xl font-semibold mb-4 flex justify-between items-center text-blue-400">
            Knowledge Repository
            <span className="text-xs font-mono text-slate-500">{library.filter(b => b.status === 'Done').length} Indexed</span>
          </h2>
          <div className="flex-1 overflow-y-auto custom-scrollbar">
            <table className="w-full text-left text-xs border-separate border-spacing-y-2">
              <thead>
                <tr className="text-slate-500 uppercase tracking-wider">
                  <th className="pb-2 pl-2">Book Title</th>
                  <th className="pb-2">Theme</th>
                  <th className="pb-2 pr-2">Status</th>
                </tr>
              </thead>
              <tbody>
                {library.map((book, idx) => (
                  <tr key={idx} className="bg-slate-900/50 border border-slate-700 group hover:bg-slate-700/30 transition-colors">
                    <td className="py-2 pl-2 rounded-l-lg max-w-48 truncate" title={book.name}>
                      {book.name}
                    </td>
                    <td className="py-2 text-slate-400">
                      {book.folder}
                    </td>
                    <td className="py-2 pr-2 rounded-r-lg">
                      <span className={`px-2 py-0.5 rounded text-[10px] font-bold ${
                        book.status === 'Done' ? 'bg-emerald-500/20 text-emerald-400' :
                        book.status === 'Processing' ? 'bg-blue-500/20 text-blue-400 animate-pulse' :
                        book.status.startsWith('Error') ? 'bg-red-500/20 text-red-400' :
                        'bg-slate-700 text-slate-400'
                      }`}>
                        {book.status}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div className="bg-slate-800 rounded-xl p-6 border border-slate-700">
        <h2 className="text-xl font-semibold mb-4 flex items-center gap-2">
          <Activity className="text-cyan-400" /> Recent Route Decisions
        </h2>
        <div className="space-y-3 max-h-64 overflow-y-auto custom-scrollbar">
          {routeTelemetry.recent.length > 0 ? routeTelemetry.recent.slice(0, 6).map(event => (
            <div key={`${event.recordedAtUtc}-${event.correlationId ?? event.routeKey ?? event.operationKind}`} className="rounded-xl border border-slate-700 bg-slate-900/60 px-4 py-3">
              <div className="flex items-center justify-between gap-3 text-[10px] uppercase tracking-[0.2em] text-slate-500">
                <span>{event.channel}</span>
                <span>{new Date(event.recordedAtUtc).toLocaleTimeString()}</span>
              </div>
              <div className="mt-2 text-sm text-slate-200">{event.routeKey || 'Route unavailable'}</div>
              <div className="mt-2 flex flex-wrap gap-2 text-[10px] uppercase tracking-[0.18em]">
                <span className="rounded-full border border-cyan-500/20 bg-cyan-500/10 px-2 py-1 text-cyan-100">{event.operationKind}</span>
                <span className={`rounded-full border px-2 py-1 ${
                  event.quality === 'failed' || event.quality === 'blocked' || event.quality === 'degraded'
                    ? 'border-amber-500/20 bg-amber-500/10 text-amber-100'
                    : 'border-emerald-500/20 bg-emerald-500/10 text-emerald-100'
                }`}>
                  {event.quality}
                </span>
                <span className="rounded-full border border-slate-700 bg-slate-900/80 px-2 py-1 text-slate-300">{event.outcome}</span>
              </div>
            </div>
          )) : (
            <div className="rounded-xl border border-dashed border-slate-700 bg-slate-900/40 px-4 py-6 text-sm text-slate-500">
              Route telemetry is idle. Trigger chat or generation activity to populate this feed.
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

function GuardrailRow({
  label,
  value,
  tone,
}: {
  label: string;
  value: string;
  tone: 'good' | 'warn' | 'neutral';
}) {
  return (
    <div className="flex justify-between items-center bg-slate-900/50 p-2 rounded border border-slate-700/50 gap-3">
      <span className="text-sm">{label}</span>
      <span className={`text-sm font-bold ${tone === 'good' ? 'text-emerald-300' : tone === 'warn' ? 'text-amber-300' : 'text-slate-400'}`}>{value}</span>
    </div>
  );
}

function TelemetryMetric({
  label,
  value,
  tone = 'neutral',
}: {
  label: string;
  value: string;
  tone?: 'warn' | 'neutral';
}) {
  return (
    <div className={`rounded-xl border px-3 py-3 ${tone === 'warn' ? 'border-amber-500/20 bg-amber-500/5' : 'border-slate-700 bg-slate-900/60'}`}>
      <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">{label}</div>
      <div className="mt-1 text-sm text-slate-200">{value}</div>
    </div>
  );
}

export default EvolutionDashboard;
