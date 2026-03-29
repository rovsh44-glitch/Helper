import { useCallback, useEffect } from 'react';
import { useConversationActions, useConversationRuntimeState, useConversationShellState } from '../contexts/ConversationStateContext';
import {
  clearPendingTurn,
  clearStoredConversationId,
  getSavedResponseStyle,
  readPendingTurn,
  readPreferredLanguage,
  readSavedStylePreferences,
  readStoredConversationId,
  RESPONSE_STYLE_KEY,
  saveConversationId,
} from '../services/conversationSession';
import {
  getConversationReadiness,
  getConversationSnapshot,
  resumeConversationTurn as resumeConversationTurnRequest,
} from '../services/conversationApi';
import { appendSystemMessage, getSystemInstruction } from '../utils/conversationUi';
import { toConversationResponseState, toConversationSnapshotState } from '../utils/conversationSnapshots';

type ReadinessSnapshot = Awaited<ReturnType<typeof getConversationReadiness>>;

function useConversationResumeController() {
  const { conversationId } = useConversationRuntimeState();
  const { startupAlert, startupState, resumeAvailable, liveWebMode } = useConversationShellState();
  const { applySnapshot, setMessages, setStartupState, setResumeAvailable, setResponseStyle } = useConversationActions();

  const systemInstruction = useCallback(
    () => getSystemInstruction(getSavedResponseStyle(), readPreferredLanguage(), readSavedStylePreferences()),
    []);

  const resumeConversationTurn = useCallback(async (targetConversationId: string) => {
    const resumed = await resumeConversationTurnRequest(targetConversationId, {
      maxHistory: 24,
      systemInstruction: systemInstruction(),
      liveWebMode,
    });

    applySnapshot(toConversationResponseState(resumed));
    clearPendingTurn();
    setResumeAvailable(false);
  }, [applySnapshot, liveWebMode, setResumeAvailable, systemInstruction]);

  const resumeLastTurn = useCallback(async () => {
    const pending = readPendingTurn();
    if (pending?.conversationId) {
      try {
        await resumeConversationTurn(pending.conversationId);
        setStartupState('ready', null);
        return;
      } catch (error) {
        setStartupState('degraded', `Resume last turn failed: ${error instanceof Error ? error.message : 'Unknown error'}`);
      }
    }

    const savedConversationId = readStoredConversationId();
    if (!savedConversationId) {
      setResumeAvailable(false);
      return;
    }

    try {
      const snapshot = await getConversationSnapshot(savedConversationId);
      applySnapshot(toConversationSnapshotState(snapshot));
      if (snapshot.activeTurn?.hasPendingResponse) {
        await resumeConversationTurn(snapshot.conversationId);
      }
      setStartupState('ready', null);
      setResumeAvailable(false);
    } catch (error) {
      setStartupState('degraded', `Resume last turn failed: ${error instanceof Error ? error.message : 'Unknown error'}`);
      setResumeAvailable(true);
    }
  }, [applySnapshot, resumeConversationTurn, setResumeAvailable, setStartupState]);

  return {
    conversationId,
    startupAlert,
    startupState,
    resumeAvailable,
    resumeLastTurn,
    applySnapshot,
    setMessages,
    setResponseStyle,
    setResumeAvailable,
    setStartupState,
    resumeConversationTurn,
  };
}

export function useResumeLastTurnAction() {
  const { resumeLastTurn } = useConversationResumeController();
  return resumeLastTurn;
}

