import React, { useEffect, useMemo, useState } from 'react';
import {
  activateProviderProfile,
  getProviderProfilesSnapshot,
  recommendProviderProfile,
  type ProviderProfileSummaryDto,
  type ProviderProfilesSnapshotDto,
  type ProviderRecommendationRequestDto,
  type ProviderRecommendationResultDto,
} from '../../services/providerProfilesApi';
import type { ProviderRecommendationGoal } from '../../types';

type Preset = {
  id: ProviderRecommendationGoal;
  label: string;
  request: ProviderRecommendationRequestDto;
  note: string;
};

const PRESETS: Preset[] = [
  {
    id: 'local_fast',
    label: 'Local Fast',
    request: { goal: 'local_fast', preferLocal: true, latencyPreference: 'low', codingIntensity: 'low' },
    note: 'Bias toward low-latency local interaction.',
  },
  {
    id: 'local_coder',
    label: 'Local Coder',
    request: { goal: 'local_coder', preferLocal: true, latencyPreference: 'balanced', codingIntensity: 'heavy' },
    note: 'Prefer local coding and patch-generation routes.',
  },
  {
    id: 'hosted_reasoning',
    label: 'Hosted Reasoning',
    request: { goal: 'hosted_reasoning', preferLocal: false, latencyPreference: 'quality', codingIntensity: 'medium' },
    note: 'Bias toward higher reasoning quality when remote capacity is acceptable.',
  },
  {
    id: 'research_verified',
    label: 'Research Verified',
    request: { goal: 'research_verified', preferLocal: false, needVision: false, latencyPreference: 'quality', codingIntensity: 'medium' },
    note: 'Favor verification-oriented reasoning workloads.',
  },
  {
    id: 'privacy_first',
    label: 'Privacy First',
    request: { goal: 'privacy_first', preferLocal: true, latencyPreference: 'balanced', codingIntensity: 'medium' },
    note: 'Prefer local-only or privacy-preserving operation.',
  },
];

function formatGoal(goal: string): string {
  return goal.replace(/_/g, ' ');
}

function findRecommendedProfile(
  snapshot: ProviderProfilesSnapshotDto | null,
  recommendation: ProviderRecommendationResultDto | null,
): ProviderProfileSummaryDto | null {
  if (!snapshot || !recommendation?.recommendedProfileId) {
    return null;
  }

  return snapshot.profiles.find(profile => profile.profile.id === recommendation.recommendedProfileId) ?? null;
}

