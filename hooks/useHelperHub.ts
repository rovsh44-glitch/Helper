import { useCallback, useEffect, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { getAccessToken } from '../services/apiConfig';
import type { MutationProposal, ProgressLogEntry, StrategicPlan, ThoughtStreamEntry } from '../types';

const MAX_PROGRESS_LOGS = 200;
const MAX_THOUGHTS = 50;

function parseTaggedJson<T>(rawMessage: string, tag: string): T | null {
  if (!rawMessage.includes(tag)) {
    return null;
  }

  try {
    return JSON.parse(rawMessage.split(tag)[1]) as T;
  } catch {
    return null;
  }
}

function parseThoughtEvent(rawMessage: string): ThoughtStreamEntry {
  try {
    const parsed = JSON.parse(rawMessage) as { content?: string; timestamp?: string; type?: string };
    return {
      id: crypto.randomUUID(),
      content: parsed.content || rawMessage,
      timestamp: parsed.timestamp ? new Date(parsed.timestamp).getTime() : Date.now(),
      type: parsed.type,
    };
  } catch {
    return {
      id: crypto.randomUUID(),
      content: rawMessage,
      timestamp: Date.now(),
    };
  }
}

export function useHelperHub(hubUrl: string) {
  const [progressEntries, setProgressEntries] = useState<ProgressLogEntry[]>([]);
  const [thoughts, setThoughts] = useState<ThoughtStreamEntry[]>([]);
  const [currentPlan, setCurrentPlan] = useState<StrategicPlan | null>(null);
  const [activeMutation, setActiveMutation] = useState<MutationProposal | null>(null);

  const clearProgressState = useCallback(() => {
    setProgressEntries([]);
    setCurrentPlan(null);
    setActiveMutation(null);
  }, []);

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => getAccessToken(),
      })
      .withAutomaticReconnect()
      .build();

    connection.on('ReceiveProgress', (rawMessage: string) => {
      const plan = parseTaggedJson<StrategicPlan>(rawMessage, '[STRATEGY_JSON]');
      if (plan) {
        setCurrentPlan(plan);
        return;
      }

      const mutation = parseTaggedJson<MutationProposal>(rawMessage, '[MUTATION_JSON]');
      if (mutation) {
        setActiveMutation(mutation);
        return;
      }

      setProgressEntries(previous => [
        ...previous.slice(-(MAX_PROGRESS_LOGS - 1)),
        {
          id: crypto.randomUUID(),
          message: rawMessage,
          timestamp: Date.now(),
        },
      ]);
    });

    connection.on('ReceiveThought', (rawThought: string) => {
      const thought = parseThoughtEvent(rawThought);
      setThoughts(previous => [thought, ...previous].slice(0, MAX_THOUGHTS));
    });

    connection.start().catch(error => console.error('Helper Hub Error:', error));

    return () => {
      connection.off('ReceiveProgress');
      connection.off('ReceiveThought');
      void connection.stop();
    };
  }, [hubUrl]);

  return {
    progressEntries,
    thoughts,
    currentPlan,
    activeMutation,
    clearProgressState,
    dismissActiveMutation: () => setActiveMutation(null),
  };
}
