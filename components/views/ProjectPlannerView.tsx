import React, { useEffect, useMemo, useState } from 'react';
import { getTemplateCatalog, planArchitectureDraft } from '../../services/planningApi';
import { useWorkflowState } from '../../contexts/WorkflowStateContext';
import { createBuilderLaunchRequest } from '../../services/workflowDrafts';
import type { ArchitecturePlanAnalysis, ArchitecturePlannerSeed, BuilderLaunchRequest, TemplateCatalogItem } from '../../types';

const TARGET_OS_OPTIONS = [
  { value: 'Windows', label: 'Windows' },
  { value: 'Linux', label: 'Linux' },
  { value: 'MacOS', label: 'macOS' },
] as const;

interface ProjectPlannerViewProps {
  initialSeed?: ArchitecturePlannerSeed | null;
  onOpenBuilder?: (request: BuilderLaunchRequest) => void;
}

export const ProjectPlannerView: React.FC<ProjectPlannerViewProps> = ({ initialSeed = null, onOpenBuilder }) => {
  const {
    plannerPrompt,
    plannerTargetOs,
    plannerAnalysis,
    setPlannerPrompt,
    setPlannerTargetOs,
    setPlannerAnalysis,
    hydratePlannerFromSeed,
  } = useWorkflowState();
  const [templates, setTemplates] = useState<TemplateCatalogItem[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;

    getTemplateCatalog()
      .then(result => {
        if (isMounted) {
          setTemplates(result as TemplateCatalogItem[]);
        }
      })
      .catch(loadError => {
        if (isMounted) {
          setError(loadError instanceof Error ? loadError.message : 'Unable to load template catalog.');
        }
      });

    return () => {
      isMounted = false;
    };
  }, []);

  useEffect(() => {
    if (!initialSeed) {
      return;
    }

    hydratePlannerFromSeed(initialSeed);
    setError(null);
  }, [hydratePlannerFromSeed, initialSeed]);

  const visibleTemplates = useMemo(() => {
    if (templates.length === 0) {
      return [];
    }

    if (!plannerAnalysis) {
      return templates.slice(0, 6);
    }

    const preferred = new Set([
      plannerAnalysis.route.templateId || '',
      ...plannerAnalysis.route.candidates,
    ]);

    const prioritized = templates.filter(template => preferred.has(template.id));
    if (prioritized.length > 0) {
      return prioritized.slice(0, 6);
    }

    return templates.slice(0, 6);
  }, [plannerAnalysis, templates]);

  const handlePlan = async () => {
    if (!plannerPrompt.trim()) {
      setError('Enter a concrete system request before planning architecture.');
      return;
    }

    setIsLoading(true);
    setError(null);
    try {
      const result = await planArchitectureDraft({
        prompt: plannerPrompt.trim(),
        targetOs: plannerTargetOs,
      });
      setPlannerAnalysis(result as ArchitecturePlanAnalysis);
    } catch (planError) {
      setError(planError instanceof Error ? planError.message : 'Architecture planning failed.');
    } finally {
      setIsLoading(false);
    }
  };

  const handleOpenBuilder = () => {
    if (!plannerAnalysis || !onOpenBuilder) {
      return;
    }

    onOpenBuilder(createBuilderLaunchRequest(plannerPrompt, plannerAnalysis));
  };

  return (
    <div className="p-8 h-full bg-slate-950 overflow-y-auto">
      <div className="max-w-7xl mx-auto grid grid-cols-1 xl:grid-cols-[360px_minmax(0,1fr)] gap-6">
        <section className="bg-slate-900/80 border border-slate-800 rounded-2xl p-5 h-fit">
          <div className="text-[10px] uppercase tracking-[0.2em] text-primary-300 font-bold">Planner Console</div>
          <h2 className="text-2xl font-black text-white mt-2">Architecture Planner</h2>
          <p className="text-sm text-slate-400 mt-2">
            Generate a working architecture draft with blueprint validation and template routing.
          </p>

          <label className="block mt-5 text-[11px] uppercase tracking-widest text-slate-500">System Request</label>
          <textarea
            value={plannerPrompt}
            onChange={(event) => setPlannerPrompt(event.target.value)}
            className="mt-2 w-full h-40 rounded-xl bg-slate-950 border border-slate-700 text-slate-100 p-4 text-sm focus:outline-none focus:border-primary-500 resize-none"
          />

          <label className="block mt-4 text-[11px] uppercase tracking-widest text-slate-500">Target OS</label>
          <select
            value={plannerTargetOs}
            onChange={(event) => setPlannerTargetOs(event.target.value as (typeof TARGET_OS_OPTIONS)[number]['value'])}
            className="mt-2 w-full rounded-xl bg-slate-950 border border-slate-700 text-slate-100 px-4 py-3 text-sm focus:outline-none focus:border-primary-500"
          >
            {TARGET_OS_OPTIONS.map(option => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>

          {error && (
            <div className="mt-4 rounded-xl border border-rose-900/50 bg-rose-950/20 px-3 py-2 text-xs text-rose-300">
              {error}
            </div>
          )}

          <button
            onClick={handlePlan}
            disabled={isLoading}
            className="mt-5 w-full rounded-xl bg-primary-600 hover:bg-primary-500 disabled:opacity-50 text-white font-bold uppercase tracking-widest text-xs px-4 py-3 transition-colors"
          >
            {isLoading ? 'Planning...' : 'Plan Architecture'}
          </button>

          <button
            onClick={handleOpenBuilder}
            disabled={!plannerAnalysis || !onOpenBuilder}
            className="mt-3 w-full rounded-xl border border-primary-500/40 bg-primary-950/20 hover:bg-primary-950/40 disabled:opacity-40 text-primary-100 font-bold uppercase tracking-widest text-xs px-4 py-3 transition-colors"
          >
            Generate In Builder
          </button>

          {initialSeed && (
            <div className="mt-4 rounded-xl border border-primary-900/40 bg-primary-950/20 px-3 py-3 text-xs text-primary-100">
              Hydrated from Strategic Map handoff. Route hints and objectives were folded into the planner prompt.
            </div>
          )}

          <div className="mt-6 pt-5 border-t border-slate-800">
            <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold mb-3">Template Catalog Snapshot</div>
            <div className="space-y-2">
              {visibleTemplates.length === 0 && (
                <div className="text-xs text-slate-500">No template metadata available yet.</div>
              )}
              {visibleTemplates.map(template => {
                const isRouted = template.id === plannerAnalysis?.route.templateId;
                return (
                  <div
                    key={template.id}
                    className={`rounded-xl border px-3 py-3 ${isRouted ? 'border-primary-500/60 bg-primary-500/10' : 'border-slate-800 bg-black/20'}`}
                  >
                    <div className="flex justify-between gap-3">
                      <div>
                        <div className="text-sm font-bold text-slate-100">{template.name}</div>
                        <div className="text-[11px] text-slate-500">{template.id}</div>
                      </div>
                      <div className="text-[10px] uppercase tracking-widest text-slate-400">
                        {template.language}
                      </div>
                    </div>
                    <div className="mt-2 text-xs text-slate-400">{template.description}</div>
                  </div>
                );
              })}
            </div>
          </div>
        </section>

        <section className="space-y-6">
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-4">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold">Recommended Route</div>
              <div className="mt-2 text-lg font-bold text-slate-100">
                {plannerAnalysis?.route.templateId || 'Pending'}
              </div>
              <div className="mt-1 text-xs text-slate-400">
                Confidence {Math.round((plannerAnalysis?.route.confidence ?? 0) * 100)}%
              </div>
              <p className="mt-3 text-sm text-slate-400 leading-relaxed">
                {plannerAnalysis?.route.reason || 'Run planning to resolve template routing.'}
              </p>
            </div>

            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-4">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold">Blueprint Gate</div>
              <div className={`mt-2 text-lg font-bold ${plannerAnalysis?.blueprintValid ? 'text-emerald-300' : 'text-amber-300'}`}>
                {plannerAnalysis ? (plannerAnalysis.blueprintValid ? 'Accepted' : 'Needs review') : 'Not run'}
              </div>
              <p className="mt-3 text-sm text-slate-400">
                {plannerAnalysis?.blueprint.targetOs ? `Target OS: ${plannerAnalysis.blueprint.targetOs}` : 'Planner has not produced a blueprint yet.'}
              </p>
            </div>

            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-4">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold">Planned Files</div>
              <div className="mt-2 text-3xl font-black text-white">
                {plannerAnalysis?.plan.plannedFiles.length ?? 0}
              </div>
              <p className="mt-3 text-sm text-slate-400">
                {plannerAnalysis
                  ? new Date(plannerAnalysis.analyzedAtUtc).toLocaleString()
                  : 'Waiting for first planning pass.'}
              </p>
            </div>
          </div>

          <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-5">
            <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold mb-3">Planning Summary</div>
            <div className="text-lg text-slate-100 leading-relaxed">
              {plannerAnalysis?.plan.description || 'Architecture summary will appear here after planning.'}
            </div>
          </div>

          <div className="grid grid-cols-1 2xl:grid-cols-2 gap-6">
            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-5">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold mb-3">Blueprint Reasoning</div>
              <div className="text-sm text-slate-300 leading-relaxed">
                {plannerAnalysis?.blueprint.architectureReasoning || 'No blueprint reasoning yet.'}
              </div>

              {plannerAnalysis?.blueprint.nuGetPackages && plannerAnalysis.blueprint.nuGetPackages.length > 0 && (
                <>
                  <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold mt-5 mb-3">Packages</div>
                  <div className="flex flex-wrap gap-2">
                    {plannerAnalysis.blueprint.nuGetPackages.map(packageName => (
                      <span key={packageName} className="rounded-full border border-slate-700 bg-slate-950 px-3 py-1 text-xs text-slate-300">
                        {packageName}
                      </span>
                    ))}
                  </div>
                </>
              )}
            </div>

            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-5">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold mb-3">Routing Candidates</div>
              <div className="flex flex-wrap gap-2">
                {(plannerAnalysis?.route.candidates ?? []).map(candidate => (
                  <span key={candidate} className="rounded-full border border-slate-700 bg-slate-950 px-3 py-1 text-xs text-slate-300">
                    {candidate}
                  </span>
                ))}
                {(!plannerAnalysis || plannerAnalysis.route.candidates.length === 0) && (
                  <div className="text-sm text-slate-500">No route candidates yet.</div>
                )}
              </div>
            </div>
          </div>

          <div className="grid grid-cols-1 2xl:grid-cols-2 gap-6">
            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-5">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold mb-3">Planned File Tasks</div>
              <div className="space-y-3">
                {(plannerAnalysis?.plan.plannedFiles ?? []).map(file => (
                  <div key={file.path} className="rounded-xl border border-slate-800 bg-black/20 px-4 py-3">
                    <div className="flex items-start justify-between gap-4">
                      <div>
                        <div className="text-sm font-bold text-slate-100">{file.path}</div>
                        <div className="text-xs text-slate-400 mt-1">{file.purpose}</div>
                      </div>
                      <div className="text-[10px] uppercase tracking-widest text-slate-500">
                        {file.dependencies.length} deps
                      </div>
                    </div>
                    {file.technicalContract && (
                      <div className="mt-2 text-[11px] text-slate-500 leading-relaxed">{file.technicalContract}</div>
                    )}
                  </div>
                ))}
                {(!plannerAnalysis || plannerAnalysis.plan.plannedFiles.length === 0) && (
                  <div className="text-sm text-slate-500">Planner has not proposed file tasks yet.</div>
                )}
              </div>
            </div>

            <div className="bg-slate-900/70 border border-slate-800 rounded-2xl p-5">
              <div className="text-[10px] uppercase tracking-widest text-slate-500 font-bold mb-3">Blueprint File Skeleton</div>
              <div className="space-y-3">
                {(plannerAnalysis?.blueprint.files ?? []).map(file => (
                  <div key={file.path} className="rounded-xl border border-slate-800 bg-black/20 px-4 py-3">
                    <div className="text-sm font-bold text-slate-100">{file.path}</div>
                    <div className="mt-1 text-xs text-slate-400">
                      {[file.purpose, file.role, typeof file.methodCount === 'number' ? `${file.methodCount} methods` : undefined, file.description, file.language]
                        .filter(Boolean)
                        .join(' • ') || 'No extra metadata'}
                    </div>
                  </div>
                ))}
                {(!plannerAnalysis || plannerAnalysis.blueprint.files.length === 0) && (
                  <div className="text-sm text-slate-500">Blueprint file skeleton will appear here.</div>
                )}
              </div>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
};
