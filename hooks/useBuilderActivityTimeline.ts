import { useCallback, useState } from 'react';
import type { BuilderActivityEntry } from '../types';

const MAX_ACTIVITY_ENTRIES = 40;

type BuilderActivityInput = Omit<BuilderActivityEntry, 'id' | 'timestamp'>;

export function useBuilderActivityTimeline() {
  const [activityEntries, setActivityEntries] = useState<BuilderActivityEntry[]>([]);

  const recordActivity = useCallback((entry: BuilderActivityInput) => {
    setActivityEntries(previous => [
      {
        id: crypto.randomUUID(),
        timestamp: Date.now(),
        ...entry,
      },
      ...previous,
    ].slice(0, MAX_ACTIVITY_ENTRIES));
  }, []);

  const clearActivity = useCallback(() => {
    setActivityEntries([]);
  }, []);

  return {
    activityEntries,
    recordActivity,
    clearActivity,
  };
}
