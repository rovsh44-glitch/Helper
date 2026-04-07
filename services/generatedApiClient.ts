import { API_ROOT, clearAccessTokenCache, withApiHeaders, type SessionSurface } from './apiConfig';
import { fetchWithTimeout, type RequestTimeoutProfile } from './httpClient';

export type LiveWebMode = 'auto' | 'force_search' | 'no_web';
export type ConversationInputMode = 'text' | 'voice';

export interface ChatRequestDto {
  message: string;
  conversationId?: string;
  maxHistory?: number;
  systemInstruction?: string;
  branchId?: string;
  attachments?: AttachmentDto[];
  liveWebMode?: LiveWebMode;
  inputMode?: ConversationInputMode;
}

export interface AttachmentDto {
  id: string;
  type: string;
  name: string;
  sizeBytes: number;
  referenceUri?: string;
}

export interface ChatMessageDto {
  role: string;
  content: string;
  timestamp: string;
  turnId?: string;
  turnVersion?: number;
  branchId?: string;
  toolCalls?: string[];
  citations?: string[];
  attachments?: AttachmentDto[];
  inputMode?: ConversationInputMode;
}

export interface ClaimGroundingDto {
  claim: string;
  type: 'Fact' | 'Opinion' | 'Instruction' | string;
  sourceIndex?: number;
  evidenceGrade: string;
}

export interface SearchTraceSourceDto {
  ordinal: number;
  title: string;
  url: string;
  publishedAt?: string;
  evidenceKind?: string;
  trustLevel?: string;
  wasSanitized?: boolean;
  safetyFlags?: string[];
  snippet?: string;
  passageCount?: number;
}

export interface SearchTraceDto {
  requestedMode: LiveWebMode;
  resolvedRequirement: 'no_web_needed' | 'web_helpful' | 'web_required' | string;
  reason?: string;
  status: string;
  signals?: string[];
  events?: string[];
  sources?: SearchTraceSourceDto[];
  inputMode?: ConversationInputMode;
}

export interface ChatResponseDto {
  conversationId: string;
  response: string;
  messages: ChatMessageDto[];
  timestamp: string;
  confidence?: number;
  sources?: string[];
  turnId?: string;
  toolCalls?: string[];
  requiresConfirmation?: boolean;
  nextStep?: string;
  groundingStatus?: string;
  citationCoverage?: number;
  verifiedClaims?: number;
  totalClaims?: number;
  claimGroundings?: ClaimGroundingDto[];
  uncertaintyFlags?: string[];
  branchId?: string;
  availableBranches?: string[];
  executionMode?: string;
  budgetProfile?: string;
  budgetExceeded?: boolean;
  estimatedTokensGenerated?: number;
  reasoningEffort?: string;
  decisionExplanation?: string;
  repairClass?: string;
  searchTrace?: SearchTraceDto;
  inputMode?: ConversationInputMode;
}

export interface ChatResumeRequestDto {
  maxHistory?: number;
  systemInstruction?: string;
  liveWebMode?: LiveWebMode;
}

export interface ChatStreamResumeRequestDto {
  cursorOffset?: number;
  maxHistory?: number;
  systemInstruction?: string;
  turnId?: string;
  liveWebMode?: LiveWebMode;
}

export interface ConversationMemoryItemDto {
  id: string;
  type: string;
  content: string;
  scope?: string;
  retention?: string;
  whyRemembered?: string;
  priority?: number;
  createdAt: string;
  expiresAt?: string;
  sourceTurnId?: string;
  sourceProjectId?: string;
  isPersonal: boolean;
  userEditable?: boolean;
}

export interface ConversationMemoryPolicyDto {
  longTermMemoryEnabled: boolean;
  personalMemoryConsentGranted: boolean;
  personalMemoryConsentAt?: string;
  sessionMemoryTtlMinutes: number;
  taskMemoryTtlHours: number;
  longTermMemoryTtlDays: number;
}

export interface LiveVoiceSessionSyncDto {
  sessionId: string;
  language: string;
  runtimeKind: string;
  status: string;
  isHeld: boolean;
  transcript?: string;
  transcriptSegments?: string[];
  attachedReferenceCount?: number;
  lastReferenceSummary?: string;
  referenceArtifacts?: string[];
  interruptionsEnabled?: boolean;
}

export interface LiveVoiceChunkSyncDto {
  sessionId: string;
  sequence: number;
  durationMs: number;
  byteCount: number;
  transcript?: string;
  language?: string;
  runtimeKind?: string;
  attachedReferenceCount?: number;
  lastReferenceSummary?: string;
  referenceArtifacts?: string[];
}

export interface LiveVoiceCaptureChunkDto {
  sequence: number;
  durationMs: number;
  byteCount: number;
  transcript?: string;
  capturedAtUtc: string;
}

export interface ModelPoolSnapshotDto {
  pool: string;
  inFlight: number;
  totalCalls: number;
  failedCalls: number;
  timeoutCalls: number;
  avgLatencyMs: number;
}

export interface RouteTelemetryBucketDto {
  key: string;
  count: number;
}

