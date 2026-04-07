// --- Helper Core Types ---

export interface ThoughtNode {
  content: string;
  score: number;
  children: ThoughtNode[];
}

export interface ResearchResult {
  summary: string;
  sources: string[];
  keyFindings: string[];
  fullReport: string;
}

export type LiveWebMode = 'auto' | 'force_search' | 'no_web';
export type ConversationInputMode = 'text' | 'voice';

export interface SearchTraceSource {
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

export interface SearchTrace {
  requestedMode: LiveWebMode;
  resolvedRequirement: 'no_web_needed' | 'web_helpful' | 'web_required' | string;
  reason?: string;
  status: string;
  signals?: string[];
  events?: string[];
  sources?: SearchTraceSource[];
  inputMode?: ConversationInputMode;
}

export interface Message {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: number;
  turnId?: string;
  turnVersion?: number;
  branchId?: string;
  confidence?: number;
  sources?: string[];
  toolCalls?: string[];
  requiresConfirmation?: boolean;
  nextStep?: string;
  groundingStatus?: string;
  citationCoverage?: number;
  verifiedClaims?: number;
  totalClaims?: number;
  claimGroundings?: ClaimGrounding[];
  uncertaintyFlags?: string[];
  executionMode?: string;
  budgetProfile?: string;
  budgetExceeded?: boolean;
  estimatedTokensGenerated?: number;
  attachments?: ChatAttachment[];
  availableBranches?: string[];
  rating?: number;
  thoughtTree?: ThoughtNode;
  generatedFiles?: string[];
  researchResult?: ResearchResult; // Added
  diagnosticsDeck?: MessageDiagnosticsDeck;
  searchTrace?: SearchTrace;
  inputMode?: ConversationInputMode;
}

export interface ClaimGrounding {
  claim: string;
  type: string;
  sourceIndex?: number;
  evidenceGrade: string;
}

export type MessageDiagnosticsTone = 'neutral' | 'info' | 'positive' | 'warning' | 'critical';

export interface MessageDiagnosticsItem {
  label: string;
  value: string;
  tone?: MessageDiagnosticsTone;
  mono?: boolean;
}

export interface MessageDiagnosticsSection {
  id: string;
  title: string;
  layout?: 'grid' | 'list';
  items: MessageDiagnosticsItem[];
}

export interface MessageDiagnosticsDeck {
  title: string;
  badges: MessageDiagnosticsItem[];
  sections: MessageDiagnosticsSection[];
}

export interface MutationProposal {
  id: string;
  filePath: string;
  originalCode: string;
  proposedCode: string;
  reason?: string;
  timestamp?: string;
}

export interface ProgressLogEntry {
  id: string;
  message: string;
  timestamp: number;
}

export interface ThoughtStreamEntry {
  id: string;
  content: string;
  timestamp: number;
  type?: string;
}

export interface ChatAttachment {
  id: string;
  type: string;
  name: string;
  sizeBytes: number;
  referenceUri?: string;
}

export type AppTabKey =
  | 'orchestrator'
  | 'runtime'
  | 'strategy'
  | 'objectives'
  | 'planner'
  | 'evolution'
  | 'indexing'
  | 'builder'
  | 'settings';

export interface RagDocument {
  id: string;
  title: string;
  content: string;
  timestamp: number;
  tags: string[];
}

export interface AppConfig {
  aiUrl: string;
  useInternet: boolean;
  contextWindow: number;
}

export enum DeploymentPlatform {
  CLI = 'Console App',
  WPF = 'Windows (WPF)',
  WEB = 'Web API (ASP.NET)',
  MACOS = 'macOS',
  MOBILE = 'Cross-platform Mobile'
}

// --- Project / Generator Types ---

export interface VirtualFile {
  name: string;
  path: string; // Added: FULL relative path
  content: string;
  language: 'typescript' | 'python' | 'swift' | 'kotlin' | 'json' | 'xml' | 'xaml' | 'text' | 'csharp' | 'markdown';
}

export interface VirtualFolder {
  name: string;
  path: string;
  files: VirtualFile[];
  folders: VirtualFolder[];
}

export interface GeneratedProject {
  id: string;
  name: string;
  fullPath: string; // Added: Real OS path
  targetPlatform: DeploymentPlatform;
  launchContext?: BuilderLaunchRequest | null;
  root: VirtualFolder;
  status: 'draft' | 'building' | 'compiled' | 'error';
  lastBuildTime?: number;
}

export interface BuilderActivityEntry {
  id: string;
  kind: 'workspace' | 'file' | 'build' | 'mutation' | 'structure';
  summary: string;
  detail?: string;
  timestamp: number;
  relatedPath?: string;
  tone?: 'neutral' | 'success' | 'warning' | 'danger';
}

export interface E2ETestResult {
  id: string;
  testName: string;
  status: 'pass' | 'fail' | 'running' | 'pending';
  logs: string[];
}

export interface Goal {
  id: string;
  title: string;
  description: string;
  isCompleted: boolean;
  createdAt: string;
}

export interface StrategyBranch {
  id: string;
  description: string;
  confidenceScore: number;
  risks: string[];
  suggestedTools?: string[];
}

export interface StrategicPlan {
  selectedStrategyId: string;
  options: StrategyBranch[];
  reasoning: string;
  requiresMoreInfo: boolean;
  clarifyingQuestions?: string[];
}

export interface TemplateRoutingDecision {
  matched: boolean;
  templateId?: string | null;
  confidence: number;
  candidates: string[];
  reason: string;
}

export interface TemplateCatalogItem {
  id: string;
  name: string;
  description: string;
  language: string;
  version?: string | null;
  deprecated?: boolean;
  tags?: string[];
  rootPath?: string;
}

export interface PlannedFileTask {
  path: string;
  purpose: string;
  dependencies: string[];
  technicalContract?: string | null;
}

export interface ProjectPlanDraft {
  description: string;
  plannedFiles: PlannedFileTask[];
}

export interface BlueprintFileSummary {
  path: string;
  purpose?: string;
  description?: string;
  language?: string;
  role?: string;
  methodCount?: number;
}

export interface BlueprintSummary {
  name: string;
  targetOs: string;
  nuGetPackages: string[];
  architectureReasoning: string;
  files: BlueprintFileSummary[];
}

export interface StrategicMapAnalysis {
  plan: StrategicPlan;
  activeGoals: Goal[];
  route: TemplateRoutingDecision;
  analyzedAtUtc: string;
}

export interface ArchitecturePlanAnalysis {
  plan: ProjectPlanDraft;
  route: TemplateRoutingDecision;
  blueprintValid: boolean;
  blueprint: BlueprintSummary;
  analyzedAtUtc: string;
}

export interface ArchitecturePlannerSeed {
  id: string;
  source: 'strategy';
  prompt: string;
  targetOs: 'Windows' | 'Linux' | 'MacOS';
  strategyAnalysis: StrategicMapAnalysis;
}

export interface BuilderLaunchRequest {
  id: string;
  source: 'planner';
  prompt: string;
  targetPlatform: DeploymentPlatform;
  routeTemplateId?: string | null;
  planSummary?: string;
  blueprintName?: string;
}

export interface BuilderWorkspaceSelection {
  kind: 'file' | 'folder';
  path: string;
  label: string;
}

export type PlannerTargetOs = ArchitecturePlannerSeed['targetOs'];

export interface EvolutionLibraryItem {
  path: string;
  name: string;
  folder: string;
  status: string;
}

export interface EvolutionStatus {
  isLearning: boolean;
  isIndexing: boolean;
  currentPhase: string;
  activeTask: string;
  processedFiles: number;
  totalFiles: number;
  fileProgress: number;
  recentLearnings?: string[];
}
