import { useEffect, useRef } from 'react';
import { useDocumentVisibility } from './useDocumentVisibility';

type UseAdaptivePollingOptions = {
  enabled?: boolean;
  visibleIntervalMs: number;
  hiddenIntervalMs?: number;
  runImmediately?: boolean;
};

export function useAdaptivePolling(
  task: () => void | Promise<void>,
  {
    enabled = true,
    visibleIntervalMs,
    hiddenIntervalMs,
    runImmediately = true,
  }: UseAdaptivePollingOptions,
) {
  const isVisible = useDocumentVisibility();
  const taskRef = useRef(task);

  useEffect(() => {
    taskRef.current = task;
  }, [task]);

  useEffect(() => {
    if (!enabled) {
      return;
    }

    const runTask = () => {
      void taskRef.current();
    };

    if (runImmediately) {
      runTask();
    }

    const intervalMs = isVisible ? visibleIntervalMs : hiddenIntervalMs;
    if (!intervalMs || intervalMs <= 0) {
      return;
    }

    const intervalId = window.setInterval(runTask, intervalMs);
    return () => window.clearInterval(intervalId);
  }, [enabled, hiddenIntervalMs, isVisible, runImmediately, visibleIntervalMs]);

  return isVisible;
}