export interface RouteTelemetryEventDto {
  recordedAtUtc: string;
  channel: string;
  operationKind: string;
  routeKey: string;
  quality: string;
  outcome: string;
  confidence?: number;
  modelRoute?: string;
  correlationId?: string;
  intentSource?: string;
  executionMode?: string;
  budgetProfile?: string;
  workloadClass?: string;
  degradationReason?: string;
  routeMatched: boolean;
  requiresClarification: boolean;
  budgetExceeded: boolean;
  compileGatePassed?: boolean;
  artifactValidationPassed?: boolean;
  smokePassed?: boolean;
  goldenTemplateEligible?: boolean;
  goldenTemplateMatched?: boolean;
  signals?: string[];
}

export interface RouteTelemetrySnapshotDto {
  schemaVersion: number;
  generatedAtUtc: string;
  totalEvents: number;
  channels: RouteTelemetryBucketDto[];
  operationKinds: RouteTelemetryBucketDto[];
  routes: RouteTelemetryBucketDto[];
  qualities: RouteTelemetryBucketDto[];
  modelRoutes: RouteTelemetryBucketDto[];
  recent: RouteTelemetryEventDto[];
  alerts: string[];
}

export interface ModelCapabilityCatalogEntryDto {
  capabilityId: string;
  routeKey: string;
  modelClass: string;
  intendedUse: string;
  latencyTier: string;
  supportsStreaming: boolean;
  supportsToolUse: boolean;
  supportsVision: boolean;
  fallbackClass: string;
  configuredFallbackModel?: string;
  resolvedModel: string;
  resolvedModelAvailable: boolean;
  notes: string[];
}

export interface DeclaredCapabilityCatalogEntryDto {
  capabilityId: string;
  surfaceKind: string;
  ownerId: string;
  displayName: string;
  declaredCapability: string;
  status: string;
  owningGate?: string;
  evidenceType?: string;
  evidenceRef?: string;
  available: boolean;
  certificationRelevant: boolean;
  enabledInCertification: boolean;
  certified: boolean;
  hasCriticalAlerts: boolean;
  notes: string[];
}

export interface CapabilityCatalogSurfaceSummaryDto {
  surfaceKind: string;
  total: number;
  available: number;
  certified: number;
  missingGateOwnership: number;
  disabledInCertification: number;
  degraded: number;
}

export interface CapabilityCatalogSummaryDto {
  totalDeclaredCapabilities: number;
  missingGateOwnership: number;
  disabledInCertification: number;
  degraded: number;
  surfaces: CapabilityCatalogSurfaceSummaryDto[];
}

export interface CapabilityCatalogSnapshotDto {
  generatedAtUtc: string;
  models: ModelCapabilityCatalogEntryDto[];
  declaredCapabilities: DeclaredCapabilityCatalogEntryDto[];
  summary: CapabilityCatalogSummaryDto;
  alerts: string[];
}

export interface HumanLikeConversationDashboardSummaryDto {
  styleTurns: number;
  repeatedPhraseRate: number;
  mixedLanguageRate: number;
  clarificationTurns: number;
  helpfulClarificationTurns: number;
  clarificationRepairEscalations: number;
  clarificationHelpfulnessRate: number;
  repairAttempts: number;
  repairSucceeded: number;
  repairSuccessRate: number;
  styleFeedbackVotes: number;
  styleFeedbackAverageRating: number;
  styleLowRatingRate: number;
}

export interface HumanLikeConversationDashboardTrendPointDto {
  dateUtc: string;
  styleTurns: number;
  repeatedPhraseRate: number;
  mixedLanguageRate: number;
  clarificationTurns: number;
  helpfulClarificationTurns: number;
  clarificationHelpfulnessRate: number;
  repairAttempts: number;
  repairSuccessRate: number;
  styleFeedbackVotes: number;
  styleFeedbackAverageRating: number;
}

export interface HumanLikeConversationDashboardSnapshotDto {
  generatedAtUtc: string;
  windowDays: number;
  summary: HumanLikeConversationDashboardSummaryDto;
  trend: HumanLikeConversationDashboardTrendPointDto[];
  alerts: string[];
}

export interface ControlPlaneSnapshotDto {
  readiness: {
    status: string;
    phase: string;
    lifecycleState: string;
    readyForChat: boolean;
    listening: boolean;
    warmupMode: string;
    lastTransitionUtc?: string;
    startedAtUtc: string;
    listeningAtUtc?: string;
    minimalReadyAtUtc?: string;
    warmReadyAtUtc?: string;
    timeToListeningMs?: number;
    timeToReadyMs?: number;
    timeToWarmReadyMs?: number;
    alerts: string[];
  };
  configuration: {
    isValid: boolean;
    alerts: string[];
  };
  policies: {
    researchEnabled: boolean;
    groundingEnabled: boolean;
    synchronousCriticEnabled: boolean;
    asyncAuditEnabled: boolean;
    shadowModeEnabled: boolean;
    safeFallbackResponsesOnly: boolean;
  };
  modelGateway: {
    availableModels: string[];
    currentModel: string;
    pools: ModelPoolSnapshotDto[];
    lastCatalogRefreshAtUtc?: string;
    lastWarmupAtUtc?: string;
    alerts: string[];
    activeProfileId?: string;
  };
  persistenceQueue: {
    pending: number;
    enqueued: number;
    dropped: number;
    flushed: number;
    avgFlushMs: number;
    lastFlushedAtUtc?: string;
    alerts: string[];
  };
  persistence: {
    enabled: boolean;
    ready: boolean;
    loaded: boolean;
    lastFlushSucceeded: boolean;
    pendingDirtyConversations: number;
    lastJournalWriteAtUtc?: string;
    lastSnapshotAtUtc?: string;
    snapshotPath: string;
    journalPath: string;
    alerts: string[];
  };
  auditQueue: {
    pending: number;
    enqueued: number;
    dropped: number;
    processed: number;
    failed: number;
    deadLettered: number;
    avgProcessingMs: number;
    lastProcessedAt?: string;
    alerts: string[];
  };
  routeTelemetry?: RouteTelemetrySnapshotDto;
  alerts: string[];
}

