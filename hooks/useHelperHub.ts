import { useCallback, useEffect, useMemo, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { getAccessToken } from '../services/apiConfig';
import type { MutationProposal, ProgressLogEntry, StrategicPlan, ThoughtStreamEntry } from '../types';
import {
  buildReasoningFeed,
  createMutationReasoningEvent,
  createStrategyReasoningEvent,
  parseThoughtEvent,
} from '../services/reasoningSurface';

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

  const clearConversationSurface = useCallback(() => {
    setProgressEntries([]);
    setThoughts([]);
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
        setThoughts(previous => [createStrategyReasoningEvent(plan), ...previous].slice(0, MAX_THOUGHTS));
        return;
      }

      const mutation = parseTaggedJson<MutationProposal>(rawMessage, '[MUTATION_JSON]');
      if (mutation) {
        setActiveMutation(mutation);
        setThoughts(previous => [createMutationReasoningEvent(mutation), ...previous].slice(0, MAX_THOUGHTS));
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

    connection.on('ReceiveReasoningEvent', (payload: unknown) => {
      const serialized = typeof payload === 'string' ? payload : JSON.stringify(payload);
      const thought = parseThoughtEvent(serialized);
      setThoughts(previous => [thought, ...previous].slice(0, MAX_THOUGHTS));
    });

    connection.start().catch(error => console.error('Helper Hub Error:', error));

    return () => {
      connection.off('ReceiveProgress');
      connection.off('ReceiveThought');
      connection.off('ReceiveReasoningEvent');
      void connection.stop();
    };
  }, [hubUrl]);

  const reasoningFeed = useMemo(
    () => buildReasoningFeed(thoughts, progressEntries, currentPlan, activeMutation),
    [activeMutation, currentPlan, progressEntries, thoughts],
  );

  return {
    progressEntries,
    thoughts,
    reasoningFeed,
    currentPlan,
    activeMutation,
    clearProgressState,
    clearConversationSurface,
    dismissActiveMutation: () => setActiveMutation(null),
  };
}
