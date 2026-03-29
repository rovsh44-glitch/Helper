import React, { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import type {
  ArchitecturePlanAnalysis,
  ArchitecturePlannerSeed,
  PlannerTargetOs,
  StrategicMapAnalysis,
} from '../types';

export const DEFAULT_PLANNER_PROMPT = 'Design an offline-first document analysis assistant with a guarded API, local storage, and a React control panel.';

type WorkflowStateContextValue = {
  strategyTask: string;
  strategyContext: string;
  strategyAnalysis: StrategicMapAnalysis | null;
  setStrategyTask: (value: string) => void;
  setStrategyContext: (value: string) => void;
  setStrategyAnalysis: (value: StrategicMapAnalysis | null) => void;
  plannerPrompt: string;
  plannerTargetOs: PlannerTargetOs;
  plannerAnalysis: ArchitecturePlanAnalysis | null;
  setPlannerPrompt: (value: string) => void;
  setPlannerTargetOs: (value: PlannerTargetOs) => void;
  setPlannerAnalysis: (value: ArchitecturePlanAnalysis | null) => void;
  hydratePlannerFromSeed: (seed: ArchitecturePlannerSeed) => void;
};

const WorkflowStateContext = createContext<WorkflowStateContextValue | null>(null);

export function WorkflowStateProvider({ children }: { children: ReactNode }) {
  const [strategyTask, setStrategyTask] = useState('');
  const [strategyContext, setStrategyContext] = useState('');
  const [strategyAnalysis, setStrategyAnalysis] = useState<StrategicMapAnalysis | null>(null);
  const [plannerPrompt, setPlannerPrompt] = useState(DEFAULT_PLANNER_PROMPT);
  const [plannerTargetOs, setPlannerTargetOs] = useState<PlannerTargetOs>('Windows');
  const [plannerAnalysis, setPlannerAnalysis] = useState<ArchitecturePlanAnalysis | null>(null);

  const value = useMemo<WorkflowStateContextValue>(() => ({
    strategyTask,
    strategyContext,
    strategyAnalysis,
    setStrategyTask,
    setStrategyContext,
    setStrategyAnalysis,
    plannerPrompt,
    plannerTargetOs,
    plannerAnalysis,
    setPlannerPrompt,
    setPlannerTargetOs,
    setPlannerAnalysis,
    hydratePlannerFromSeed: (seed: ArchitecturePlannerSeed) => {
      setPlannerPrompt(seed.prompt);
      setPlannerTargetOs(seed.targetOs);
      setPlannerAnalysis(null);
    },
  }), [
    plannerAnalysis,
    plannerPrompt,
    plannerTargetOs,
    strategyAnalysis,
    strategyContext,
    strategyTask,
  ]);

  return (
    <WorkflowStateContext.Provider value={value}>
      {children}
    </WorkflowStateContext.Provider>
  );
}

export function useWorkflowState() {
  const context = useContext(WorkflowStateContext);
  if (!context) {
    throw new Error('useWorkflowState must be used inside WorkflowStateProvider.');
  }

  return context;
}