export interface RuntimeLogSourceDto {
  id: string;
  label: string;
  displayPath: string;
  sizeBytes: number;
  lastWriteTimeUtc?: string;
  totalLines: number;
  isPrimary: boolean;
}

export interface ProviderModelClassBindingDto {
  modelClass: string;
  modelName: string;
}

export interface ProviderCredentialReferenceDto {
  apiKeyEnvVar?: string;
  required: boolean;
  configured: boolean;
}

export interface ProviderProfileDto {
  id: string;
  displayName: string;
  kind: string;
  transportKind: string;
  baseUrl: string;
  enabled: boolean;
  isBuiltIn: boolean;
  isLocal: boolean;
  trustMode: string;
  supportedGoals: string[];
  modelBindings: ProviderModelClassBindingDto[];
  credential?: ProviderCredentialReferenceDto;
  embeddingModel?: string;
  preferredReasoningEffort?: string;
  notes?: string;
}

export interface ProviderProfileValidationDto {
  isValid: boolean;
  alerts: string[];
  warnings: string[];
}

export interface ProviderCapabilitySummaryDto {
  supportsFast: boolean;
  supportsReasoning: boolean;
  supportsCoder: boolean;
  supportsVision: boolean;
  supportsBackground: boolean;
  supportsResearchVerified: boolean;
  supportsPrivacyFirst: boolean;
  requiresLocalRuntime: boolean;
}

export interface ProviderProfileSummaryDto {
  profile: ProviderProfileDto;
  validation: ProviderProfileValidationDto;
  capabilities: ProviderCapabilitySummaryDto;
  isActive: boolean;
}

export interface ProviderProfilesSnapshotDto {
  generatedAtUtc: string;
  activeProfileId?: string;
  profiles: ProviderProfileSummaryDto[];
  alerts: string[];
}

export interface ProviderActivationRequestDto {
  profileId: string;
}

export interface ProviderActivationResultDto {
  success: boolean;
  activeProfileId?: string;
  reasonCodes: string[];
  warnings: string[];
}

export interface ProviderRecommendationRequestDto {
  goal: string;
  preferLocal?: boolean;
  needVision?: boolean;
  latencyPreference?: string;
  codingIntensity?: string;
}

export interface ProviderRecommendationResultDto {
  recommendedProfileId?: string;
  alternativeProfileIds: string[];
  reasonCodes: string[];
  warnings: string[];
}

export interface ProviderDoctorRunRequestDto {
  profileId?: string;
  includeInactive?: boolean;
}

export interface ProviderDoctorCheckDto {
  code: string;
  status: string;
  severity: string;
  summary: string;
  detail?: string;
  durationMs?: number;
}

export interface ProviderDoctorProfileReportDto {
  profileId: string;
  displayName: string;
  transportKind: string;
  baseUrl: string;
  isActive: boolean;
  isEnabled: boolean;
  status: string;
  capabilities: ProviderCapabilitySummaryDto;
  checks: ProviderDoctorCheckDto[];
  alerts: string[];
  warnings: string[];
}

export interface ProviderDoctorReportDto {
  generatedAtUtc: string;
  status: string;
  activeProfileId?: string;
  profiles: ProviderDoctorProfileReportDto[];
  alerts: string[];
}

export interface RuntimeLogEntryDto {
  sourceId: string;
  lineNumber: number;
  text: string;
  severity: 'error' | 'warn' | 'info' | 'debug' | 'neutral' | string;
  timestampLabel?: string;
  isContinuation: boolean;
  semantics?: RuntimeLogSemanticsDto;
}

export interface RuntimeLogSemanticsDto {
  scope: string;
  domain: string;
  operationKind: string;
  summary: string;
  route?: string;
  correlationId?: string;
  latencyMs?: number;
  latencyBucket?: string;
  degradationReason?: string;
  markers?: string[];
  structured: boolean;
}

export interface RuntimeLogsSnapshotDto {
  schemaVersion: number;
  semanticsVersion: string;
  generatedAtUtc: string;
  sources: RuntimeLogSourceDto[];
  entries: RuntimeLogEntryDto[];
  alerts: string[];
}

export interface TurnRegenerateRequestDto {
  maxHistory?: number;
  systemInstruction?: string;
  branchId?: string;
  liveWebMode?: LiveWebMode;
}

export interface BranchMergeRequestDto {
  sourceBranchId: string;
  targetBranchId: string;
}

