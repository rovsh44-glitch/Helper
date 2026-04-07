import { startTransition, useCallback, useEffect, useMemo, useState } from 'react';
import { useAdaptivePolling } from './useAdaptivePolling';
import {
  getControlPlaneSnapshot,
  getRuntimeLogsSnapshot,
  type ControlPlaneSnapshotDto,
  type RuntimeLogsSnapshotDto,
} from '../services/api/runtimeApi';
import { buildRouteTelemetryOverview } from '../services/runtimeTelemetry';
import {
  buildBootStages,
  buildDomainOptions,
  buildIntelligence,
  buildSeverityOptions,
  buildScopeOptions,
  deriveLogEntry,
  formatAge,
  mapRuntimeApiError,
  tokenizeSearch,
  type BootStage,
  type DerivedLogEntry,
  type DomainFilter,
  type ScopeFilter,
  type SeverityFilter,
} from '../utils/runtimeLogIntelligence';
import { readRouteQueryParam, updateRouteQueryParams } from '../services/appShellRoute';

export type RuntimeFeedItem = {
  id: string;
  kind: 'thought' | 'progress';
  text: string;
  timestamp: number;
  tag: string;
};

export function useRuntimeConsoleModel() {
  const [controlPlane, setControlPlane] = useState<ControlPlaneSnapshotDto | null>(null);
  const [logsSnapshot, setLogsSnapshot] = useState<RuntimeLogsSnapshotDto | null>(null);
  const [selectedSourceId, setSelectedSourceId] = useState(() => readRouteQueryParam('source') ?? '');
  const [severityFilter, setSeverityFilter] = useState<SeverityFilter>('all');
  const [scopeFilter, setScopeFilter] = useState<ScopeFilter>('all');
  const [domainFilter, setDomainFilter] = useState<DomainFilter>('all');
  const [textFilter, setTextFilter] = useState('');
  const [error, setError] = useState<string | null>(null);

  const refreshRuntime = useCallback(async () => {
    try {
      const [controlPlaneSnapshot, runtimeLogs] = await Promise.all([
        getControlPlaneSnapshot(),
        getRuntimeLogsSnapshot({ tail: 60, maxSources: 4 }),
      ]);

      setControlPlane(controlPlaneSnapshot);
      setLogsSnapshot(runtimeLogs);
      setError(null);
    } catch (runtimeError) {
      setError(mapRuntimeApiError(runtimeError));
    }
  }, []);

  useAdaptivePolling(refreshRuntime, {
    visibleIntervalMs: 4000,
    hiddenIntervalMs: 15000,
  });

  useEffect(() => {
    if (!logsSnapshot?.sources.length) {
      setSelectedSourceId('');
      return;
    }

    const selectedExists = logsSnapshot.sources.some(source => source.id === selectedSourceId);
    if (!selectedExists) {
      setSelectedSourceId(logsSnapshot.sources[0].id);
    }
  }, [logsSnapshot, selectedSourceId]);

  useEffect(() => {
    updateRouteQueryParams({
      source: selectedSourceId || null,
    }, { replace: true });
  }, [selectedSourceId]);

  const activeSource = useMemo(
    () => logsSnapshot?.sources.find(source => source.id === selectedSourceId) ?? logsSnapshot?.sources[0] ?? null,
    [logsSnapshot, selectedSourceId],
  );

  const sourceById = useMemo(
    () => new Map((logsSnapshot?.sources ?? []).map(source => [source.id, source])),
    [logsSnapshot],
  );

  const derivedEntries = useMemo<DerivedLogEntry[]>(
    () => (logsSnapshot?.entries ?? []).map(entry => deriveLogEntry(entry, sourceById.get(entry.sourceId))),
    [logsSnapshot, sourceById],
  );

  const sourceEntries = useMemo(() => {
    if (!activeSource) {
      return derivedEntries;
    }

    return derivedEntries.filter(entry => entry.raw.sourceId === activeSource.id);
  }, [activeSource, derivedEntries]);

  const searchTokens = useMemo(() => tokenizeSearch(textFilter), [textFilter]);

  const filteredEntries = useMemo(() => sourceEntries.filter(entry => {
    if (severityFilter !== 'all' && entry.severity !== severityFilter) {
      return false;
    }

    if (scopeFilter !== 'all' && entry.scope !== scopeFilter) {
      return false;
    }

    if (domainFilter !== 'all' && entry.domain !== domainFilter) {
      return false;
    }

    return searchTokens.length === 0 || searchTokens.every(token => entry.searchable.includes(token));
  }), [domainFilter, scopeFilter, searchTokens, severityFilter, sourceEntries]);

  const severityOptions = useMemo(() => buildSeverityOptions(sourceEntries), [sourceEntries]);
  const scopeOptions = useMemo(() => buildScopeOptions(sourceEntries), [sourceEntries]);
  const domainOptions = useMemo(() => buildDomainOptions(sourceEntries), [sourceEntries]);
  const filteredIntel = useMemo(() => buildIntelligence(filteredEntries), [filteredEntries]);
  const sourceIntel = useMemo(() => buildIntelligence(sourceEntries), [sourceEntries]);
  const bootStages = useMemo<BootStage[]>(() => buildBootStages(controlPlane), [controlPlane]);
  const telemetryOverview = useMemo(() => buildRouteTelemetryOverview(controlPlane), [controlPlane]);
  const semanticsCoverage = useMemo(() => {
    const total = derivedEntries.length;
    const structured = derivedEntries.filter(entry => entry.structured).length;
    return {
      total,
      structured,
      label: total > 0 ? `${structured}/${total}` : '0/0',
      tone: total === 0 ? 'neutral' as const : structured === total ? 'good' as const : 'warn' as const,
    };
  }, [derivedEntries]);
  const hasFilters = severityFilter !== 'all' || scopeFilter !== 'all' || domainFilter !== 'all' || textFilter.trim().length > 0;

  const resetFilters = useCallback(() => {
    setSeverityFilter('all');
    setScopeFilter('all');
    setDomainFilter('all');
    setTextFilter('');
  }, []);

  return {
    activeSource,
    bootStages,
    controlPlane,
    derivedEntries,
    domainFilter,
    domainOptions,
    error,
    filteredEntries,
    filteredIntel,
    hasFilters,
    logsSnapshot,
    refreshRuntime,
    resetFilters,
    scopeFilter,
    scopeOptions,
    searchTokens,
    selectedSourceId,
    semanticsCoverage,
    setDomainFilter: (value: DomainFilter) => startTransition(() => setDomainFilter(value)),
    setScopeFilter: (value: ScopeFilter) => startTransition(() => setScopeFilter(value)),
    setSelectedSourceId: (value: string) => startTransition(() => setSelectedSourceId(value)),
    setSeverityFilter: (value: SeverityFilter) => startTransition(() => setSeverityFilter(value)),
    setTextFilter,
    severityFilter,
    severityOptions,
    sourceEntries,
    sourceIntel,
    telemetryOverview,
    textFilter,
    updatedLabel: formatAge(logsSnapshot?.generatedAtUtc ?? controlPlane?.readiness.lastTransitionUtc),
  };
}

export type RuntimeConsoleModel = ReturnType<typeof useRuntimeConsoleModel>;
