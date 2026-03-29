import React from 'react';
import type { RouteTelemetryOverview } from '../../services/runtimeTelemetry';

type InfrastructureCard = {
  label: string;
  value: string;
  note: string;
};

type SettingsInfrastructurePanelProps = {
  isRefreshingRuntime: boolean;
  infrastructureCards: InfrastructureCard[];
  routeTelemetry: RouteTelemetryOverview;
};

export const SettingsInfrastructurePanel: React.FC<SettingsInfrastructurePanelProps> = ({
  isRefreshingRuntime,
  infrastructureCards,
  routeTelemetry,
}) => (
  <div className="bg-slate-900 p-6 rounded-xl border border-slate-800">
    <h3 className="text-sm font-bold text-primary-400 uppercase mb-4">Infrastructure</h3>
    <p className="text-[11px] text-slate-500 mb-4">
      This section reflects the live backend control plane and structured route telemetry. No static product claims are shown here.
    </p>
    <div className="mb-4 rounded-xl border border-cyan-500/20 bg-cyan-500/5 px-4 py-3 text-[11px] text-cyan-100/90">
      {isRefreshingRuntime ? 'Refreshing runtime telemetry...' : 'Runtime telemetry is polled live from the backend control plane.'}
    </div>
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      {infrastructureCards.map(card => (
        <div key={card.label} className="p-3 bg-black/20 rounded border border-white/5">
          <div className="text-[10px] text-slate-500 uppercase">{card.label}</div>
          <div className="text-sm text-slate-300">{card.value}</div>
          <div className="text-[10px] text-slate-500 mt-1">{card.note}</div>
        </div>
      ))}
    </div>
    <div className="mt-4 space-y-2">
      {routeTelemetry.recent.slice(0, 3).map(event => (
        <div key={`${event.recordedAtUtc}-${event.correlationId ?? event.routeKey ?? event.operationKind}`} className="rounded-lg border border-white/5 bg-black/20 px-3 py-2">
          <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">
            {event.channel} · {event.operationKind} · {event.quality}
          </div>
          <div className="mt-1 text-sm text-slate-300">{event.routeKey || 'Route unavailable'}</div>
        </div>
      ))}
      {routeTelemetry.recent.length === 0 && (
        <div className="rounded-lg border border-dashed border-white/5 bg-black/10 px-3 py-2 text-[11px] text-slate-500">
          No route telemetry events have been recorded yet.
        </div>
      )}
    </div>
  </div>
);
