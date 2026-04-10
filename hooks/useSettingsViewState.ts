import { useEffect, useMemo, useState } from 'react';
import type { ConversationMemoryItemDto } from '../services/conversationApi';
import {
  DEFAULT_ANSWER_SHAPE_KEY,
  DIRECTNESS_KEY,
  ENTHUSIASM_KEY,
  PREFERRED_LANGUAGE_KEY,
  RESPONSE_STYLE_KEY,
  WARMTH_KEY,
} from '../services/conversationSession';
import {
  cancelConversationBackgroundTask,
  deleteConversationMemoryEntry,
  getConversationMemorySnapshot,
  getConversationSnapshot,
  setConversationProactiveTopicEnabled,
  setConversationPreferences,
} from '../services/conversationApi';
import {
  filterProjectScopedBackgroundTasks,
  filterProjectScopedProactiveTopics,
} from '../services/projectContextContinuityScope';
import { useCapabilityCatalog } from './useCapabilityCatalog';
import { useControlPlaneTelemetry } from './useControlPlaneTelemetry';
import { useHumanLikeConversationDashboard } from './useHumanLikeConversationDashboard';
import { buildRouteTelemetryOverview } from '../services/runtimeTelemetry';
import { navigateToTab } from '../services/appShellRoute';
import { buildConversationStylePreview } from '../utils/conversationStylePreview';
import { readActiveConversationId, readStoredPreference, writeStoredPreference } from '../services/settingsPreferenceStorage';
import type { SettingsAlertItem } from '../components/settings/SettingsAlertsPanel';
import type { ContinuityBackgroundTask, ContinuityProactiveTopic } from '../services/settingsContinuityContracts';

type ConversationPreferenceOverride = Partial<Parameters<typeof setConversationPreferences>[1]>;
type ConversationSnapshot = Awaited<ReturnType<typeof getConversationSnapshot>>;

function normalizeClearableTextValue(value: string | null | undefined): string | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  if (value === null) {
    return null;
  }

  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

