import React, { useState } from 'react';
import { useGoalsState } from '../../contexts/GoalsStateContext';
import { planStrategyDraft } from '../../services/planningApi';
import { useWorkflowState } from '../../contexts/WorkflowStateContext';
import type { ArchitecturePlannerSeed, StrategicMapAnalysis, StrategicPlan } from '../../types';
import { createArchitecturePlannerSeed } from '../../services/workflowDrafts';
import { StrategicMindMap } from '../StrategicMindMap';

interface StrategicMapViewProps {
  initialPlan: StrategicPlan | null;
  onOpenPlanner?: (seed: ArchitecturePlannerSeed) => void;
}

const LIVE_ROUTE_REASON = 'Live strategy received from the Helper hub.';

export const StrategicMapView: React.FC<StrategicMapViewProps> = ({ initialPlan, onOpenPlanner }) => {
  const {
    strategyTask,
    strategyContext,
    strategyAnalysis,
    setStrategyTask,
    setStrategyContext,
    setStrategyAnalysis,
  } = useWorkflowState();
  const { activeGoals } = useGoalsState();
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const liveGoalsInScope = activeGoals;

  const displayAnalysis = strategyAnalysis ?? (
    initialPlan
      ? {
          plan: initialPlan,
          activeGoals: [],
          route: {
            matched: false,
            templateId: null,
            confidence: 0,
            candidates: [],
            reason: LIVE_ROUTE_REASON,
          },
          analyzedAtUtc: new Date().toISOString(),
        }
      : null
  );

  const handleAnalyze = async () => {
    if (!strategyTask.trim()) {
      setError('Enter a concrete task to build a strategic map.');
      return;
    }

    setIsLoading(true);
    setError(null);
    try {
      const result = await planStrategyDraft({
        task: strategyTask.trim(),
        context: strategyContext.trim() || undefined,
      });
      setStrategyAnalysis(result as StrategicMapAnalysis);
    } catch (analysisError) {
      setError(analysisError instanceof Error ? analysisError.message : 'Strategy analysis failed.');
    } finally {
      setIsLoading(false);
    }
  };

  const handleOpenPlanner = () => {
    if (!displayAnalysis || !strategyTask.trim() || !onOpenPlanner) {
      return;
    }

    onOpenPlanner(createArchitecturePlannerSeed(strategyTask, strategyContext, displayAnalysis));
  };

  return (
    <div className="p-8 h-full bg-slate-950 overflow-y-auto">
      <div className="max-w-6xl mx-auto grid grid-cols-1 xl:grid-cols-[360px_minmax(0,1fr)] gap-6">
        <section className="bg-slate-900/80 border border-slate-800 rounded-2xl p-5 h-fit">
          <div className="text-[10px] uppercase tracking-[0.2em] text-indigo-300 font-bold">Strategic Input</div>
          <h2 className="text-2xl font-black text-white mt-2">Strategic Map</h2>
          <p className="text-sm text-slate-400 mt-2">
            Build a task-level strategy snapshot with goals, route hints, and clarifying questions.
          </p>

          <label className="block mt-5 text-[11px] uppercase tracking-widest text-slate-500">Task</label>
          <textarea
            value={strategyTask}
            onChange={(event) => setStrategyTask(event.target.value)}
            placeholder="Example: Design an offline-first document analysis assistant with guarded code generation."
            className="mt-2 w-full h-32 rounded-xl bg-slate-950 border border-slate-700 text-slate-100 p-4 text-sm focus:outline-none focus:border-indigo-500 resize-none"
          />

          <label className="block mt-4 text-[11px] uppercase tracking-widest text-slate-500">Optional Context</label>
          <textarea
            value={strategyContext}
            onChange={(event) => setStrategyContext(event.target.value)}
            placeholder="Paste constraints, available assets, system notes, or domain context."
            className="mt-2 w-full h-32 rounded-xl bg-slate-950 border border-slate-700 text-slate-100 p-4 text-sm focus:outline-none focus:border-indigo-500 resize-none"
          />

          {error && (
            <div className="mt-4 rounded-xl border border-rose-900/50 bg-rose-950/20 px-3 py-2 text-xs text-rose-300">
              {error}
            </div>
          )}

          <button
            onClick={handleAnalyze}
            disabled={isLoading}
            className="mt-5 w-full rounded-xl bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white font-bold uppercase tracking-widest text-xs px-4 py-3 transition-colors"
          >
            {isLoading ? 'Analyzing...' : 'Analyze Strategy'}
          </button>

          <button
            onClick={handleOpenPlanner}
            disabled={!displayAnalysis || !strategyTask.trim() || !onOpenPlanner}
            className="mt-3 w-full rounded-xl border border-indigo-500/40 bg-indigo-950/20 hover:bg-indigo-950/40 disabled:opacity-40 text-indigo-100 font-bold uppercase tracking-widest text-xs px-4 py-3 transition-colors"
          >
            Send To Architecture
          </button>
        </section>

        <section className="space-y-6">
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-4">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold">Route</div>
              <div className="mt-2 text-lg font-bold text-slate-100">
                {displayAnalysis?.route.templateId || 'No deterministic route yet'}
              </div>
              <div className="mt-1 text-xs text-slate-400">
                Confidence {Math.round((displayAnalysis?.route.confidence ?? 0) * 100)}%
              </div>
              <p className="mt-3 text-sm text-slate-400 leading-relaxed">
                {displayAnalysis?.route.reason || 'Run a strategic analysis to resolve the strongest route.'}
              </p>
            </div>

            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-4">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold">Goals In Scope</div>
              <div className="mt-2 text-3xl font-black text-white">
                {liveGoalsInScope.length}
              </div>
              <p className="mt-2 text-sm text-slate-400">
                Active objectives are shared live across tabs and folded into strategy generation automatically.
              </p>
            </div>

            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-4">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold">State</div>
              <div className="mt-2 text-lg font-bold text-slate-100">
                {displayAnalysis?.plan.requiresMoreInfo ? 'Need clarification' : 'Executable'}
              </div>
              <p className="mt-2 text-sm text-slate-400">
                {displayAnalysis
                  ? new Date(displayAnalysis.analyzedAtUtc).toLocaleString()
                  : 'No strategy analysis has been captured yet.'}
              </p>
            </div>
          </div>

          {liveGoalsInScope.length > 0 && (
            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-5">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold mb-3">Active Objectives</div>
              <div className="flex flex-wrap gap-2">
                {liveGoalsInScope.map(goal => (
                  <div key={goal.id} className="rounded-xl border border-slate-700 bg-black/20 px-3 py-2">
                    <div className="text-xs font-bold text-slate-100">{goal.title}</div>
                    {goal.description && <div className="text-[11px] text-slate-400 mt-1">{goal.description}</div>}
                  </div>
                ))}
              </div>
            </div>
          )}

          {displayAnalysis?.route.candidates && displayAnalysis.route.candidates.length > 0 && (
            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-5">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold mb-3">Routing Candidates</div>
              <div className="flex flex-wrap gap-2">
                {displayAnalysis.route.candidates.map(candidate => (
                  <span key={candidate} className="rounded-full border border-slate-700 bg-slate-950 px-3 py-1 text-xs text-slate-300">
                    {candidate}
                  </span>
                ))}
              </div>
            </div>
          )}

          {displayAnalysis?.plan.clarifyingQuestions && displayAnalysis.plan.clarifyingQuestions.length > 0 && (
            <div className="bg-amber-950/20 border border-amber-900/40 rounded-2xl p-5">
              <div className="text-[10px] uppercase tracking-widest text-amber-300 font-bold mb-3">Clarifying Questions</div>
              <div className="space-y-2">
                {displayAnalysis.plan.clarifyingQuestions.map(question => (
                  <div key={question} className="text-sm text-amber-100">{question}</div>
                ))}
              </div>
            </div>
          )}

          <div className="bg-slate-900/60 border border-slate-800 rounded-2xl p-5">
            <StrategicMindMap plan={displayAnalysis?.plan ?? null} />
          </div>
        </section>
      </div>
    </div>
  );
};
