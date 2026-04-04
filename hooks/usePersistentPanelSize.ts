import { useCallback, useEffect, useState } from 'react';

type PersistentPanelSizeOptions = {
  storageKey: string;
  defaultSize: number;
  minSize: number;
  maxSize: number;
};

const STORAGE_PREFIX = 'helper.panel-size.';

function clamp(value: number, minSize: number, maxSize: number) {
  return Math.min(maxSize, Math.max(minSize, value));
}

function readStoredSize(storageKey: string, fallback: number, minSize: number, maxSize: number) {
  if (typeof window === 'undefined') {
    return fallback;
  }

  const raw = window.localStorage.getItem(`${STORAGE_PREFIX}${storageKey}`);
  if (!raw) {
    return fallback;
  }

  const parsed = Number.parseFloat(raw);
  return Number.isFinite(parsed) ? clamp(parsed, minSize, maxSize) : fallback;
}

export function usePersistentPanelSize({
  storageKey,
  defaultSize,
  minSize,
  maxSize,
}: PersistentPanelSizeOptions) {
  const [size, setSize] = useState(() => readStoredSize(storageKey, defaultSize, minSize, maxSize));

  useEffect(() => {
    if (typeof window === 'undefined') {
      return;
    }

    window.localStorage.setItem(`${STORAGE_PREFIX}${storageKey}`, String(size));
  }, [size, storageKey]);

  const setClampedSize = useCallback((value: number) => {
    setSize(clamp(value, minSize, maxSize));
  }, [maxSize, minSize]);

  const resizeBy = useCallback((delta: number) => {
    setSize(current => clamp(current + delta, minSize, maxSize));
  }, [maxSize, minSize]);

  return {
    size,
    setSize: setClampedSize,
    resizeBy,
  };
}