export function useSettingsViewState() {
  const [actionStatus, setActionStatus] = useState<string | null>(null);
  const [responseStyle, setResponseStyle] = useState(() => readStoredPreference(RESPONSE_STYLE_KEY, 'balanced'));
  const [memoryEnabled, setMemoryEnabled] = useState(true);
  const [personalConsent, setPersonalConsent] = useState(false);
  const [preferredLanguage, setPreferredLanguage] = useState(() => readStoredPreference(PREFERRED_LANGUAGE_KEY, 'auto'));
  const [warmth, setWarmth] = useState(() => readStoredPreference(WARMTH_KEY, 'balanced'));
  const [enthusiasm, setEnthusiasm] = useState(() => readStoredPreference(ENTHUSIASM_KEY, 'balanced'));
  const [directness, setDirectness] = useState(() => readStoredPreference(DIRECTNESS_KEY, 'balanced'));
  const [defaultAnswerShape, setDefaultAnswerShape] = useState(() => readStoredPreference(DEFAULT_ANSWER_SHAPE_KEY, 'auto'));
  const [decisionAssertiveness, setDecisionAssertiveness] = useState('balanced');
  const [clarificationTolerance, setClarificationTolerance] = useState('balanced');
  const [citationPreference, setCitationPreference] = useState('adaptive');
  const [repairStyle, setRepairStyle] = useState('direct_fix');
  const [reasoningStyle, setReasoningStyle] = useState('concise');
  const [reasoningEffort, setReasoningEffort] = useState('balanced');
  const [projectId, setProjectId] = useState('');
  const [projectLabel, setProjectLabel] = useState('');
  const [projectInstructions, setProjectInstructions] = useState('');
  const [projectMemoryEnabled, setProjectMemoryEnabled] = useState(true);
  const [backgroundResearchEnabled, setBackgroundResearchEnabled] = useState(true);
  const [proactiveUpdatesEnabled, setProactiveUpdatesEnabled] = useState(false);
  const [projectReferenceArtifacts, setProjectReferenceArtifacts] = useState<string[]>([]);
  const [backgroundTasks, setBackgroundTasks] = useState<ContinuityBackgroundTask[]>([]);
  const [proactiveTopics, setProactiveTopics] = useState<ContinuityProactiveTopic[]>([]);
  const [sessionTtlMinutes, setSessionTtlMinutes] = useState(720);
  const [taskTtlHours, setTaskTtlHours] = useState(336);
  const [longTermTtlDays, setLongTermTtlDays] = useState(180);
  const [memoryItems, setMemoryItems] = useState<ConversationMemoryItemDto[]>([]);
  const [memoryError, setMemoryError] = useState<string | null>(null);
  const [memoryStatus, setMemoryStatus] = useState<string>('Idle');
  const [isLoadingMemory, setIsLoadingMemory] = useState(false);
  const [isSavingPreferences, setIsSavingPreferences] = useState(false);
  const [isDeletingMemory, setIsDeletingMemory] = useState<string | null>(null);
  const {
    controlPlane,
    error: runtimeError,
    isRefreshing: isRefreshingRuntime,
  } = useControlPlaneTelemetry();
  const {
    snapshot: capabilityCatalog,
    error: capabilityError,
    isRefreshing: isRefreshingCapabilityCatalog,
  } = useCapabilityCatalog();
  const {
    snapshot: conversationQualityDashboard,
    error: conversationQualityError,
    isRefreshing: isRefreshingConversationQuality,
  } = useHumanLikeConversationDashboard();

  const conversationId = readActiveConversationId();
  const status = runtimeError ? 'API unavailable' : controlPlane ? 'API reachable' : 'Checking...';
  const routeTelemetry = useMemo(() => buildRouteTelemetryOverview(controlPlane), [controlPlane]);
  const stylePreview = useMemo(() => buildConversationStylePreview({
    responseStyle,
    preferredLanguage,
    warmth,
    enthusiasm,
    directness,
    defaultAnswerShape,
  }), [defaultAnswerShape, directness, enthusiasm, preferredLanguage, responseStyle, warmth]);

  const resetConversationScopedState = (memoryStatusMessage: string) => {
    setMemoryEnabled(true);
    setPersonalConsent(false);
    setDecisionAssertiveness('balanced');
    setClarificationTolerance('balanced');
    setCitationPreference('adaptive');
    setRepairStyle('direct_fix');
    setReasoningStyle('concise');
    setReasoningEffort('balanced');
    setProjectId('');
    setProjectLabel('');
    setProjectInstructions('');
    setProjectMemoryEnabled(true);
    setBackgroundResearchEnabled(true);
    setProactiveUpdatesEnabled(false);
    setProjectReferenceArtifacts([]);
    setBackgroundTasks([]);
    setProactiveTopics([]);
    setSessionTtlMinutes(720);
    setTaskTtlHours(336);
    setLongTermTtlDays(180);
    setMemoryItems([]);
    setMemoryError(null);
    setMemoryStatus(memoryStatusMessage);
  };

  const applyConversationSnapshot = (data: ConversationSnapshot) => {
    if (!data.preferences) {
      return;
    }

    setMemoryEnabled(!!data.preferences.longTermMemoryEnabled);
    setPersonalConsent(!!data.preferences.personalMemoryConsentGranted);
    if (data.preferences.preferredLanguage) {
      setPreferredLanguage(data.preferences.preferredLanguage);
      writeStoredPreference(PREFERRED_LANGUAGE_KEY, data.preferences.preferredLanguage);
    }
    if (data.preferences.detailLevel) {
      const detailLevel = data.preferences.detailLevel === 'deep' ? 'detailed' : data.preferences.detailLevel;
      setResponseStyle(detailLevel);
      writeStoredPreference(RESPONSE_STYLE_KEY, detailLevel);
    }
    if (data.preferences.warmth) {
      setWarmth(data.preferences.warmth);
      writeStoredPreference(WARMTH_KEY, data.preferences.warmth);
    }
    if (data.preferences.enthusiasm) {
      setEnthusiasm(data.preferences.enthusiasm);
      writeStoredPreference(ENTHUSIASM_KEY, data.preferences.enthusiasm);
    }
    if (data.preferences.directness) {
      setDirectness(data.preferences.directness);
      writeStoredPreference(DIRECTNESS_KEY, data.preferences.directness);
    }
    if (data.preferences.defaultAnswerShape) {
      setDefaultAnswerShape(data.preferences.defaultAnswerShape);
      writeStoredPreference(DEFAULT_ANSWER_SHAPE_KEY, data.preferences.defaultAnswerShape);
    }

    setDecisionAssertiveness(data.preferences.decisionAssertiveness ?? 'balanced');
    setClarificationTolerance(data.preferences.clarificationTolerance ?? 'balanced');
    setCitationPreference(data.preferences.citationPreference ?? 'adaptive');
    setRepairStyle(data.preferences.repairStyle ?? 'direct_fix');
    setReasoningStyle(data.preferences.reasoningStyle ?? 'concise');
    setReasoningEffort(data.preferences.reasoningEffort ?? 'balanced');
    setProjectId(typeof data.preferences.projectId === 'string' ? data.preferences.projectId : '');
    setProjectLabel(typeof data.preferences.projectLabel === 'string' ? data.preferences.projectLabel : '');
    setProjectInstructions(typeof data.preferences.projectInstructions === 'string' ? data.preferences.projectInstructions : '');
    setProjectMemoryEnabled(typeof data.preferences.projectMemoryEnabled === 'boolean' ? data.preferences.projectMemoryEnabled : true);
    setBackgroundResearchEnabled(typeof data.preferences.backgroundResearchEnabled === 'boolean' ? data.preferences.backgroundResearchEnabled : true);
    setProactiveUpdatesEnabled(typeof data.preferences.proactiveUpdatesEnabled === 'boolean' ? data.preferences.proactiveUpdatesEnabled : false);
    setProjectReferenceArtifacts(data.projectContext?.referenceArtifacts ?? []);
    setBackgroundTasks(data.backgroundTasks ?? []);
    setProactiveTopics(data.proactiveTopics ?? []);
    setSessionTtlMinutes(typeof data.preferences.sessionMemoryTtlMinutes === 'number' ? data.preferences.sessionMemoryTtlMinutes : 720);
    setTaskTtlHours(typeof data.preferences.taskMemoryTtlHours === 'number' ? data.preferences.taskMemoryTtlHours : 336);
    setLongTermTtlDays(typeof data.preferences.longTermMemoryTtlDays === 'number' ? data.preferences.longTermMemoryTtlDays : 180);
  };

  useEffect(() => {
    if (!actionStatus) {
      return;
    }

    const timeoutId = window.setTimeout(() => setActionStatus(null), 2500);
    return () => window.clearTimeout(timeoutId);
  }, [actionStatus]);

  const reloadMemory = async () => {
    if (!conversationId) {
      resetConversationScopedState('No active conversation. Memory preferences will apply after chat starts.');
      return;
    }

    setIsLoadingMemory(true);
    try {
      const snapshot = await getConversationMemorySnapshot(conversationId);
      setMemoryItems(snapshot.items);
      setMemoryEnabled(snapshot.policy.longTermMemoryEnabled);
      setPersonalConsent(snapshot.policy.personalMemoryConsentGranted);
      setSessionTtlMinutes(snapshot.policy.sessionMemoryTtlMinutes);
      setTaskTtlHours(snapshot.policy.taskMemoryTtlHours);
      setLongTermTtlDays(snapshot.policy.longTermMemoryTtlDays);
      setMemoryError(null);
      setMemoryStatus(`Memory snapshot refreshed at ${new Date().toLocaleTimeString()}.`);
    } catch (error) {
      setMemoryError(mapSettingsApiError(error));
      setMemoryStatus('Memory snapshot unavailable.');
    } finally {
      setIsLoadingMemory(false);
    }
  };

  useEffect(() => {
    let isMounted = true;

    const hydrateConversationPreferences = async () => {
      if (!conversationId) {
        resetConversationScopedState('No active conversation. Start a chat to sync memory controls.');
        return;
      }

      try {
        const data = await getConversationSnapshot(conversationId);
        if (!isMounted || !data.preferences) {
          return;
        }

        resetConversationScopedState('Memory snapshot refreshed.');
        applyConversationSnapshot(data);
      } catch (error) {
        if (!isMounted) {
          return;
        }

        setMemoryError(mapSettingsApiError(error));
      }

      if (isMounted) {
        await reloadMemory();
      }
    };

    void hydrateConversationPreferences();

    return () => {
      isMounted = false;
    };
  }, [conversationId]);

  const savePreferences = async (override?: ConversationPreferenceOverride) => {
    if (!conversationId) {
      setMemoryError('Start a conversation before persisting memory preferences.');
      setMemoryStatus('Preference save skipped.');
      return;
    }

    setIsSavingPreferences(true);
    try {
      const requestBody: ConversationPreferenceOverride = {
        longTermMemoryEnabled: override?.longTermMemoryEnabled ?? memoryEnabled,
        preferredLanguage: override?.preferredLanguage ?? preferredLanguage,
        detailLevel: override?.detailLevel ?? responseStyle,
        warmth: override?.warmth ?? warmth,
        enthusiasm: override?.enthusiasm ?? enthusiasm,
        directness: override?.directness ?? directness,
        defaultAnswerShape: override?.defaultAnswerShape ?? defaultAnswerShape,
        decisionAssertiveness: override?.decisionAssertiveness ?? decisionAssertiveness,
        clarificationTolerance: override?.clarificationTolerance ?? clarificationTolerance,
        citationPreference: override?.citationPreference ?? citationPreference,
        repairStyle: override?.repairStyle ?? repairStyle,
        reasoningStyle: override?.reasoningStyle ?? reasoningStyle,
        reasoningEffort: override?.reasoningEffort ?? reasoningEffort,
        personalMemoryConsentGranted: override?.personalMemoryConsentGranted ?? personalConsent,
        sessionMemoryTtlMinutes: override?.sessionMemoryTtlMinutes ?? sessionTtlMinutes,
        taskMemoryTtlHours: override?.taskMemoryTtlHours ?? taskTtlHours,
        longTermMemoryTtlDays: override?.longTermMemoryTtlDays ?? longTermTtlDays,
      };

      if (override && 'searchLocalityHint' in override) {
        requestBody.searchLocalityHint = normalizeClearableTextValue(override.searchLocalityHint);
      }

      if (override && ('projectId' in override || 'projectLabel' in override || 'projectInstructions' in override || 'projectMemoryEnabled' in override)) {
        requestBody.projectId = normalizeClearableTextValue(override.projectId);
        requestBody.projectLabel = normalizeClearableTextValue(override.projectLabel);
        requestBody.projectInstructions = normalizeClearableTextValue(override.projectInstructions);
        requestBody.projectMemoryEnabled = override.projectMemoryEnabled ?? projectMemoryEnabled;
      }

      if (override && ('backgroundResearchEnabled' in override || 'proactiveUpdatesEnabled' in override)) {
        requestBody.backgroundResearchEnabled = override.backgroundResearchEnabled ?? backgroundResearchEnabled;
        requestBody.proactiveUpdatesEnabled = override.proactiveUpdatesEnabled ?? proactiveUpdatesEnabled;
      }

      await setConversationPreferences(conversationId, requestBody);
      applyConversationSnapshot(await getConversationSnapshot(conversationId));
      await reloadMemory();
      setMemoryError(null);
      setMemoryStatus(`Preferences saved at ${new Date().toLocaleTimeString()}.`);
    } catch (error) {
      setMemoryError(mapSettingsApiError(error));
      setMemoryStatus('Preference save failed.');
    } finally {
      setIsSavingPreferences(false);
    }
  };

  const saveStyle = (style: string) => {
    setResponseStyle(style);
    writeStoredPreference(RESPONSE_STYLE_KEY, style);
    void savePreferences({ detailLevel: style });
  };

  const saveLanguage = (language: string) => {
    setPreferredLanguage(language);
    writeStoredPreference(PREFERRED_LANGUAGE_KEY, language);
    void savePreferences({ preferredLanguage: language });
  };

  const saveWarmthPreference = (value: string) => {
    setWarmth(value);
    writeStoredPreference(WARMTH_KEY, value);
    void savePreferences({ warmth: value });
  };

  const saveEnthusiasmPreference = (value: string) => {
    setEnthusiasm(value);
    writeStoredPreference(ENTHUSIASM_KEY, value);
    void savePreferences({ enthusiasm: value });
  };

  const saveDirectnessPreference = (value: string) => {
    setDirectness(value);
    writeStoredPreference(DIRECTNESS_KEY, value);
    void savePreferences({ directness: value });
  };

  const saveDefaultAnswerShapePreference = (value: string) => {
    setDefaultAnswerShape(value);
    writeStoredPreference(DEFAULT_ANSWER_SHAPE_KEY, value);
    void savePreferences({ defaultAnswerShape: value });
  };

  const saveDecisionAssertiveness = (value: string) => {
    setDecisionAssertiveness(value);
    void savePreferences({ decisionAssertiveness: value });
  };

  const saveClarificationTolerance = (value: string) => {
    setClarificationTolerance(value);
    void savePreferences({ clarificationTolerance: value });
  };

  const saveCitationPreference = (value: string) => {
    setCitationPreference(value);
    void savePreferences({ citationPreference: value });
  };

  const saveRepairStyle = (value: string) => {
    setRepairStyle(value);
    void savePreferences({ repairStyle: value });
  };

  const saveReasoningStyle = (value: string) => {
    setReasoningStyle(value);
    void savePreferences({ reasoningStyle: value });
  };

  const saveReasoningEffort = (value: string) => {
    setReasoningEffort(value);
    void savePreferences({ reasoningEffort: value });
  };

  const saveProjectContext = (override?: ConversationPreferenceOverride) => {
    void savePreferences({
      projectId: normalizeClearableTextValue(override?.projectId ?? projectId),
      projectLabel: normalizeClearableTextValue(override?.projectLabel ?? projectLabel),
      projectInstructions: normalizeClearableTextValue(override?.projectInstructions ?? projectInstructions),
      projectMemoryEnabled: override?.projectMemoryEnabled ?? projectMemoryEnabled,
    });
  };

  const saveContinuityControls = (override?: ConversationPreferenceOverride) => {
    void savePreferences({
      backgroundResearchEnabled: override?.backgroundResearchEnabled ?? backgroundResearchEnabled,
      proactiveUpdatesEnabled: override?.proactiveUpdatesEnabled ?? proactiveUpdatesEnabled,
    });
  };

  const deleteMemoryItem = async (memoryId: string) => {
    if (!conversationId) {
      setMemoryError('Start a conversation before deleting memory items.');
      return;
    }

    setIsDeletingMemory(memoryId);
    try {
      await deleteConversationMemoryEntry(conversationId, memoryId);
      await reloadMemory();
      setMemoryError(null);
      setMemoryStatus('Memory item deleted.');
    } catch (error) {
      setMemoryError(mapSettingsApiError(error));
      setMemoryStatus('Memory delete failed.');
    } finally {
      setIsDeletingMemory(null);
    }
  };

  const cancelBackgroundTask = async (taskId: string) => {
    if (!conversationId) {
      setActionStatus('Start a conversation before canceling background work.');
      return;
    }

    try {
      await cancelConversationBackgroundTask(conversationId, taskId, 'Canceled from settings project context.');
      applyConversationSnapshot(await getConversationSnapshot(conversationId));
      setActionStatus('Background task canceled.');
    } catch (error) {
      setActionStatus(mapSettingsApiError(error));
    }
  };

  const setProactiveTopicEnabled = async (topicId: string, enabled: boolean) => {
    if (!conversationId) {
      setActionStatus('Start a conversation before editing proactive topics.');
      return;
    }

    try {
      await setConversationProactiveTopicEnabled(conversationId, topicId, enabled);
      applyConversationSnapshot(await getConversationSnapshot(conversationId));
      setActionStatus(enabled ? 'Proactive topic enabled.' : 'Proactive topic disabled.');
    } catch (error) {
      setActionStatus(mapSettingsApiError(error));
    }
  };

  const projectScopedBackgroundTasks = useMemo(
    () => filterProjectScopedBackgroundTasks(backgroundTasks, projectId),
    [backgroundTasks, projectId],
  );
  const projectScopedProactiveTopics = useMemo(
    () => filterProjectScopedProactiveTopics(proactiveTopics, projectId),
    [projectId, proactiveTopics],
  );


  const infrastructureCards = useMemo(() => {
    if (!controlPlane) {
      return [
        {
          label: 'Control Plane',
          value: 'Unavailable',
          note: runtimeError || 'Runtime snapshot has not been loaded yet.',
        },
      ];
    }

    return [
      {
        label: 'Active model',
        value: controlPlane.modelGateway.currentModel || 'Not reported',
        note: `${controlPlane.modelGateway.availableModels.length} model(s) visible to the gateway${controlPlane.modelGateway.activeProfileId ? ` · profile ${controlPlane.modelGateway.activeProfileId}` : ''}`,
      },
      {
        label: 'Readiness',
        value: controlPlane.readiness.readyForChat ? 'Ready for chat' : controlPlane.readiness.status,
        note: `${controlPlane.readiness.phase} / ${controlPlane.readiness.lifecycleState}`,
      },
      {
        label: 'Warmup policy',
        value: controlPlane.readiness.warmupMode || 'Unknown',
        note: controlPlane.readiness.lastTransitionUtc
          ? `Last transition ${new Date(controlPlane.readiness.lastTransitionUtc).toLocaleString()}`
          : 'No readiness transition timestamp reported',
      },
      {
        label: 'Route telemetry',
        value: routeTelemetry.totalEvents > 0 ? `${routeTelemetry.totalEvents} events` : 'Idle',
        note: routeTelemetry.totalEvents > 0
          ? `${routeTelemetry.dominantOperationKind} via ${routeTelemetry.dominantChannel}`
          : 'No route telemetry has been recorded yet.',
      },
      {
        label: 'Route quality',
        value: routeTelemetry.dominantQuality,
        note: `${routeTelemetry.degradedCount} degraded · ${routeTelemetry.failedCount} failed · ${routeTelemetry.blockedCount} blocked`,
      },
      {
        label: 'Configuration',
        value: controlPlane.configuration.isValid ? 'Valid' : 'Degraded',
        note: controlPlane.configuration.alerts[0] || routeTelemetry.alerts[0] || 'No active configuration alerts',
      },
    ];
  }, [controlPlane, routeTelemetry, runtimeError]);

  const navigateToRuntimeConsole = () => {
    navigateToTab('runtime');
  };

  const navigateToHelperCore = () => {
    navigateToTab('orchestrator');
  };

  const focusSettingsSection = (sectionId: string) => {
    const target = document.getElementById(sectionId);
    if (!target) {
      setActionStatus(`Section ${sectionId} is unavailable.`);
      return;
    }

    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
    window.history.replaceState({}, '', `${window.location.pathname}${window.location.search}#${sectionId}`);
    setActionStatus(`Focused ${sectionId.replace('settings-', '').replace(/-/g, ' ')}.`);
  };

  const governanceSnapshot = useMemo(() => ({
    exportedAtUtc: new Date().toISOString(),
    conversationId,
    runtimeStatus: status,
    routeTelemetry,
    controlPlaneReadiness: controlPlane?.readiness ?? null,
    capabilityCatalog,
    conversationQualityDashboard,
    memoryStatus,
    memoryPolicy: {
      memoryEnabled,
      personalConsent,
      sessionTtlMinutes,
      taskTtlHours,
      longTermTtlDays,
    },
    stylePreferences: {
      responseStyle,
      preferredLanguage,
      warmth,
      enthusiasm,
      directness,
      defaultAnswerShape,
    },
    personalization: {
      decisionAssertiveness,
      clarificationTolerance,
      citationPreference,
      repairStyle,
      reasoningStyle,
      reasoningEffort,
    },
    projectContext: {
      projectId: projectId.trim() || null,
      projectLabel: projectLabel.trim() || null,
      projectInstructions: projectInstructions.trim() || null,
      projectMemoryEnabled,
      referenceArtifacts: projectReferenceArtifacts,
    },
    continuity: {
      backgroundResearchEnabled,
      proactiveUpdatesEnabled,
      backgroundTasks: projectScopedBackgroundTasks,
      proactiveTopics: projectScopedProactiveTopics,
    },
    memoryItems: memoryItems.slice(0, 20).map(item => ({
      id: item.id,
      type: item.type,
      scope: item.scope,
      retention: item.retention,
      whyRemembered: item.whyRemembered,
      priority: item.priority,
      isPersonal: item.isPersonal,
      createdAt: item.createdAt,
      expiresAt: item.expiresAt,
      content: item.content,
      sourceProjectId: item.sourceProjectId,
    })),
  }), [
    capabilityCatalog,
    citationPreference,
    clarificationTolerance,
    controlPlane?.readiness,
    conversationId,
    conversationQualityDashboard,
    decisionAssertiveness,
    defaultAnswerShape,
    directness,
    enthusiasm,
    longTermTtlDays,
    memoryEnabled,
    memoryItems,
    memoryStatus,
    personalConsent,
    preferredLanguage,
    projectId,
    projectInstructions,
    projectLabel,
    projectMemoryEnabled,
    projectReferenceArtifacts,
    backgroundResearchEnabled,
    proactiveUpdatesEnabled,
    projectScopedBackgroundTasks,
    projectScopedProactiveTopics,
    reasoningEffort,
    reasoningStyle,
    repairStyle,
    responseStyle,
    routeTelemetry,
    sessionTtlMinutes,
    status,
    taskTtlHours,
    warmth,
  ]);

  const copyGovernanceSnapshot = async () => {
    try {
      if (!navigator.clipboard?.writeText) {
        throw new Error('Clipboard API unavailable');
      }

      await navigator.clipboard.writeText(JSON.stringify(governanceSnapshot, null, 2));
      setActionStatus('Governance snapshot copied.');
    } catch {
      setActionStatus('Governance snapshot copy failed.');
    }
  };

  const exportGovernanceSnapshot = () => {
    try {
      const payload = JSON.stringify(governanceSnapshot, null, 2);
      const blob = new Blob([payload], { type: 'application/json;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `helper-settings-governance-${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
      setActionStatus('Governance snapshot exported.');
    } catch {
      setActionStatus('Governance snapshot export failed.');
    }
  };

  const settingsAlerts = useMemo<SettingsAlertItem[]>(() => {
    const items: SettingsAlertItem[] = [];

    if (runtimeError) {
      items.push({
        id: 'runtime-error',
        title: 'Runtime',
        message: runtimeError,
        tone: 'critical',
        actionLabel: 'Open Runtime',
        onAction: navigateToRuntimeConsole,
      });
    }

    if (capabilityError) {
      items.push({
        id: 'capability-error',
        title: 'Capability Coverage',
        message: capabilityError,
        tone: 'warning',
        actionLabel: 'Open Coverage',
        onAction: () => focusSettingsSection('settings-capability-coverage'),
      });
    }

    if (conversationQualityError) {
      items.push({
        id: 'conversation-quality-error',
        title: 'Conversation Quality',
        message: conversationQualityError,
        tone: 'warning',
        actionLabel: 'Open Quality',
        onAction: () => focusSettingsSection('settings-conversation-quality'),
      });
    }

    if (memoryError) {
      items.push({
        id: 'memory-error',
        title: 'Conversation Memory',
        message: memoryError,
        tone: 'critical',
        actionLabel: 'Open Memory',
        onAction: () => focusSettingsSection('settings-memory-policy'),
      });
    }

    routeTelemetry.alerts.slice(0, 2).forEach((alert, index) => {
      items.push({
        id: `telemetry-alert-${index}`,
        title: 'Route Telemetry',
        message: alert,
        tone: 'info',
        actionLabel: 'Open Infrastructure',
        onAction: () => focusSettingsSection('settings-infrastructure'),
      });
    });

    conversationQualityDashboard?.alerts.slice(0, 2).forEach((alert, index) => {
      items.push({
        id: `quality-alert-${index}`,
        title: 'Conversation Quality Alert',
        message: alert,
        tone: 'info',
        actionLabel: 'Open Quality',
        onAction: () => focusSettingsSection('settings-conversation-quality'),
      });
    });

    return items;
  }, [
    capabilityError,
    conversationQualityDashboard?.alerts,
    conversationQualityError,
    memoryError,
    routeTelemetry.alerts,
    runtimeError,
  ]);

  return {
    actionStatus,
    copyGovernanceSnapshot,
    exportGovernanceSnapshot,
    focusSettingsSection,
    responseStyle,
    memoryEnabled,
    personalConsent,
    preferredLanguage,
    warmth,
    enthusiasm,
    directness,
    defaultAnswerShape,
    decisionAssertiveness,
    clarificationTolerance,
    citationPreference,
    repairStyle,
    reasoningStyle,
    reasoningEffort,
    projectId,
    projectLabel,
    projectInstructions,
    projectMemoryEnabled,
    backgroundResearchEnabled,
    proactiveUpdatesEnabled,
    projectReferenceArtifacts,
    projectScopedBackgroundTasks,
    projectScopedProactiveTopics,
    sessionTtlMinutes,
    taskTtlHours,
    longTermTtlDays,
    memoryItems,
    memoryError,
    memoryStatus,
    isLoadingMemory,
    isSavingPreferences,
    isDeletingMemory,
    controlPlane,
    runtimeError,
    isRefreshingRuntime,
    capabilityCatalog,
    capabilityError,
    isRefreshingCapabilityCatalog,
    conversationQualityDashboard,
    conversationQualityError,
    isRefreshingConversationQuality,
    status,
    routeTelemetry,
    stylePreview,
    infrastructureCards,
    settingsAlerts,
    navigateToRuntimeConsole,
    navigateToHelperCore,
    saveStyle,
    saveLanguage,
    saveWarmthPreference,
    saveEnthusiasmPreference,
    saveDirectnessPreference,
    saveDefaultAnswerShapePreference,
    saveDecisionAssertiveness,
    saveClarificationTolerance,
    saveCitationPreference,
    saveRepairStyle,
    saveReasoningStyle,
    saveReasoningEffort,
    saveProjectContext,
    saveContinuityControls,
    setMemoryEnabled,
    setPersonalConsent,
    setProjectId,
    setProjectLabel,
    setProjectInstructions,
    setProjectMemoryEnabled,
    setBackgroundResearchEnabled,
    setProactiveUpdatesEnabled,
    setSessionTtlMinutes,
    setTaskTtlHours,
    setLongTermTtlDays,
    savePreferences,
    deleteMemoryItem,
    cancelBackgroundTask,
    setProactiveTopicEnabled,
  };
}

function mapSettingsApiError(error: unknown): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return 'The backend settings surface did not return a usable response.';
}