export function useConversationBootstrap() {
  const {
    conversationId,
    startupAlert,
    startupState,
    resumeAvailable,
    resumeLastTurn,
    applySnapshot,
    setMessages,
    setResponseStyle,
    setResumeAvailable,
    setStartupState,
    resumeConversationTurn,
  } = useConversationResumeController();

  useEffect(() => {
    if (conversationId) {
      saveConversationId(conversationId);
    }
  }, [conversationId]);

  useEffect(() => {
    setResumeAvailable(Boolean(readPendingTurn()?.conversationId || readStoredConversationId()));

    const onStorage = (event: StorageEvent) => {
      if (event.key === RESPONSE_STYLE_KEY && event.newValue) {
        setResponseStyle(event.newValue);
      }
    };

    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, [setResponseStyle, setResumeAvailable]);

  useEffect(() => {
    let disposed = false;

    const syncStartupState = (readiness: ReadinessSnapshot) => {
      const hasWarnings = readiness.status === 'degraded' || readiness.alerts.length > 0;
      setStartupState(readiness.readyForChat ? (hasWarnings ? 'degraded' : 'ready') : 'degraded', formatReadinessAlert(readiness));
      return hasWarnings;
    };

    const monitorStartupRecovery = async () => {
      for (let attempt = 0; attempt < 120; attempt += 1) {
        await delay(3000);
        if (disposed) {
          return;
        }

        try {
          const readiness = await getConversationReadiness();
          if (disposed) {
            return;
          }

          const hasWarnings = syncStartupState(readiness);
          if (readiness.readyForChat && !hasWarnings) {
            return;
          }
        } catch {
          // Keep the existing degraded banner until readiness becomes reachable again.
        }
      }
    };

    const bootstrap = async () => {
      const readiness = await waitForChatReadiness();
      if (disposed) {
        return;
      }

      const hasWarnings = syncStartupState(readiness);

      if (!readiness.readyForChat) {
        setMessages(previous => appendSystemMessage(previous, formatReadinessAlert(readiness)));
        void monitorStartupRecovery();
        return;
      }

      if (hasWarnings) {
        void monitorStartupRecovery();
      }

      const savedConversationId = readStoredConversationId();
      if (savedConversationId) {
        try {
          const snapshot = await getConversationSnapshot(savedConversationId);
          if (disposed) {
            return;
          }

          applySnapshot(toConversationSnapshotState(snapshot));
          if (snapshot.activeTurn?.hasPendingResponse) {
            await resumeConversationTurn(snapshot.conversationId);
          }
        } catch {
          clearStoredConversationId();
          setResumeAvailable(Boolean(readPendingTurn()?.conversationId));
        }
      }

      const pending = readPendingTurn();
      if (!pending?.conversationId) {
        return;
      }

      try {
        await resumeConversationTurn(pending.conversationId);
      } catch (error) {
        if (disposed) {
          return;
        }

        setStartupState('degraded', `Pending turn recovery failed: ${error instanceof Error ? error.message : 'Unknown error'}`);
        setResumeAvailable(true);
      }
    };

    void bootstrap().catch(error => {
      if (disposed) {
        return;
      }

      const message = error instanceof Error ? error.message : 'Backend readiness bootstrap failed.';
      setStartupState('degraded', message);
      setResumeAvailable(Boolean(readPendingTurn()?.conversationId || readStoredConversationId()));
      setMessages(previous => appendSystemMessage(previous, `Startup failed: ${message}`));
    });

    return () => {
      disposed = true;
    };
  }, [applySnapshot, resumeConversationTurn, setMessages, setResumeAvailable, setStartupState]);

  return {
    startupState,
    startupAlert,
    resumeAvailable,
    resumeLastTurn,
  };
}

async function waitForChatReadiness(maxAttempts = 20, delayMs = 1000) {
  let lastSnapshot: ReadinessSnapshot | null = null;
  let lastError: unknown = null;

  for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
    try {
      lastSnapshot = await getConversationReadiness();
      if (lastSnapshot.readyForChat) {
        return lastSnapshot;
      }
    } catch (error) {
      lastError = error;
    }

    await delay(delayMs);
  }

  if (lastSnapshot) {
    return lastSnapshot;
  }

  throw lastError instanceof Error ? lastError : new Error('Backend readiness check failed.');
}

function formatReadinessAlert(snapshot: ReadinessSnapshot) {
  if (snapshot.alerts.length > 0) {
    return snapshot.alerts.join(' ');
  }

  if (snapshot.readyForChat) {
    return `Backend is ready. Phase=${snapshot.phase}. WarmupMode=${snapshot.warmupMode}.`;
  }

  return `Backend is not ready yet. Phase=${snapshot.phase}. WarmupMode=${snapshot.warmupMode}.`;
}

function delay(timeoutMs: number) {
  return new Promise(resolve => window.setTimeout(resolve, timeoutMs));
}
