import { helperApi } from './generatedApiClient';

export async function getGoalsSnapshot(includeCompleted: boolean) {
  return helperApi.getGoals({ includeCompleted });
}

export async function addGoalEntry(body: Parameters<typeof helperApi.addGoal>[0]) {
  return helperApi.addGoal(body);
}

export async function updateGoalEntry(goalId: string, body: Parameters<typeof helperApi.updateGoal>[1]) {
  return helperApi.updateGoal(goalId, body);
}

export async function completeGoalEntry(goalId: string) {
  return helperApi.completeGoal(goalId);
}

export async function deleteGoalEntry(goalId: string) {
  return helperApi.deleteGoal(goalId);
}