export interface BranchCompareResponseDto {
  success: boolean;
  conversationId: string;
  sourceBranchId: string;
  targetBranchId: string;
  sourceSummary?: string;
  targetSummary?: string;
  sourceMessageCount: number;
  targetMessageCount: number;
  sharedTurnIds: string[];
  sourceOnlyTurnIds: string[];
  targetOnlyTurnIds: string[];
  sourceOnlyMessages: Array<{
    role: string;
    turnId?: string;
    timestamp: string;
    contentPreview: string;
    provenance: {
      toolCalls: number;
      citations: number;
      attachments: number;
    };
  }>;
}

export interface ConversationRepairRequestDto {
  correctedIntent: string;
  turnId?: string;
  repairNote?: string;
  maxHistory?: number;
  systemInstruction?: string;
  branchId?: string;
  liveWebMode?: LiveWebMode;
}

export interface SearchRequestDto {
  query: string;
  limit?: number;
}

export interface RagIngestRequestDto {
  title: string;
  content: string;
}

export interface AddGoalDto {
  title: string;
  description: string;
}

export interface UpdateGoalDto {
  title: string;
  description: string;
}

export interface ResearchRequestDto {
  topic: string;
}

export interface GenerationRequestDto {
  prompt: string;
  outputPath?: string;
}

export interface StrategicPlanRequestDto {
  task: string;
  context?: string;
}

export interface ArchitecturePlanRequestDto {
  prompt: string;
  targetOs?: string;
}

export interface BuildRequestDto {
  projectPath: string;
}

export interface WorkspaceProjectRequestDto {
  projectPath: string;
}

export interface WorkspaceNodeRequestDto {
  projectPath: string;
  relativePath: string;
}

export interface WorkspaceCreateRequestDto {
  projectPath: string;
  parentRelativePath?: string;
  name: string;
  isFolder: boolean;
}

export interface WorkspaceRenameRequestDto {
  projectPath: string;
  relativePath: string;
  newName: string;
}

export interface WorkspaceDeleteRequestDto {
  projectPath: string;
  relativePath: string;
}

export interface WorkspaceFileDto {
  name: string;
  path: string;
  language: string;
}

export interface WorkspaceFolderDto {
  name: string;
  path: string;
  files: WorkspaceFileDto[];
  folders: WorkspaceFolderDto[];
}

export interface WorkspaceProjectDto {
  name: string;
  fullPath: string;
  root: WorkspaceFolderDto;
}

export interface FileWriteRequestDto {
  path: string;
  content?: string;
}

export interface StreamChunk {
  type: 'token' | 'done' | 'heartbeat' | 'stage' | 'warning';
  content?: string;
  offset?: number;
  fullResponse?: string;
  conversationId?: string;
  timestamp?: string;
  turnId?: string;
  confidence?: number;
  sources?: string[];
  toolCalls?: string[];
  requiresConfirmation?: boolean;
  nextStep?: string;
  groundingStatus?: string;
  citationCoverage?: number;
  verifiedClaims?: number;
  totalClaims?: number;
  claimGroundings?: ClaimGroundingDto[];
  uncertaintyFlags?: string[];
  branchId?: string;
  availableBranches?: string[];
  executionMode?: string;
  budgetProfile?: string;
  budgetExceeded?: boolean;
  estimatedTokensGenerated?: number;
  searchTrace?: SearchTraceDto;
  inputMode?: ConversationInputMode;
  stage?: string;
  warningCode?: string;
}

type RequestOptions = {
  timeoutMs?: number;
  profile?: RequestTimeoutProfile;
  label?: string;
  surface?: SessionSurface;
};

async function sendRequest(path: string, init?: RequestInit, options: RequestOptions = {}, forceRefresh = false): Promise<Response> {
  const headers = await withApiHeaders({
    ...(init?.headers as Record<string, string> | undefined),
  }, {
    forceRefresh,
    surface: options.surface,
  });

  try {
    return await fetchWithTimeout(`${API_ROOT}${path}`, {
      ...init,
      headers,
    }, {
      profile: options.profile ?? 'standard',
      timeoutMs: options.timeoutMs,
      label: options.label ?? `API ${path}`,
    });
  } catch (error) {
    const details = error instanceof Error ? error.message : 'network failure';
    throw new Error(`API network error ${path}: ${details}`);
  }
}

function shouldRetryWithFreshSession(status: number, responseText: string): boolean {
  return status === 401 || (status === 403 && /missing scope/i.test(responseText));
}

async function request<T>(path: string, init?: RequestInit, options: RequestOptions = {}): Promise<T> {
  let response = await sendRequest(path, init, options);

  if (!response.ok) {
    let text = await response.text();
    let status = response.status;
    if (shouldRetryWithFreshSession(status, text)) {
      clearAccessTokenCache(options.surface);
      response = await sendRequest(path, init, options, true);
      if (response.ok) {
        return await response.json();
      }

      status = response.status;
      text = await response.text();
    }

    throw new Error(`API ${status} ${path}: ${text}`);
  }

  return await response.json();
}

async function requestVoid(path: string, init?: RequestInit, options: RequestOptions = {}): Promise<void> {
  await request<unknown>(path, init, options);
}

