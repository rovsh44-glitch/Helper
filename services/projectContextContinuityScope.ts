import type { ContinuityBackgroundTask, ContinuityProactiveTopic } from './settingsContinuityContracts';

function normalizeProjectId(projectId: string | null | undefined): string | null {
  const normalized = projectId?.trim();
  return normalized ? normalized.toLowerCase() : null;
}

export function filterProjectScopedBackgroundTasks(
  tasks: ContinuityBackgroundTask[],
  projectId: string,
): ContinuityBackgroundTask[] {
  const normalizedProjectId = normalizeProjectId(projectId);
  if (!normalizedProjectId) {
    return [];
  }

  return tasks.filter((task) => normalizeProjectId(task.projectId) === normalizedProjectId);
}

export function filterProjectScopedProactiveTopics(
  topics: ContinuityProactiveTopic[],
  projectId: string,
): ContinuityProactiveTopic[] {
  const normalizedProjectId = normalizeProjectId(projectId);
  if (!normalizedProjectId) {
    return [];
  }

  return topics.filter((topic) => normalizeProjectId(topic.projectId) === normalizedProjectId);
}
