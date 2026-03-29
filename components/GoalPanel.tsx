import React, { useMemo, useState } from 'react';
import { Goal } from '../types';
import { useGoalsState } from '../contexts/GoalsStateContext';

type GoalFilter = 'active' | 'completed' | 'all';

type GoalDraft = {
  title: string;
  description: string;
};

const EMPTY_DRAFT: GoalDraft = { title: '', description: '' };

export const GoalPanel: React.FC = () => {
  const {
    goals,
    activeGoals,
    completedGoals,
    isLoading,
    isSubmitting,
    error,
    addGoal: addGoalToStore,
    updateGoal: updateGoalInStore,
    completeGoal: completeGoalInStore,
    deleteGoal: deleteGoalInStore,
    clearError,
  } = useGoalsState();
  const [newGoal, setNewGoal] = useState<GoalDraft>(EMPTY_DRAFT);
  const [editingGoalId, setEditingGoalId] = useState<string | null>(null);
  const [editingDraft, setEditingDraft] = useState<GoalDraft>(EMPTY_DRAFT);
  const [filter, setFilter] = useState<GoalFilter>('active');

  const visibleGoals = useMemo(() => goals.filter(goal => {
    if (filter === 'active') {
      return !goal.isCompleted;
    }

    if (filter === 'completed') {
      return goal.isCompleted;
    }

    return true;
  }), [filter, goals]);

  const activeCount = activeGoals.length;
  const completedCount = completedGoals.length;

  const addGoal = async () => {
    if (!newGoal.title.trim()) {
      await addGoalToStore(newGoal);
      return;
    }

    try {
      clearError();
      await addGoalToStore(newGoal);
      setNewGoal(EMPTY_DRAFT);
    } catch {
      // Shared goals context already exposes the actionable error.
    }
  };

  const startEditing = (goal: Goal) => {
    setEditingGoalId(goal.id);
    setEditingDraft({
      title: goal.title,
      description: goal.description,
    });
  };

  const cancelEditing = () => {
    setEditingGoalId(null);
    setEditingDraft(EMPTY_DRAFT);
  };

  const saveGoal = async (goalId: string) => {
    if (!editingDraft.title.trim()) {
      await updateGoalInStore(goalId, editingDraft);
      return;
    }

    try {
      clearError();
      await updateGoalInStore(goalId, editingDraft);
      cancelEditing();
    } catch {
      // Shared goals context already exposes the actionable error.
    }
  };

  const completeGoal = async (goalId: string) => {
    try {
      clearError();
      await completeGoalInStore(goalId);
    } catch {
      // Shared goals context already exposes the actionable error.
    }
  };

  const deleteGoal = async (goalId: string) => {
    try {
      clearError();
      await deleteGoalInStore(goalId);
    } catch {
      // Shared goals context already exposes the actionable error.
    }
  };

  return (
    <div className="bg-slate-900/80 border border-slate-800 rounded-xl p-4 flex flex-col gap-4 shadow-2xl">
      <div className="flex justify-between items-center border-b border-slate-800 pb-2">
        <div>
          <h3 className="text-xs font-bold text-primary-400 uppercase tracking-widest">Objectives</h3>
          <div className="mt-1 text-[10px] text-slate-500 font-mono">{activeCount} active / {completedCount} completed</div>
        </div>
        <div className="flex gap-2">
          {(['active', 'completed', 'all'] as const).map(option => (
            <button
              key={option}
              type="button"
              onClick={() => setFilter(option)}
              className={`px-2 py-1 rounded text-[10px] uppercase tracking-widest border transition-colors ${
                filter === option
                  ? 'border-primary-500 bg-primary-500/10 text-primary-200'
                  : 'border-slate-700 text-slate-500 hover:text-slate-300'
              }`}
            >
              {option}
            </button>
          ))}
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-rose-900/50 bg-rose-950/20 px-3 py-2 text-xs text-rose-300">
          {error}
        </div>
      )}

      <div className="flex flex-col gap-2 max-h-80 overflow-y-auto pr-2 custom-scrollbar">
        {isLoading ? (
          <div className="p-3 bg-black/20 border border-slate-800 rounded-lg text-xs text-slate-500">
            Loading objectives...
          </div>
        ) : visibleGoals.length === 0 ? (
          <div className="p-3 bg-black/20 border border-dashed border-slate-800 rounded-lg text-xs text-slate-500">
            No objectives in the current filter.
          </div>
        ) : (
          visibleGoals.map(goal => {
            const isEditing = editingGoalId === goal.id;
            return (
              <div key={goal.id} className={`p-3 border rounded-lg transition-all ${goal.isCompleted ? 'bg-black/20 border-slate-800 opacity-70' : 'bg-black/40 border-slate-800 hover:border-primary-500/50'}`}>
                {isEditing ? (
                  <div className="space-y-2">
                    <input
                      value={editingDraft.title}
                      onChange={event => setEditingDraft(current => ({ ...current, title: event.target.value }))}
                      className="w-full bg-black/60 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200 focus:border-primary-500 outline-none"
                    />
                    <textarea
                      value={editingDraft.description}
                      onChange={event => setEditingDraft(current => ({ ...current, description: event.target.value }))}
                      className="w-full bg-black/60 border border-slate-800 rounded px-3 py-2 text-[11px] text-slate-300 focus:border-primary-500 outline-none h-20 resize-none"
                    />
                    <div className="flex gap-2">
                      <button
                        type="button"
                        onClick={() => void saveGoal(goal.id)}
                        disabled={isSubmitting}
                        className="px-3 py-1.5 rounded bg-emerald-600 text-white text-[10px] font-bold uppercase tracking-widest disabled:opacity-50"
                      >
                        Save
                      </button>
                      <button
                        type="button"
                        onClick={cancelEditing}
                        className="px-3 py-1.5 rounded border border-slate-700 text-slate-400 text-[10px] font-bold uppercase tracking-widest"
                      >
                        Cancel
                      </button>
                    </div>
                  </div>
                ) : (
                  <>
                    <div className="flex justify-between items-start gap-4">
                      <div>
                        <div className="text-xs font-bold text-slate-200 mb-1">{goal.title}</div>
                        <div className="text-[10px] text-slate-500 leading-relaxed">{goal.description || 'No description provided.'}</div>
                      </div>
                      <div className={`text-[9px] font-bold uppercase tracking-widest ${goal.isCompleted ? 'text-emerald-400' : 'text-amber-300'}`}>
                        {goal.isCompleted ? 'completed' : 'active'}
                      </div>
                    </div>
                    <div className="mt-3 flex gap-2">
                      {!goal.isCompleted && (
                        <button
                          type="button"
                          onClick={() => void completeGoal(goal.id)}
                          disabled={isSubmitting}
                          className="px-2 py-1 rounded bg-primary-600/80 text-white text-[10px] font-bold uppercase tracking-widest disabled:opacity-50"
                        >
                          Complete
                        </button>
                      )}
                      <button
                        type="button"
                        onClick={() => startEditing(goal)}
                        disabled={isSubmitting}
                        className="px-2 py-1 rounded border border-slate-700 text-slate-300 text-[10px] font-bold uppercase tracking-widest disabled:opacity-50"
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        onClick={() => void deleteGoal(goal.id)}
                        disabled={isSubmitting}
                        className="px-2 py-1 rounded border border-rose-900/60 text-rose-300 text-[10px] font-bold uppercase tracking-widest disabled:opacity-50"
                      >
                        Delete
                      </button>
                    </div>
                  </>
                )}
              </div>
            );
          })
        )}
      </div>

      <div className="mt-2 flex flex-col gap-2 border-t border-slate-800 pt-4">
        <input
          placeholder="Objective title..."
          className="bg-black/60 border border-slate-800 rounded px-3 py-2 text-xs text-slate-300 focus:border-primary-500 outline-none"
          value={newGoal.title}
          onChange={event => setNewGoal(current => ({ ...current, title: event.target.value }))}
        />
        <textarea
          placeholder="Description..."
          className="bg-black/60 border border-slate-800 rounded px-3 py-2 text-[10px] text-slate-400 focus:border-primary-500 outline-none h-16 resize-none"
          value={newGoal.description}
          onChange={event => setNewGoal(current => ({ ...current, description: event.target.value }))}
        />
        <button
          onClick={() => void addGoal()}
          disabled={isSubmitting}
          className="bg-primary-600 hover:bg-primary-500 text-white text-[10px] font-bold py-2 rounded transition-colors uppercase tracking-tighter disabled:opacity-50"
        >
          {isSubmitting ? 'Saving...' : 'Add Objective'}
        </button>
      </div>
    </div>
  );
};
