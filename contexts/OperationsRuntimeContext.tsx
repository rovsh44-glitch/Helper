import React, { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { useAdaptivePolling } from '../hooks/useAdaptivePolling';
import {
  getEvolutionLibrarySnapshot,
  getEvolutionStatusSnapshot,
  runEvolutionAction,
  runIndexingAction,
} from '../services/evolutionOperationsApi';
import type { EvolutionLibraryItem, EvolutionStatus } from '../types';

type OperationsRuntimeContextValue = {
  status: EvolutionStatus | null;
  library: EvolutionLibraryItem[];
  selectedBookPath: string;
  setSelectedBookPath: (path: string) => void;
  error: string | null;
  isRefreshing: boolean;
  refresh: () => Promise<void>;
  startEvolution: (targetPath?: string) => Promise<void>;
  pauseEvolution: () => Promise<void>;
  stopEvolution: () => Promise<void>;
  resetEvolution: () => Promise<void>;
  startIndexing: (targetPath?: string) => Promise<void>;
  pauseIndexing: () => Promise<void>;
  resetIndexing: () => Promise<void>;
  clearError: () => void;
};

const OperationsRuntimeContext = createContext<OperationsRuntimeContextValue | null>(null);

export function OperationsRuntimeProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<EvolutionStatus | null>(null);
  const [library, setLibrary] = useState<EvolutionLibraryItem[]>([]);
  const [selectedBookPath, setSelectedBookPath] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);

  const refresh = useCallback(async () => {
    setIsRefreshing(true);
    try {
      const [nextLibrary, nextStatus] = await Promise.all([
        getEvolutionLibrarySnapshot(),
        getEvolutionStatusSnapshot(),
      ]);
      setLibrary(nextLibrary as EvolutionLibraryItem[]);
      setStatus(nextStatus as EvolutionStatus);
      setError(null);
    } catch (refreshError) {
      setError(mapOperationsApiError(refreshError));
    } finally {
      setIsRefreshing(false);
    }
  }, []);

  useAdaptivePolling(refresh, {
    visibleIntervalMs: 4000,
    hiddenIntervalMs: 15000,
  });

  const runAction = useCallback(async (action: () => Promise<unknown>) => {
    try {
      await action();
      await refresh();
      setError(null);
    } catch (actionError) {
      setError(mapOperationsApiError(actionError));
      throw actionError;
    }
  }, [refresh]);

  const value = useMemo<OperationsRuntimeContextValue>(() => ({
    status,
    library,
    selectedBookPath,
    setSelectedBookPath,
    error,
    isRefreshing,
    refresh,
    startEvolution: async (targetPath?: string) => runAction(() => runEvolutionAction('start', { targetPath })),
    pauseEvolution: async () => runAction(() => runEvolutionAction('pause')),
    stopEvolution: async () => runAction(() => runEvolutionAction('stop')),
    resetEvolution: async () => runAction(() => runEvolutionAction('reset')),
    startIndexing: async (targetPath?: string) => runAction(() => runIndexingAction('start', { targetPath })),
    pauseIndexing: async () => runAction(() => runIndexingAction('pause')),
    resetIndexing: async () => runAction(() => runIndexingAction('reset')),
    clearError: () => setError(null),
  }), [error, isRefreshing, library, refresh, runAction, selectedBookPath, status]);

  return (
    <OperationsRuntimeContext.Provider value={value}>
      {children}
    </OperationsRuntimeContext.Provider>
  );
}

export function useOperationsRuntime() {
  const context = useContext(OperationsRuntimeContext);
  if (!context) {
    throw new Error('useOperationsRuntime must be used inside OperationsRuntimeProvider.');
  }

  return context;
}

function mapOperationsApiError(error: unknown): string {
  if (!(error instanceof Error)) {
    return 'Operations runtime failed to reach Helper backend.';
  }

  const message = error.message || '';
  if (message.includes('API 403')) {
    return message.toLowerCase().includes('missing scope')
      ? 'Operations session is missing required scopes (403). Reload the page if the refreshed session still fails.'
      : 'Operations access was denied by backend policy (403).';
  }

  if (message.includes('API 401')) {
    return 'Operations authentication failed (401). Check session bootstrap or backend auth configuration.';
  }

  if (message.toLowerCase().includes('network error') || message.includes('Failed to fetch')) {
    return 'Operations runtime lost connection to Helper backend.';
  }

  return `Operations runtime error: ${message}`;
}
