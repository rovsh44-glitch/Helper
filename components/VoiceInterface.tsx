import React, { useEffect, useMemo, useRef, useState } from 'react';
import type { ConversationInputMode } from '../types';

interface VoiceInterfaceProps {
  isProcessing: boolean;
  preferredLanguage: string;
  onInput: (text: string) => void;
  lastMessage?: string;
  lastMessageInputMode?: ConversationInputMode;
}

type BrowserSpeechRecognition = {
  continuous: boolean;
  interimResults: boolean;
  lang: string;
  start: () => void;
  stop: () => void;
  onresult?: (event: { results: ArrayLike<ArrayLike<{ transcript: string }>> }) => void;
  onerror?: (event: { error?: string }) => void;
  onend?: () => void;
};

export const VoiceInterface: React.FC<VoiceInterfaceProps> = ({
  isProcessing,
  preferredLanguage,
  onInput,
  lastMessage,
  lastMessageInputMode = 'text',
}) => {
  const [isListening, setIsListening] = useState(false);
  const [isSpeaking, setIsSpeaking] = useState(false);
  const [lastTranscript, setLastTranscript] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const recognitionRef = useRef<BrowserSpeechRecognition | null>(null);
  const lastSpokenKeyRef = useRef<string>('');
  const recognitionLanguage = useMemo(() => resolveRecognitionLanguage(preferredLanguage), [preferredLanguage]);

  const recognitionSupported = typeof window !== 'undefined' &&
    ('webkitSpeechRecognition' in window || 'SpeechRecognition' in window);
  const synthesisSupported = typeof window !== 'undefined' && 'speechSynthesis' in window;

  useEffect(() => {
    if (!recognitionSupported) {
      recognitionRef.current = null;
      return;
    }

    const SpeechRecognition = (window as unknown as {
      SpeechRecognition?: new () => BrowserSpeechRecognition;
      webkitSpeechRecognition?: new () => BrowserSpeechRecognition;
    }).SpeechRecognition || (window as unknown as {
      webkitSpeechRecognition?: new () => BrowserSpeechRecognition;
    }).webkitSpeechRecognition;

    if (!SpeechRecognition) {
      recognitionRef.current = null;
      return;
    }

    const recognition = new SpeechRecognition();
    recognition.continuous = false;
    recognition.interimResults = false;
    recognition.lang = recognitionLanguage;
    recognition.onresult = (event) => {
      const transcript = event.results?.[0]?.[0]?.transcript?.trim();
      if (!transcript) {
        setIsListening(false);
        return;
      }

      setLastTranscript(transcript);
      setErrorMessage(null);
      onInput(transcript);
      setIsListening(false);
    };
    recognition.onerror = (event) => {
      setErrorMessage(event.error ? `Speech recognition error: ${event.error}` : 'Speech recognition failed.');
      setIsListening(false);
    };
    recognition.onend = () => setIsListening(false);

    recognitionRef.current = recognition;
  }, [onInput, recognitionLanguage, recognitionSupported]);

  useEffect(() => {
    if (!synthesisSupported || !lastMessage || isProcessing || lastMessageInputMode !== 'voice') {
      return;
    }

    const speechText = buildSpeechPlaybackText(lastMessage);
    if (!speechText) {
      return;
    }

    const speechKey = `${lastMessageInputMode}:${speechText}`;
    if (lastSpokenKeyRef.current === speechKey) {
      return;
    }

    lastSpokenKeyRef.current = speechKey;
    window.speechSynthesis.cancel();

    const utterance = new SpeechSynthesisUtterance(speechText);
    utterance.lang = recognitionLanguage;
    utterance.onstart = () => setIsSpeaking(true);
    utterance.onend = () => setIsSpeaking(false);
    utterance.onerror = () => setIsSpeaking(false);
    window.speechSynthesis.speak(utterance);
  }, [isProcessing, lastMessage, lastMessageInputMode, recognitionLanguage, synthesisSupported]);

  const toggleListening = () => {
    if (!recognitionSupported || !recognitionRef.current) {
      setErrorMessage('Browser speech recognition is not available in this runtime.');
      return;
    }

    setErrorMessage(null);
    if (isListening) {
      recognitionRef.current.stop();
      setIsListening(false);
      return;
    }

    recognitionRef.current.lang = recognitionLanguage;
    recognitionRef.current.start();
    setIsListening(true);
  };

  const stopSpeaking = () => {
    if (!synthesisSupported) {
      return;
    }

    window.speechSynthesis.cancel();
    setIsSpeaking(false);
  };

  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-950/80 p-4 shadow-inner lg:w-[19rem]">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-[10px] uppercase tracking-[0.18em] text-slate-500">Voice</div>
          <div className="mt-1 text-sm text-slate-100">Speech-to-search</div>
          <div className="mt-1 text-xs text-slate-400">
            Voice turns reuse the same live web route and search session as text.
          </div>
        </div>
        <span className={`rounded-full border px-2 py-1 text-[10px] uppercase tracking-wide ${
          isListening
            ? 'border-red-700 bg-red-950/40 text-red-200'
            : isSpeaking
              ? 'border-emerald-700 bg-emerald-950/40 text-emerald-200'
              : 'border-slate-700 bg-slate-900/70 text-slate-400'
        }`}>
          {isListening ? 'Listening' : isSpeaking ? 'Speaking' : 'Idle'}
        </span>
      </div>

      <div className="mt-4 flex gap-2">
        <button
          onClick={toggleListening}
          disabled={isProcessing || !recognitionSupported}
          className="rounded-xl border border-slate-700 bg-slate-900 px-3 py-2 text-xs text-slate-100 transition-colors hover:border-primary-500 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {isListening ? 'Stop Mic' : 'Start Mic'}
        </button>
        <button
          onClick={stopSpeaking}
          disabled={!synthesisSupported}
          className="rounded-xl border border-slate-700 bg-slate-900 px-3 py-2 text-xs text-slate-300 transition-colors hover:border-slate-500 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Stop Voice
        </button>
      </div>

      <div className="mt-3 space-y-2 text-[11px] text-slate-400">
        <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2">
          Recognition: <span className="text-slate-200">{recognitionSupported ? recognitionLanguage : 'unsupported'}</span>
        </div>
        {lastTranscript && (
          <div className="rounded-xl border border-slate-800 bg-black/20 px-3 py-2">
            <div className="text-[10px] uppercase tracking-wide text-slate-500">Last Transcript</div>
            <div className="mt-1 text-xs text-slate-200">{lastTranscript}</div>
          </div>
        )}
        {errorMessage && (
          <div className="rounded-xl border border-amber-800 bg-amber-950/20 px-3 py-2 text-amber-200">
            {errorMessage}
          </div>
        )}
      </div>
    </div>
  );
};

function resolveRecognitionLanguage(preferredLanguage: string): string {
  if (preferredLanguage === 'ru') {
    return 'ru-RU';
  }

  if (preferredLanguage === 'en') {
    return 'en-US';
  }

  return navigator.language || 'en-US';
}

function buildSpeechPlaybackText(text: string): string {
  const stripped = text
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, '$1')
    .replace(/\[(\d+)(?::p\d+)?\]/g, ' ')
    .replace(/https?:\/\/\S+/g, ' ')
    .replace(/(^|\n)\s*[-*]\s+/g, '$1')
    .replace(/(^|\n)\s*\d+\.\s+/g, '$1')
    .replace(/\s+/g, ' ')
    .trim();

  if (!stripped) {
    return '';
  }

  return stripped.length <= 720 ? stripped : `${stripped.slice(0, 717).trimEnd()}...`;
}
