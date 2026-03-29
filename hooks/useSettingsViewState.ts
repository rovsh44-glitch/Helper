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
  deleteConversationMemoryEntry,
  getConversationMemorySnapshot,
  getConversationSnapshot,
  setConversationPreferences,
} from '../services/conversationApi';
import { useCapabilityCatalog } from './useCapabilityCatalog';
import { useControlPlaneTelemetry } from './useControlPlaneTelemetry';
import { useHumanLikeConversationDashboard } from './useHumanLikeConversationDashboard';
import { buildRouteTelemetryOverview } from '../services/runtimeTelemetry';
import { buildConversationStylePreview } from '../utils/conversationStylePreview';
import { readActiveConversationId, readStoredPreference, writeStoredPreference } from '../services/settingsPreferenceStorage';

type ConversationPreferenceOverride = Partial<Parameters<typeof setConversationPreferences>[1]>;

export function useSettingsViewState() {
  const [responseStyle, setResponseStyle] = useState(() => readStoredPreference(RESPONSE_STYLE_KEY, 'balanced'));
  const [memoryEnabled, setMemoryEnabled] = useState(true);
  const [personalConsent, setPersonalConsent] = useState(false);
  const [preferredLanguage, setPreferredLanguage] = useState(() => readStoredPreference(PREFERRED_LANGUAGE_KEY, 'auto'));
  const [warmth, setWarmth] = useState(() => readStoredPreference(WARMTH_KEY, 'balanced'));
  const [enthusiasm, setEnthusiasm] = useState(() => readStoredPreference(ENTHUSIASM_KEY, 'balanced'));
  const [directness, setDirectness] = useState(() => readStoredPreference(DIRECTNESS_KEY, 'balanced'));
  const [defaultAnswerShape, setDefaultAnswerShape] = useState(() => readStoredPreference(DEFAULT_ANSWER_SHAPE_KEY, 'auto'));
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

  const reloadMemory = async () => {
    if (!conversationId) {
      setMemoryItems([]);
      setMemoryStatus('No active conversation. Memory preferences will apply after chat starts.');
      setMemoryError(null);
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
        setMemoryItems([]);
        setMemoryError(null);
        setMemoryStatus('No active conversation. Start a chat to sync memory controls.');
        return;
      }

      try {
        const data = await getConversationSnapshot(conversationId);
        if (!isMounted || !data.preferences) {
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
        if (data.preferences.sessionMemoryTtlMinutes) {
          setSessionTtlMinutes(data.preferences.sessionMemoryTtlMinutes);
        }
        if (data.preferences.taskMemoryTtlHours) {
          setTaskTtlHours(data.preferences.taskMemoryTtlHours);
        }
        if (data.preferences.longTermMemoryTtlDays) {
          setLongTermTtlDays(data.preferences.longTermMemoryTtlDays);
        }
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
      await setConversationPreferences(conversationId, {
        longTermMemoryEnabled: override?.longTermMemoryEnabled ?? memoryEnabled,
        preferredLanguage: override?.preferredLanguage ?? preferredLanguage,
        detailLevel: override?.detailLevel ?? responseStyle,
        warmth: override?.warmth ?? warmth,
        enthusiasm: override?.enthusiasm ?? enthusiasm,
        directness: override?.directness ?? directness,
        defaultAnswerShape: override?.defaultAnswerShape ?? defaultAnswerShape,
        personalMemoryConsentGranted: override?.personalMemoryConsentGranted ?? personalConsent,
        sessionMemoryTtlMinutes: override?.sessionMemoryTtlMinutes ?? sessionTtlMinutes,
        taskMemoryTtlHours: override?.taskMemoryTtlHours ?? taskTtlHours,
        longTermMemoryTtlDays: override?.longTermMemoryTtlDays ?? longTermTtlDays,
      });
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
        note: `${controlPlane.modelGateway.availableModels.length} model(s) visible to the gateway`,
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

  return {
    responseStyle,
    memoryEnabled,
    personalConsent,
    preferredLanguage,
    warmth,
    enthusiasm,
    directness,
    defaultAnswerShape,
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
    saveStyle,
    saveLanguage,
    saveWarmthPreference,
    saveEnthusiasmPreference,
    saveDirectnessPreference,
    saveDefaultAnswerShapePreference,
    setMemoryEnabled,
    setPersonalConsent,
    setSessionTtlMinutes,
    setTaskTtlHours,
    setLongTermTtlDays,
    savePreferences,
    deleteMemoryItem,
  };
}

function mapSettingsApiError(error: unknown): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return 'The backend settings surface did not return a usable response.';
}
