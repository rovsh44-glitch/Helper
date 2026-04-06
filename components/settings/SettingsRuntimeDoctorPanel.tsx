import React, { useEffect, useState } from 'react';
import { getProviderProfilesSnapshot } from '../../services/providerProfilesApi';
import { runProviderDoctor, type ProviderDoctorReportDto } from '../../services/providerDoctorApi';

function statusTone(status: string): string {
  switch (status.toLowerCase()) {
    case 'healthy':
      return 'text-emerald-100 border-emerald-500/30 bg-emerald-500/10';
    case 'failed':
      return 'text-rose-100 border-rose-500/30 bg-rose-500/10';
    default:
      return 'text-amber-100 border-amber-500/30 bg-amber-500/10';
  }
}

export const SettingsRuntimeDoctorPanel: React.FC = () => {
  const [report, setReport] = useState<ProviderDoctorReportDto | null>(null);
  const [profileId, setProfileId] = useState<string>('');
  const [profileOptions, setProfileOptions] = useState<Array<{ id: string; displayName: string }>>([]);
  const [isRunning, setIsRunning] = useState(false);
  const [status, setStatus] = useState('Run backend-owned probes to validate endpoint reachability, credential wiring, and catalog visibility.');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;

    const loadProfiles = async () => {
      try {
        const snapshot = await getProviderProfilesSnapshot();
        if (!isMounted) {
          return;
        }

        setProfileOptions(snapshot.profiles.map(profile => ({
          id: profile.profile.id,
          displayName: profile.profile.displayName,
        })));
      } catch {
        if (isMounted) {
          setProfileOptions([]);
        }
      }
    };

    void loadProfiles();

    return () => {
      isMounted = false;
    };
  }, []);

  const runDoctor = async () => {
    setIsRunning(true);
    setStatus(profileId ? `Running doctor for ${profileId}...` : 'Running doctor for all provider profiles...');
    try {
      const nextReport = await runProviderDoctor({
        profileId: profileId || undefined,
        includeInactive: true,
      });
      setReport(nextReport);
      setError(null);
      setStatus(`Doctor completed at ${new Date(nextReport.generatedAtUtc).toLocaleTimeString()} with status ${nextReport.status}.`);
    } catch (runError) {
      setError(runError instanceof Error ? runError.message : 'Runtime doctor failed.');
      setStatus('Runtime doctor failed.');
    } finally {
      setIsRunning(false);
    }
  };

  return (
    <div className="bg-slate-900 p-6 rounded-xl border border-slate-800">
      <h3 className="text-sm font-bold text-primary-400 uppercase mb-4">Runtime Doctor</h3>
      <p className="text-[11px] text-slate-500 mb-4">
        This doctor runs from the backend and probes the configured transports directly. It does not trust browser state and it does not expose secrets.
      </p>
      <div className="grid grid-cols-1 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)] gap-4">
        <div className="rounded-xl border border-white/5 bg-black/20 p-4">
          <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">Run Doctor</div>
          <label className="mt-3 block text-[11px] text-slate-400">
            Scope
            <select
              value={profileId}
              onChange={(event) => setProfileId(event.target.value)}
              className="mt-2 w-full rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-200"
            >
              <option value="">All provider profiles</option>
              {profileOptions.map(option => (
                <option key={option.id} value={option.id}>{option.displayName}</option>
              ))}
            </select>
          </label>
          <button
            onClick={() => void runDoctor()}
            disabled={isRunning}
            className="mt-4 rounded-full border border-cyan-500/40 px-4 py-2 text-[10px] font-bold uppercase tracking-widest text-cyan-100 hover:border-cyan-300 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {isRunning ? 'Running...' : 'Run Runtime Doctor'}
          </button>
          <div className="mt-4 rounded-lg border border-cyan-500/20 bg-cyan-500/5 px-3 py-3 text-[11px] text-cyan-100/90">
            {status}
          </div>
          {error && (
            <div className="mt-3 rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-3 text-[11px] text-rose-100">
              {error}
            </div>
          )}
          {report?.alerts.length ? (
            <div className="mt-3 rounded-lg border border-amber-500/20 bg-amber-500/10 px-3 py-3 text-[11px] text-amber-100">
              {report.alerts.join(' ')}
            </div>
          ) : null}
        </div>
        <div className="space-y-3">
          {report && (
            <div className={`rounded-xl border px-4 py-3 ${statusTone(report.status)}`}>
              <div className="text-[10px] uppercase tracking-[0.18em]">Doctor Status</div>
              <div className="mt-1 text-sm font-semibold">{report.status}</div>
              <div className="mt-1 text-[11px] opacity-80">
                Active profile: {report.activeProfileId || 'none'}
              </div>
            </div>
          )}
          {report?.profiles.map(profile => (
            <div key={profile.profileId} className="rounded-xl border border-white/5 bg-black/20 p-4">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <div className="text-sm font-semibold text-white">{profile.displayName}</div>
                  <div className="text-[11px] text-slate-400">{profile.baseUrl}</div>
                </div>
                <span className={`rounded-full border px-2 py-1 text-[10px] uppercase tracking-[0.18em] ${statusTone(profile.status)}`}>
                  {profile.status}
                </span>
              </div>
              <div className="mt-3 space-y-2">
                {profile.checks.map(check => (
                  <div key={`${profile.profileId}-${check.code}`} className="rounded-lg border border-white/5 bg-slate-950/80 px-3 py-2">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">{check.code}</div>
                      <div className="text-[10px] uppercase tracking-[0.18em] text-slate-400">
                        {check.status}{typeof check.durationMs === 'number' ? ` · ${check.durationMs} ms` : ''}
                      </div>
                    </div>
                    <div className="mt-1 text-[11px] text-slate-200">{check.summary}</div>
                    {check.detail && <div className="mt-1 text-[11px] text-slate-500">{check.detail}</div>}
                  </div>
                ))}
              </div>
              {profile.alerts.length > 0 && (
                <div className="mt-3 text-[11px] text-rose-100">{profile.alerts.join(' ')}</div>
              )}
              {profile.warnings.length > 0 && (
                <div className="mt-2 text-[11px] text-amber-100">{profile.warnings.join(' ')}</div>
              )}
            </div>
          ))}
          {!report && (
            <div className="rounded-xl border border-dashed border-white/5 bg-black/10 px-4 py-3 text-[11px] text-slate-500">
              No doctor report is loaded yet.
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
