import { useCallback, useState } from 'react';
import { useAdaptivePolling } from './useAdaptivePolling';
import type { HumanLikeConversationDashboardSnapshotDto } from '../services/api/runtimeApi';
import { getHumanLikeConversationDashboard } from '../services/api/runtimeApi';

type UseHumanLikeConversationDashboardOptions = {
  enabled?: boolean;
  days?: number;
  visibleIntervalMs?: number;
  hiddenIntervalMs?: number;
};

export function useHumanLikeConversationDashboard({
  enabled = true,
  days = 7,
  visibleIntervalMs = 8000,
  hiddenIntervalMs = 20000,
}: UseHumanLikeConversationDashboardOptions = {}) {
  const [snapshot, setSnapshot] = useState<HumanLikeConversationDashboardSnapshotDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);

  const refresh = useCallback(async () => {
    if (!enabled) {
      return;
    }

    setIsRefreshing(true);
    try {
      const nextSnapshot = await getHumanLikeConversationDashboard(days);
      setSnapshot(nextSnapshot);
      setError(null);
    } catch (refreshError) {
      setError(mapHumanLikeConversationDashboardError(refreshError));
    } finally {
      setIsRefreshing(false);
    }
  }, [days, enabled]);

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

function mapHumanLikeConversationDashboardError(error: unknown): string {
  if (!(error instanceof Error)) {
    return 'Conversation quality dashboard is unavailable.';
  }

  const message = error.message || '';
  if (message.includes('API 403')) {
    return message.toLowerCase().includes('missing scope')
      ? 'Conversation quality dashboard requires runtime-console scopes (403). Reload if the refreshed session still fails.'
      : 'Conversation quality dashboard access was denied by backend policy (403).';
  }

  if (message.includes('API 401')) {
    return 'Conversation quality dashboard authentication failed (401).';
  }

  if (message.toLowerCase().includes('network error') || message.includes('Failed to fetch')) {
    return 'Conversation quality dashboard lost connection to Helper backend.';
  }

  return `Conversation quality dashboard error: ${message}`;
}
