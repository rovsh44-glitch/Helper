export interface ContinuityBackgroundTask {
  id: string;
  kind: string;
  title: string;
  status: string;
  createdAtUtc: string;
  dueAtUtc?: string;
  projectId?: string;
  notes?: string;
}

export interface ContinuityProactiveTopic {
  id: string;
  topic: string;
  frequency: string;
  enabled: boolean;
  createdAtUtc: string;
  projectId?: string;
}

export interface ContinuityLiveVoiceSession {
  sessionId: string;
  status: string;
  language: string;
  runtimeKind: string;
  interruptionsEnabled: boolean;
  isHeld: boolean;
  lastTranscript?: string;
  transcriptSegments?: string[];
  attachedReferenceCount: number;
  lastReferenceSummary?: string;
  captureChunkCount: number;
  approximateDurationMs: number;
  holdCount: number;
  resumeCount: number;
  recentChunks?: Array<{
    sequence: number;
    durationMs: number;
    byteCount: number;
    transcript?: string;
    capturedAtUtc: string;
  }>;
  startedAtUtc?: string;
  updatedAtUtc: string;
}