export const SettingsProviderProfilesPanel: React.FC = () => {
  const [snapshot, setSnapshot] = useState<ProviderProfilesSnapshotDto | null>(null);
  const [recommendation, setRecommendation] = useState<ProviderRecommendationResultDto | null>(null);
  const [status, setStatus] = useState<string>('Loading provider profiles...');
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [activatingId, setActivatingId] = useState<string | null>(null);
  const [selectedPresetId, setSelectedPresetId] = useState<ProviderRecommendationGoal>('local_fast');

  const recommendedProfile = useMemo(
    () => findRecommendedProfile(snapshot, recommendation),
    [recommendation, snapshot],
  );

  const loadSnapshot = async (preserveStatus?: string) => {
    setIsLoading(true);
    try {
      const nextSnapshot = await getProviderProfilesSnapshot();
      setSnapshot(nextSnapshot);
      setError(null);
      setStatus(preserveStatus ?? `Provider snapshot refreshed at ${new Date(nextSnapshot.generatedAtUtc).toLocaleTimeString()}.`);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Provider profile snapshot failed.');
      setStatus('Provider snapshot unavailable.');
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    void loadSnapshot();
  }, []);

  const handleRecommend = async (preset: Preset) => {
    setSelectedPresetId(preset.id);
    setStatus(`Evaluating preset ${preset.label}...`);
    try {
      const result = await recommendProviderProfile(preset.request);
      setRecommendation(result);
      setError(null);
      setStatus(result.recommendedProfileId
        ? `Recommendation ready for ${preset.label}.`
        : `No viable provider recommendation for ${preset.label}.`);
    } catch (recommendError) {
      setError(recommendError instanceof Error ? recommendError.message : 'Recommendation failed.');
      setStatus('Recommendation failed.');
    }
  };

  const handleActivate = async (profileId: string) => {
    setActivatingId(profileId);
    setStatus(`Activating ${profileId}...`);
    try {
      const result = await activateProviderProfile(profileId);
      await loadSnapshot(`Active provider profile is now ${result.activeProfileId ?? profileId}.`);
      setError(null);
    } catch (activateError) {
      setError(activateError instanceof Error ? activateError.message : 'Provider activation failed.');
      setStatus('Provider activation failed.');
    } finally {
      setActivatingId(null);
    }
  };

  return (
    <div className="bg-slate-900 p-6 rounded-xl border border-slate-800">
      <h3 className="text-sm font-bold text-primary-400 uppercase mb-4">Provider Profiles</h3>
      <p className="text-[11px] text-slate-500 mb-4">
        Profiles are backend-owned. Base URLs, active profile state, and credential references are managed server-side and never stored in browser preferences.
      </p>
      <div className="mb-4 rounded-xl border border-cyan-500/20 bg-cyan-500/5 px-4 py-3 text-[11px] text-cyan-100/90">
        {isLoading ? 'Refreshing provider profile snapshot...' : status}
      </div>
      {error && (
        <div className="mb-4 rounded-xl border border-rose-500/30 bg-rose-500/10 px-4 py-3 text-[11px] text-rose-100">
          {error}
        </div>
      )}
      {snapshot?.alerts.length ? (
        <div className="mb-4 rounded-xl border border-amber-500/20 bg-amber-500/10 px-4 py-3 text-[11px] text-amber-100">
          {snapshot.alerts.join(' ')}
        </div>
      ) : null}
      <div className="grid grid-cols-1 xl:grid-cols-[minmax(0,0.8fr)_minmax(0,1.2fr)] gap-4">
        <div className="rounded-xl border border-white/5 bg-black/20 p-4">
          <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">Preset Recommendation</div>
          <div className="mt-3 space-y-2">
            {PRESETS.map(preset => (
              <button
                key={preset.id}
                onClick={() => void handleRecommend(preset)}
                className={`w-full rounded-lg border px-3 py-3 text-left transition ${selectedPresetId === preset.id ? 'border-cyan-400/60 bg-cyan-500/10 text-cyan-50' : 'border-white/5 bg-black/20 text-slate-200 hover:border-slate-500'}`}
              >
                <div className="text-xs font-semibold uppercase tracking-[0.18em]">{preset.label}</div>
                <div className="mt-1 text-[11px] text-slate-400">{preset.note}</div>
              </button>
            ))}
          </div>
          <div className="mt-4 rounded-lg border border-white/5 bg-slate-950/80 p-3">
            <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">Recommendation Result</div>
            {!recommendation && (
              <div className="mt-2 text-[11px] text-slate-500">Run a preset recommendation to get a profile suggestion and explicit reason codes.</div>
            )}
            {recommendation && (
              <div className="mt-2 space-y-2 text-[11px] text-slate-300">
                <div>
                  Recommended: <span className="font-semibold text-white">{recommendedProfile?.profile.displayName ?? recommendation.recommendedProfileId ?? 'none'}</span>
                </div>
                <div className="flex flex-wrap gap-2">
                  {recommendation.reasonCodes.map(reason => (
                    <span key={reason} className="rounded-full border border-cyan-500/30 px-2 py-1 text-[10px] uppercase tracking-[0.14em] text-cyan-100">
                      {formatGoal(reason)}
                    </span>
                  ))}
                </div>
                {recommendation.warnings.length > 0 && (
                  <div className="text-amber-100">{recommendation.warnings.join(' ')}</div>
                )}
                {recommendedProfile && !recommendedProfile.isActive && recommendedProfile.validation.isValid && (
                  <button
                    onClick={() => void handleActivate(recommendedProfile.profile.id)}
                    disabled={activatingId === recommendedProfile.profile.id}
                    className="rounded-full border border-emerald-500/40 px-3 py-1.5 text-[10px] font-bold uppercase tracking-widest text-emerald-100 hover:border-emerald-300 disabled:cursor-not-allowed disabled:opacity-60"
                  >
                    {activatingId === recommendedProfile.profile.id ? 'Activating...' : 'Activate Recommended'}
                  </button>
                )}
              </div>
            )}
          </div>
        </div>
        <div className="space-y-3">
          {snapshot?.profiles.map(summary => (
            <div key={summary.profile.id} className="rounded-xl border border-white/5 bg-black/20 p-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <div className="text-sm font-semibold text-white">{summary.profile.displayName}</div>
                    {summary.isActive && (
                      <span className="rounded-full border border-emerald-500/40 px-2 py-1 text-[10px] uppercase tracking-[0.18em] text-emerald-100">
                        Active
                      </span>
                    )}
                    <span className="rounded-full border border-white/10 px-2 py-1 text-[10px] uppercase tracking-[0.18em] text-slate-300">
                      {formatGoal(summary.profile.transportKind)}
                    </span>
                  </div>
                  <div className="mt-1 text-[11px] text-slate-400">
                    {summary.profile.baseUrl} · {summary.profile.isLocal ? 'local' : 'remote'} · {summary.profile.isBuiltIn ? 'built-in' : 'custom'}
                  </div>
                </div>
                <button
                  onClick={() => void handleActivate(summary.profile.id)}
                  disabled={summary.isActive || !summary.validation.isValid || !summary.profile.enabled || activatingId === summary.profile.id}
                  className="rounded-full border border-primary-500/30 px-3 py-1.5 text-[10px] font-bold uppercase tracking-widest text-primary-100 hover:border-primary-400/60 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  {summary.isActive ? 'Active' : activatingId === summary.profile.id ? 'Activating...' : 'Activate'}
                </button>
              </div>
              <div className="mt-3 grid grid-cols-1 md:grid-cols-3 gap-3 text-[11px] text-slate-300">
                <div className="rounded-lg border border-white/5 bg-slate-950/80 p-3">
                  <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">Capabilities</div>
                  <div className="mt-2 flex flex-wrap gap-2">
                    {summary.capabilities.supportsFast && <span className="rounded-full border border-white/10 px-2 py-1 text-[10px]">fast</span>}
                    {summary.capabilities.supportsReasoning && <span className="rounded-full border border-white/10 px-2 py-1 text-[10px]">reasoning</span>}
                    {summary.capabilities.supportsCoder && <span className="rounded-full border border-white/10 px-2 py-1 text-[10px]">coder</span>}
                    {summary.capabilities.supportsVision && <span className="rounded-full border border-white/10 px-2 py-1 text-[10px]">vision</span>}
                    {summary.capabilities.supportsResearchVerified && <span className="rounded-full border border-white/10 px-2 py-1 text-[10px]">research</span>}
                    {summary.capabilities.supportsPrivacyFirst && <span className="rounded-full border border-white/10 px-2 py-1 text-[10px]">privacy</span>}
                  </div>
                </div>
                <div className="rounded-lg border border-white/5 bg-slate-950/80 p-3">
                  <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">Model Bindings</div>
                  <div className="mt-2 space-y-1">
                    {summary.profile.modelBindings.map(binding => (
                      <div key={`${summary.profile.id}-${binding.modelClass}`} className="text-[11px] text-slate-300">
                        <span className="text-slate-500">{binding.modelClass}:</span> {binding.modelName}
                      </div>
                    ))}
                  </div>
                </div>
                <div className="rounded-lg border border-white/5 bg-slate-950/80 p-3">
                  <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">Validation</div>
                  <div className={`mt-2 text-[11px] ${summary.validation.isValid ? 'text-emerald-100' : 'text-rose-100'}`}>
                    {summary.validation.isValid ? 'Valid' : 'Needs attention'}
                  </div>
                  {summary.profile.credential && (
                    <div className="mt-2 text-[11px] text-slate-400">
                      Credential env: <span className="text-slate-200">{summary.profile.credential.apiKeyEnvVar || 'none'}</span> ({summary.profile.credential.configured ? 'configured' : 'missing'})
                    </div>
                  )}
                  {summary.validation.alerts.length > 0 && (
                    <div className="mt-2 text-[11px] text-rose-100">{summary.validation.alerts.join(' ')}</div>
                  )}
                  {summary.validation.warnings.length > 0 && (
                    <div className="mt-2 text-[11px] text-amber-100">{summary.validation.warnings.join(' ')}</div>
                  )}
                </div>
              </div>
            </div>
          ))}
          {snapshot && snapshot.profiles.length === 0 && (
            <div className="rounded-xl border border-dashed border-white/5 bg-black/10 px-4 py-3 text-[11px] text-slate-500">
              No provider profiles are currently available.
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
