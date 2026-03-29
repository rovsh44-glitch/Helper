import { useCallback, useState } from 'react';
import { useAdaptivePolling } from './useAdaptivePolling';
import type { CapabilityCatalogSnapshotDto } from '../services/api/runtimeApi';
import { getCapabilityCatalogSnapshot } from '../services/api/runtimeApi';

type UseCapabilityCatalogOptions = {
  enabled?: boolean;
  visibleIntervalMs?: number;
  hiddenIntervalMs?: number;
};

export function useCapabilityCatalog({
  enabled = true,
  visibleIntervalMs = 15000,
  hiddenIntervalMs = 30000,
}: UseCapabilityCatalogOptions = {}) {
  const [snapshot, setSnapshot] = useState<CapabilityCatalogSnapshotDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);

  const refresh = useCallback(async () => {
    if (!enabled) {
      return;
    }

    setIsRefreshing(true);
    try {
      const nextSnapshot = await getCapabilityCatalogSnapshot();
      setSnapshot(nextSnapshot);
      setError(null);
    } catch (refreshError) {
      setError(mapCapabilityCatalogError(refreshError));
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
    snapshot,
    error,
    isRefreshing,
    refresh,
  };
}

export type CapabilityCatalogState = ReturnType<typeof useCapabilityCatalog>;

function mapCapabilityCatalogError(error: unknown): string {
  if (!(error instanceof Error)) {
    return 'Capability catalog is unavailable.';
  }

  const message = error.message || '';
  if (message.includes('API 403')) {
    return message.toLowerCase().includes('missing scope')
      ? 'Capability catalog requires runtime-console scopes (403). Reload the page if the refreshed session still fails.'
      : 'Capability catalog access was denied by backend policy (403).';
  }

  if (message.includes('API 401')) {
    return 'Capability catalog authentication failed (401).';
  }

  if (message.toLowerCase().includes('network error') || message.includes('Failed to fetch')) {
    return 'Capability catalog lost connection to Helper backend.';
  }

  return `Capability catalog error: ${message}`;
}