export class HelperApiClient {
  handshake() {
    return request<{ status: string; auth: string; requiresKey: boolean; role?: string; scopes?: string[] }>('/handshake', { method: 'GET' }, { profile: 'quick', label: 'Handshake' });
  }

  readiness() {
    return request<{
      status: string;
      phase: string;
      readyForChat: boolean;
      warmupMode: string;
      lastTransitionUtc?: string;
      authReady?: boolean;
      persistenceReady?: boolean;
      persistenceLoaded?: boolean;
      catalogReady?: boolean;
      warmupCompleted?: boolean;
      alerts: string[];
    }>('/readiness', { method: 'GET' }, { profile: 'startup', label: 'Readiness check' });
  }

  getOpenApi() {
    return request<any>('/openapi.json', { method: 'GET' });
  }

  getControlPlane() {
    return request<ControlPlaneSnapshotDto>('/control-plane', { method: 'GET' }, { surface: 'runtime_console' });
  }

  getCapabilityCatalog() {
    return request<CapabilityCatalogSnapshotDto>('/capabilities/catalog', { method: 'GET' }, { surface: 'runtime_console' });
  }

  getMetrics() {
    return request<any>('/metrics', { method: 'GET' }, { surface: 'runtime_console' });
  }

  getHumanLikeConversationDashboard(days?: number) {
    const search = new URLSearchParams();
    if (typeof days === 'number') {
      search.set('days', String(days));
    }

    const suffix = search.size > 0 ? `?${search.toString()}` : '';
    return request<HumanLikeConversationDashboardSnapshotDto>(`/metrics/human-like-conversation${suffix}`, { method: 'GET' }, { surface: 'runtime_console' });
  }

  getRuntimeLogs(query?: { tail?: number; maxSources?: number }) {
    const search = new URLSearchParams();
    if (typeof query?.tail === 'number') {
      search.set('tail', String(query.tail));
    }
    if (typeof query?.maxSources === 'number') {
      search.set('maxSources', String(query.maxSources));
    }

    const suffix = search.size > 0 ? `?${search.toString()}` : '';
    return request<RuntimeLogsSnapshotDto>(`/runtime/logs${suffix}`, { method: 'GET' }, { surface: 'runtime_console' });
  }

  getProviderProfiles() {
    return request<ProviderProfilesSnapshotDto>('/settings/provider-profiles', { method: 'GET' }, { surface: 'settings' });
  }

  getActiveProviderProfile() {
    return request<ProviderProfileSummaryDto>('/settings/provider-profiles/active', { method: 'GET' }, { surface: 'settings' });
  }

  activateProviderProfile(body: ProviderActivationRequestDto) {
    return request<ProviderActivationResultDto>('/settings/provider-profiles/activate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'settings' });
  }

  recommendProviderProfile(body: ProviderRecommendationRequestDto) {
    return request<ProviderRecommendationResultDto>('/settings/provider-profiles/recommend', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'settings' });
  }

