import { useCallback, useEffect, useRef, type Dispatch, type SetStateAction } from 'react';
import type { Message } from '../types';

type SetMessages = Dispatch<SetStateAction<Message[]>>;

const STREAM_FLUSH_INTERVAL_MS = 120;

export function useBufferedAssistantStream(setMessages: SetMessages) {
  const bufferedByMessageIdRef = useRef<Record<string, string>>({});
  const flushTimerRef = useRef<number | null>(null);

  const flushBufferedContent = useCallback(() => {
    const bufferedByMessageId = bufferedByMessageIdRef.current;
    const entries = Object.entries(bufferedByMessageId);
    if (entries.length === 0) {
      if (flushTimerRef.current !== null) {
        window.clearTimeout(flushTimerRef.current);
        flushTimerRef.current = null;
      }
      return;
    }

    bufferedByMessageIdRef.current = {};
    if (flushTimerRef.current !== null) {
      window.clearTimeout(flushTimerRef.current);
      flushTimerRef.current = null;
    }

    const updates = new Map(entries);
    setMessages(previous =>
      previous.map(message => {
        const nextChunk = updates.get(message.id);
        if (!nextChunk) {
          return message;
        }

        return {
          ...message,
          content: `${message.content || ''}${nextChunk}`,
        };
      }),
    );
  }, [setMessages]);

  const scheduleFlush = useCallback(() => {
    if (flushTimerRef.current !== null) {
      return;
    }

    flushTimerRef.current = window.setTimeout(() => {
      flushBufferedContent();
    }, STREAM_FLUSH_INTERVAL_MS);
  }, [flushBufferedContent]);

  const appendChunk = useCallback((messageId: string, chunkContent: string) => {
    if (!chunkContent) {
      return;
    }

    bufferedByMessageIdRef.current[messageId] = `${bufferedByMessageIdRef.current[messageId] || ''}${chunkContent}`;
    scheduleFlush();
  }, [scheduleFlush]);

  useEffect(() => () => flushBufferedContent(), [flushBufferedContent]);

  return {
    appendChunk,
    flushBufferedContent,
  };
}
