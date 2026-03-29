import { useCallback, useState } from 'react';
import { useAdaptivePolling } from './useAdaptivePolling';
import type { ControlPlaneSnapshotDto } from '../services/api/runtimeApi';
import { getControlPlaneSnapshot } from '../services/api/runtimeApi';

type UseControlPlaneTelemetryOptions = {
  enabled?: boolean;
  visibleIntervalMs?: number;
  hiddenIntervalMs?: number;
};

export function useControlPlaneTelemetry({
  enabled = true,
  visibleIntervalMs = 4000,
  hiddenIntervalMs = 15000,
}: UseControlPlaneTelemetryOptions = {}) {
  const [controlPlane, setControlPlane] = useState<ControlPlaneSnapshotDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);

  const refresh = useCallback(async () => {
    if (!enabled) {
      return;
    }

    setIsRefreshing(true);
    try {
      const snapshot = await getControlPlaneSnapshot();
      setControlPlane(snapshot);
      setError(null);
    } catch (refreshError) {
      setError(mapControlPlaneApiError(refreshError));
    } finally {
      setIsRefreshing(false);
    }
  }, [enabled]);

  useAdaptivePolling(refresh, {
    enabled,
    visibleIntervalMs,
    hiddenIntervalMs,
  });

  return {
    controlPlane,
    error,
    isRefreshing,
    refresh,
  };
}

export function mapControlPlaneApiError(error: unknown): string {
  if (!(error instanceof Error)) {
    return 'Control-plane telemetry is unavailable.';
  }

  const message = error.message || '';
  if (message.includes('API 403')) {
    return message.toLowerCase().includes('missing scope')
      ? 'Runtime telemetry session is missing required scopes (403). Reload the page if the refreshed session still fails.'
      : 'Runtime telemetry access was denied by backend policy (403).';
  }

  if (message.includes('API 401')) {
    return 'Runtime telemetry authentication failed (401). Check session bootstrap or backend auth configuration.';
  }

  if (message.toLowerCase().includes('network error') || message.includes('Failed to fetch')) {
    return 'Runtime telemetry lost connection to Helper backend.';
  }

  return `Runtime telemetry error: ${message}`;
}