  runRuntimeDoctor(body?: ProviderDoctorRunRequestDto) {
    return request<ProviderDoctorReportDto>('/settings/runtime-doctor/run', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body ?? {}),
    }, { surface: 'settings', profile: 'startup', timeoutMs: 20_000, label: 'Runtime doctor' });
  }

  chat(body: ChatRequestDto) {
    return request<ChatResponseDto>('/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  getConversation(conversationId: string) {
    return request<{
      conversationId: string;
      activeBranchId?: string;
      branches?: string[];
      messages: ChatMessageDto[];
      activeTurn?: { turnId?: string; startedAt?: string; hasPendingResponse?: boolean };
      preferences?: {
        longTermMemoryEnabled: boolean;
        personalMemoryConsentGranted?: boolean;
        personalMemoryConsentAt?: string;
        sessionMemoryTtlMinutes?: number;
        taskMemoryTtlHours?: number;
        longTermMemoryTtlDays?: number;
        preferredLanguage?: string;
        detailLevel?: string;
        formality?: string;
        domainFamiliarity?: string;
        preferredStructure?: string;
        warmth?: string;
        enthusiasm?: string;
        directness?: string;
        defaultAnswerShape?: string;
        searchLocalityHint?: string;
        decisionAssertiveness?: string;
        clarificationTolerance?: string;
        citationPreference?: string;
        repairStyle?: string;
        reasoningStyle?: string;
        reasoningEffort?: string;
        personaBundleId?: string;
        projectId?: string;
        projectLabel?: string;
        projectInstructions?: string;
        projectMemoryEnabled?: boolean;
        backgroundResearchEnabled?: boolean;
        proactiveUpdatesEnabled?: boolean;
        memoryTags: string[];
        memoryItemsCount?: number;
      };
      projectContext?: {
        projectId: string;
        label?: string;
        instructions?: string;
        memoryEnabled: boolean;
        referenceArtifacts?: string[];
        updatedAtUtc: string;
      };
      backgroundTasks?: Array<{
        id: string;
        kind: string;
        title: string;
        status: string;
        createdAtUtc: string;
        dueAtUtc?: string;
        projectId?: string;
        notes?: string;
      }>;
      proactiveTopics?: Array<{
        id: string;
        topic: string;
        frequency: string;
        enabled: boolean;
        createdAtUtc: string;
        projectId?: string;
      }>;
      liveVoiceSession?: {
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
        recentChunks?: LiveVoiceCaptureChunkDto[];
        startedAtUtc?: string;
        updatedAtUtc: string;
      };
    }>(`/chat/${encodeURIComponent(conversationId)}`, { method: 'GET' }, { profile: 'startup', label: 'Conversation restore' });
  }

  resumeConversationTurn(conversationId: string, body: ChatResumeRequestDto) {
    return request<ChatResponseDto>(`/chat/${encodeURIComponent(conversationId)}/resume`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { profile: 'startup', timeoutMs: 15_000, label: 'Resume conversation turn' });
  }

  async resumeChatStream(conversationId: string, body: ChatStreamResumeRequestDto, onChunk: (chunk: StreamChunk) => void): Promise<void> {
    const headers = await withApiHeaders({ 'Content-Type': 'application/json' });
    let response: Response;
    try {
      response = await fetchWithTimeout(`${API_ROOT}/chat/${encodeURIComponent(conversationId)}/stream/resume`, {
        method: 'POST',
        headers,
        body: JSON.stringify(body),
      }, {
        profile: 'stream_connect',
        label: 'Chat stream resume',
      });
    } catch (error) {
      const details = error instanceof Error ? error.message : 'network failure';
      throw new Error(`Chat stream resume network error: ${details}`);
    }

    if (!response.ok || !response.body) {
      throw new Error(`Chat stream resume failed: ${response.status}`);
    }

    await this.readSse(response, onChunk);
  }

  setConversationPreferences(conversationId: string, body: {
    longTermMemoryEnabled?: boolean;
    preferredLanguage?: string;
    detailLevel?: string;
    formality?: string;
    domainFamiliarity?: string;
    preferredStructure?: string;
    warmth?: string;
    enthusiasm?: string;
    directness?: string;
    defaultAnswerShape?: string;
    searchLocalityHint?: string;
    decisionAssertiveness?: string;
    clarificationTolerance?: string;
    citationPreference?: string;
    repairStyle?: string;
    reasoningStyle?: string;
    reasoningEffort?: string;
    personaBundleId?: string;
    projectId?: string;
    projectLabel?: string;
    projectInstructions?: string;
    projectMemoryEnabled?: boolean;
    backgroundResearchEnabled?: boolean;
    proactiveUpdatesEnabled?: boolean;
    personalMemoryConsentGranted?: boolean;
    sessionMemoryTtlMinutes?: number;
    taskMemoryTtlHours?: number;
    longTermMemoryTtlDays?: number;
  }) {
    return request<{
      success: boolean;
      longTermMemoryEnabled: boolean;
      personalMemoryConsentGranted?: boolean;
      personalMemoryConsentAt?: string;
      sessionMemoryTtlMinutes?: number;
      taskMemoryTtlHours?: number;
      longTermMemoryTtlDays?: number;
      preferredLanguage?: string;
      detailLevel?: string;
      formality?: string;
      domainFamiliarity?: string;
      preferredStructure?: string;
      warmth?: string;
      enthusiasm?: string;
      directness?: string;
      defaultAnswerShape?: string;
      searchLocalityHint?: string;
      decisionAssertiveness?: string;
      clarificationTolerance?: string;
      citationPreference?: string;
      repairStyle?: string;
      reasoningStyle?: string;
      reasoningEffort?: string;
      personaBundleId?: string;
      projectId?: string;
      projectLabel?: string;
      projectInstructions?: string;
      projectMemoryEnabled?: boolean;
      backgroundResearchEnabled?: boolean;
      proactiveUpdatesEnabled?: boolean;
    }>(`/chat/${encodeURIComponent(conversationId)}/preferences`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  getConversationMemory(conversationId: string) {
    return request<{
      conversationId: string;
      policy: ConversationMemoryPolicyDto;
      items: ConversationMemoryItemDto[];
    }>(`/chat/${encodeURIComponent(conversationId)}/memory`, { method: 'GET' });
  }

  deleteConversationMemoryItem(conversationId: string, memoryId: string) {
    return request<{ success: boolean }>(`/chat/${encodeURIComponent(conversationId)}/memory/${encodeURIComponent(memoryId)}`, {
      method: 'DELETE',
    });
  }

  cancelBackgroundTask(conversationId: string, taskId: string, body?: { reason?: string }) {
    return request<{ success: boolean; taskId: string; status: string }>(`/chat/${encodeURIComponent(conversationId)}/background/${encodeURIComponent(taskId)}/cancel`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body ?? {}),
    });
  }

  setProactiveTopicEnabled(conversationId: string, topicId: string, body: { enabled: boolean }) {
    return request<{ success: boolean; topicId: string; enabled: boolean }>(`/chat/${encodeURIComponent(conversationId)}/topics/${encodeURIComponent(topicId)}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  syncLiveVoiceSession(conversationId: string, body: LiveVoiceSessionSyncDto) {
    return request<{
      success: boolean;
      liveVoiceSession: {
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
        recentChunks?: LiveVoiceCaptureChunkDto[];
        startedAtUtc?: string;
        updatedAtUtc: string;
      };
    }>(`/chat/${encodeURIComponent(conversationId)}/voice/session`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  appendLiveVoiceChunk(conversationId: string, sessionId: string, body: LiveVoiceChunkSyncDto) {
    return request<{
      success: boolean;
      liveVoiceSession: {
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
        recentChunks?: LiveVoiceCaptureChunkDto[];
        startedAtUtc?: string;
        updatedAtUtc: string;
      };
    }>(`/chat/${encodeURIComponent(conversationId)}/voice/session/${encodeURIComponent(sessionId)}/chunks`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  clearLiveVoiceSession(conversationId: string, sessionId: string) {
    return request<{ success: boolean; sessionId: string }>(`/chat/${encodeURIComponent(conversationId)}/voice/session/${encodeURIComponent(sessionId)}`, {
      method: 'DELETE',
    });
  }

  deleteConversation(conversationId: string) {
    return requestVoid(`/chat/${encodeURIComponent(conversationId)}`, { method: 'DELETE' }, { profile: 'quick', label: 'Delete conversation' });
  }

  regenerateTurn(conversationId: string, turnId: string, body: TurnRegenerateRequestDto) {
    return request<ChatResponseDto>(`/chat/${encodeURIComponent(conversationId)}/turns/${encodeURIComponent(turnId)}/regenerate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  createBranch(conversationId: string, body: { fromTurnId: string; branchId?: string }) {
    return request<{ success: boolean; branchId?: string; error?: string }>(`/chat/${encodeURIComponent(conversationId)}/branches`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  activateBranch(conversationId: string, branchId: string) {
    return request<{ success: boolean; branchId?: string; error?: string }>(`/chat/${encodeURIComponent(conversationId)}/branches/${encodeURIComponent(branchId)}/activate`, {
      method: 'POST',
    });
  }

  compareBranches(conversationId: string, sourceBranchId: string, targetBranchId: string) {
    return request<BranchCompareResponseDto>(`/chat/${encodeURIComponent(conversationId)}/branches/compare?sourceBranchId=${encodeURIComponent(sourceBranchId)}&targetBranchId=${encodeURIComponent(targetBranchId)}`, {
      method: 'GET',
    });
  }

  mergeBranches(conversationId: string, body: BranchMergeRequestDto) {
    return request<{ success: boolean; sourceBranchId?: string; targetBranchId?: string; mergedMessages?: number; error?: string }>(`/chat/${encodeURIComponent(conversationId)}/branches/merge`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  repairConversation(conversationId: string, body: ConversationRepairRequestDto) {
    return request<ChatResponseDto>(`/chat/${encodeURIComponent(conversationId)}/repair`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  submitFeedback(conversationId: string, body: { turnId?: string; rating: number; tags?: string[]; comment?: string }) {
    return request<{ success: boolean; snapshot?: { totalVotes: number; averageRating: number; helpfulnessScore: number; alerts: string[] } }>(`/chat/${encodeURIComponent(conversationId)}/feedback`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  async streamChat(body: ChatRequestDto, onChunk: (chunk: StreamChunk) => void): Promise<void> {
    const headers = await withApiHeaders({ 'Content-Type': 'application/json' });
    let response: Response;
    try {
      response = await fetchWithTimeout(`${API_ROOT}/chat/stream`, {
        method: 'POST',
        headers,
        body: JSON.stringify(body),
      }, {
        profile: 'stream_connect',
        label: 'Chat stream',
      });
    } catch (error) {
      const details = error instanceof Error ? error.message : 'network failure';
      throw new Error(`Chat stream network error: ${details}`);
    }

    if (!response.ok || !response.body) {
      throw new Error(`Chat stream failed: ${response.status}`);
    }

    let doneReceived = false;
    let cursorOffset = 0;
    let resolvedConversationId = body.conversationId;
    let resolvedTurnId: string | undefined;

    const trackingChunkConsumer = (chunk: StreamChunk) => {
      if (chunk.conversationId) resolvedConversationId = chunk.conversationId;
      if (chunk.turnId) resolvedTurnId = chunk.turnId;

      if (chunk.type === 'token' && chunk.content) {
        const nextOffset = typeof chunk.offset === 'number'
          ? chunk.offset
          : cursorOffset + chunk.content.length;
        cursorOffset = Math.max(cursorOffset, nextOffset);
      }

      if (chunk.type === 'done') {
        doneReceived = true;
        if (chunk.fullResponse) {
          cursorOffset = Math.max(cursorOffset, chunk.fullResponse.length);
        }
      }

      onChunk(chunk);
    };

    await this.readSse(response, trackingChunkConsumer);

    if (doneReceived || !resolvedConversationId) {
      return;
    }

    await this.resumeChatStream(
      resolvedConversationId,
      {
        cursorOffset,
        maxHistory: body.maxHistory,
        systemInstruction: body.systemInstruction,
        turnId: resolvedTurnId,
      },
      trackingChunkConsumer
    );

    if (!doneReceived) {
      throw new Error('Streaming resume completed without done event.');
    }
  }

  private async readSse(response: Response, onChunk: (chunk: StreamChunk) => void): Promise<void> {
    if (!response.body) {
      return;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { value, done } = await this.readSseChunk(reader);
      if (done) {
        break;
      }

      buffer += decoder.decode(value, { stream: true });
      const events = buffer.split('\n\n');
      buffer = events.pop() || '';

      for (const event of events) {
        const line = event
          .split('\n')
          .map(part => part.trim())
          .find(part => part.startsWith('data:'));

        if (!line) continue;
        const payload = line.substring(5).trim();
        if (!payload) continue;

        try {
          const chunk = JSON.parse(payload) as StreamChunk;
          onChunk(chunk);
        } catch {
          // Keep stream resilient to malformed server events.
        }
      }
    }
  }

  private async readSseChunk(reader: ReadableStreamDefaultReader<Uint8Array>): Promise<ReadableStreamReadResult<Uint8Array>> {
    let timeoutId: number | undefined;
    try {
      return await Promise.race([
        reader.read(),
        new Promise<never>((_, reject) => {
          timeoutId = window.setTimeout(() => {
            reject(new Error('SSE stream stalled beyond heartbeat deadline.'));
          }, 20_000);
        })
      ]);
    } finally {
      if (typeof timeoutId === 'number') {
        window.clearTimeout(timeoutId);
      }
    }
  }

  ragSearch(body: SearchRequestDto) {
    return request<any[]>('/rag/search', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  ragIngest(body: RagIngestRequestDto) {
    return request<{ success: boolean }>('/rag/ingest', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  getGoals(query?: { includeCompleted?: boolean }) {
    const search = new URLSearchParams();
    if (typeof query?.includeCompleted === 'boolean') {
      search.set('includeCompleted', String(query.includeCompleted));
    }

    const suffix = search.size > 0 ? `?${search.toString()}` : '';
    return request<any[]>(`/goals${suffix}`, { method: 'GET' });
  }

  addGoal(body: AddGoalDto) {
    return request<{ success: boolean }>('/goals', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  updateGoal(goalId: string, body: UpdateGoalDto) {
    return request<{ success: boolean }>(`/goals/${goalId}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  completeGoal(goalId: string) {
    return request<{ success: boolean }>(`/goals/${goalId}/complete`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({}),
    });
  }

  deleteGoal(goalId: string) {
    return request<{ success: boolean }>(`/goals/${goalId}`, {
      method: 'DELETE',
    });
  }

  research(body: ResearchRequestDto) {
    return request<any>('/helper/research', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  generate(body: GenerationRequestDto) {
    return request<any>('/helper/generate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  build(body: BuildRequestDto) {
    return request<any>('/build', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  openWorkspace(body: WorkspaceProjectRequestDto) {
    return request<{ success: boolean; project: WorkspaceProjectDto }>('/workspace/open', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  readWorkspaceFile(body: WorkspaceNodeRequestDto) {
    return request<{ success: boolean; path: string; content: string }>('/workspace/file/read', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  createWorkspaceNode(body: WorkspaceCreateRequestDto) {
    return request<{ success: boolean; path: string }>('/workspace/node/create', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  renameWorkspaceNode(body: WorkspaceRenameRequestDto) {
    return request<{ success: boolean; path: string }>('/workspace/node/rename', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  deleteWorkspaceNode(body: WorkspaceDeleteRequestDto) {
    return request<{ success: boolean }>('/workspace/node/delete', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  writeFile(body: FileWriteRequestDto) {
    return request<{ success: boolean }>('/fs/write', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  writeRelativeFile(body: FileWriteRequestDto) {
    return request<{ success: boolean; path: string }>('/fs/write-relative', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  planStrategy(body: StrategicPlanRequestDto) {
    return request<{
      plan: any;
      activeGoals: any[];
      route: any;
      analyzedAtUtc: string;
    }>('/strategy/plan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  planArchitecture(body: ArchitecturePlanRequestDto) {
    return request<{
      plan: any;
      route: any;
      blueprintValid: boolean;
      blueprint: any;
      analyzedAtUtc: string;
    }>('/architecture/plan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }, { surface: 'builder' });
  }

  getTemplates() {
    return request<any[]>('/templates', { method: 'GET' }, { surface: 'builder' });
  }

  getEvolutionStatus() {
    return request<any>('/evolution/status', { method: 'GET' }, { surface: 'evolution' });
  }

  proposeEvolutionMutation() {
    return request<{
      success: boolean;
      status: any;
      mutation?: any;
    }>('/evolution/mutation/propose', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({}),
    }, { surface: 'evolution' });
  }

  getEvolutionLibrary() {
    return request<any[]>('/evolution/library', { method: 'GET' }, { surface: 'evolution' });
  }

  challengeEvolution(proposal: string) {
    return request<any>('/evolution/challenge', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ proposal }),
    }, { surface: 'evolution' });
  }

  evolutionAction(action: 'start' | 'pause' | 'stop' | 'reset', body?: { targetPath?: string; targetDomain?: string }) {
    return request<{ success: boolean }>(`/evolution/${action}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body ?? {}),
    }, { surface: 'evolution' });
  }

  indexingAction(action: 'start' | 'pause' | 'reset', body?: { targetPath?: string; targetDomain?: string }) {
    return request<{ success: boolean }>(`/indexing/${action}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body ?? {}),
    }, { surface: 'evolution' });
  }
}

export const helperApi = new HelperApiClient();
