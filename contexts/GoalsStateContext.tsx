import React, { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  addGoalEntry,
  completeGoalEntry,
  deleteGoalEntry,
  getGoalsSnapshot,
  updateGoalEntry,
} from '../services/goalsApi';
import type { Goal } from '../types';

type GoalDraft = {
  title: string;
  description: string;
};

type GoalsStateContextValue = {
  goals: Goal[];
  activeGoals: Goal[];
  completedGoals: Goal[];
  isLoading: boolean;
  isSubmitting: boolean;
  error: string | null;
  refreshGoals: () => Promise<void>;
  addGoal: (draft: GoalDraft) => Promise<void>;
  updateGoal: (goalId: string, draft: GoalDraft) => Promise<void>;
  completeGoal: (goalId: string) => Promise<void>;
  deleteGoal: (goalId: string) => Promise<void>;
  clearError: () => void;
};

const GoalsStateContext = createContext<GoalsStateContextValue | null>(null);

export function GoalsStateProvider({ children }: { children: ReactNode }) {
  const [goals, setGoals] = useState<Goal[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refreshGoals = useCallback(async () => {
    try {
      setError(null);
      const data = await getGoalsSnapshot(true);
      setGoals(data as Goal[]);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Unable to load objectives.');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void refreshGoals();
    const intervalId = window.setInterval(() => void refreshGoals(), 15000);
    return () => window.clearInterval(intervalId);
  }, [refreshGoals]);

  const addGoal = useCallback(async (draft: GoalDraft) => {
    if (!draft.title.trim()) {
      setError('Objective title is required.');
      return;
    }

    setIsSubmitting(true);
    setError(null);
    try {
      await addGoalEntry({
        title: draft.title.trim(),
        description: draft.description.trim(),
      });
      await refreshGoals();
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Failed to create objective.');
      throw submitError;
    } finally {
      setIsSubmitting(false);
    }
  }, [refreshGoals]);

  const updateGoal = useCallback(async (goalId: string, draft: GoalDraft) => {
    if (!draft.title.trim()) {
      setError('Objective title is required.');
      return;
    }

    setIsSubmitting(true);
    setError(null);
    const previousGoals = goals;
    setGoals(current => current.map(goal => goal.id === goalId
      ? { ...goal, title: draft.title.trim(), description: draft.description.trim() }
      : goal));

    try {
      await updateGoalEntry(goalId, {
        title: draft.title.trim(),
        description: draft.description.trim(),
      });
      await refreshGoals();
    } catch (submitError) {
      setGoals(previousGoals);
      setError(submitError instanceof Error ? submitError.message : 'Failed to update objective.');
      throw submitError;
    } finally {
      setIsSubmitting(false);
    }
  }, [goals, refreshGoals]);

  const completeGoal = useCallback(async (goalId: string) => {
    setIsSubmitting(true);
    setError(null);
    const previousGoals = goals;
    setGoals(current => current.map(goal => goal.id === goalId ? { ...goal, isCompleted: true } : goal));
    try {
      await completeGoalEntry(goalId);
      await refreshGoals();
    } catch (submitError) {
      setGoals(previousGoals);
      setError(submitError instanceof Error ? submitError.message : 'Failed to complete objective.');
      throw submitError;
    } finally {
      setIsSubmitting(false);
    }
  }, [goals, refreshGoals]);

  const deleteGoal = useCallback(async (goalId: string) => {
    setIsSubmitting(true);
    setError(null);
    const previousGoals = goals;
    setGoals(current => current.filter(goal => goal.id !== goalId));
    try {
      await deleteGoalEntry(goalId);
      await refreshGoals();
    } catch (submitError) {
      setGoals(previousGoals);
      setError(submitError instanceof Error ? submitError.message : 'Failed to delete objective.');
      throw submitError;
    } finally {
      setIsSubmitting(false);
    }
  }, [goals, refreshGoals]);

  const value = useMemo<GoalsStateContextValue>(() => ({
    goals,
    activeGoals: goals.filter(goal => !goal.isCompleted),
    completedGoals: goals.filter(goal => goal.isCompleted),
    isLoading,
    isSubmitting,
    error,
    refreshGoals,
    addGoal,
    updateGoal,
    completeGoal,
    deleteGoal,
    clearError: () => setError(null),
  }), [addGoal, completeGoal, deleteGoal, error, goals, isLoading, isSubmitting, refreshGoals, updateGoal]);

  return (
    <GoalsStateContext.Provider value={value}>
      {children}
    </GoalsStateContext.Provider>
  );
}

export function useGoalsState() {
  const context = useContext(GoalsStateContext);
  if (!context) {
    throw new Error('useGoalsState must be used inside GoalsStateProvider.');
  }

  return context;
}
