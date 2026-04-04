import React, { createContext, useContext, useMemo, type ReactNode } from 'react';
import { useHelperHub } from './useHelperHub';

type HelperHubState = ReturnType<typeof useHelperHub>;

const HelperHubContext = createContext<HelperHubState | null>(null);

export function HelperHubProvider({ hubUrl, children }: { hubUrl: string; children: ReactNode }) {
  const state = useHelperHub(hubUrl);
  const value = useMemo(() => state, [
    state.activeMutation,
    state.clearConversationSurface,
    state.clearProgressState,
    state.currentPlan,
    state.dismissActiveMutation,
    state.progressEntries,
    state.thoughts,
  ]);

  return (
    <HelperHubContext.Provider value={value}>
      {children}
    </HelperHubContext.Provider>
  );
}

export function useHelperHubContext() {
  const context = useContext(HelperHubContext);
  if (!context) {
    throw new Error('useHelperHubContext must be used inside HelperHubProvider.');
  }

  return context;
}
