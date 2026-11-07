# Topology drift

`d9954c60` (2026-10-24 06:31 UTC) → `c15ea239` (2026-11-07 13:45 UTC)

| Metric | Prev | Curr | Δ |
|---|---:|---:|---:|
| Tracked files | 3109 | 3167 | 58 |
| Project edges | 63 | 63 | 0 |
| File-graph edges | 8513 | 8708 | 195 |

## Added files (named buckets only)
- `scripts/check_scc_budget.ps1`
- `src/Helper.Api/Conversation/Audit/PostTurnAuditCritiqueSanityPolicy.cs`
- `src/Helper.Api/Conversation/Audit/PostTurnAuditFailureClassifier.cs`
- `src/Helper.Api/Conversation/Audit/PostTurnAuditLivenessGate.cs`
- `src/Helper.Api/Conversation/Audit/PostTurnAuditQueue.cs`
- `src/Helper.Api/Conversation/Audit/PostTurnAuditWorker.cs`
- `src/Helper.Api/Conversation/Audit/PostTurnDeterministicAuditFallbackPolicy.cs`
- `src/Helper.Api/Conversation/Benchmark/BenchmarkResponseAssessmentWriter.cs`
- `src/Helper.Api/Conversation/Benchmark/BenchmarkResponseFormatter.cs`
- `src/Helper.Api/Conversation/Benchmark/BenchmarkResponseFormatterFactory.cs`
- `src/Helper.Api/Conversation/Benchmark/BenchmarkResponseSectionRenderer.cs`
- `src/Helper.Api/Conversation/Benchmark/BenchmarkResponseStructurePolicy.cs`
- `src/Helper.Api/Conversation/Benchmark/BenchmarkTopicalBodyExtractor.cs`
- `src/Helper.Api/Conversation/Benchmark/EvalCorpusCoverageContracts.cs`
- `src/Helper.Api/Conversation/Benchmark/EvalCorpusCoverageReportService.cs`
- `src/Helper.Api/Conversation/Benchmark/EvalCorpusCoverageReportWriter.cs`
- `src/Helper.Api/Conversation/Benchmark/EvalCorpusCoverageRubricLoader.cs`
- `src/Helper.Api/Conversation/Benchmark/EvalQualityScorer.cs`
- `src/Helper.Api/Conversation/Benchmark/EvalRunnerV2.cs`
- `src/Helper.Api/Conversation/Benchmark/LocalFirstBenchmarkPolicy.cs`
- `src/Helper.Api/Conversation/Benchmark/LocalLibraryLearningEvalCaseGenerator.cs`
- `src/Helper.Api/Conversation/Benchmark/ReasoningAwareRetrievalPolicy.cs`
- `src/Helper.Api/Conversation/Benchmark/RetrievalChunkTopicalFitInspector.cs`
- `src/Helper.Api/Conversation/Benchmark/RetrievalSourceDiversityInspector.cs`
- `src/Helper.Api/Conversation/Clarify/AmbiguityDetection.cs`
- `src/Helper.Api/Conversation/Clarify/ClarificationQualityContracts.cs`
- `src/Helper.Api/Conversation/Clarify/ClarificationQualityPolicy.cs`
- `src/Helper.Api/Conversation/Clarify/ClarificationReplyResolver.cs`
- `src/Helper.Api/Conversation/Clarify/PendingClarificationTurnApplier.cs`
- `src/Helper.Api/Conversation/Grounding/BenchmarkComposedResponseGroundingRefresher.cs`
- `src/Helper.Api/Conversation/Grounding/BenchmarkEvidenceFallbackSummaryBuilder.cs`
- `src/Helper.Api/Conversation/Grounding/CitationGroundingService.cs`
- `src/Helper.Api/Conversation/Grounding/CitationLabelNormalizer.cs`
- `src/Helper.Api/Conversation/Grounding/CitationLineageStore.cs`
- `src/Helper.Api/Conversation/Grounding/CitationProjectionService.cs`
- `src/Helper.Api/Conversation/Grounding/CitationQuotePolicy.cs`
- `src/Helper.Api/Conversation/Grounding/ClaimAbstractionMetric.cs`
- `src/Helper.Api/Conversation/Grounding/ClaimExtractionService.cs`
- `src/Helper.Api/Conversation/Grounding/ClaimGroundingVerificationPolicy.cs`
- `src/Helper.Api/Conversation/Grounding/ClaimSourceMatcher.cs`
- … and 217 more

## Removed files
- `src/Helper.Api/Conversation/AcceptedAnswerArchiveStore.cs`
- `src/Helper.Api/Conversation/AcceptedAnswerLearningContracts.cs`
- `src/Helper.Api/Conversation/AcceptedAnswerLearningStore.cs`
- `src/Helper.Api/Conversation/AcceptedAnswerPathRedactor.cs`
- `src/Helper.Api/Conversation/AcceptedAnswerPreferenceExampleBuilder.cs`
- `src/Helper.Api/Conversation/AmbiguityDetection.cs`
- `src/Helper.Api/Conversation/Analysis/JsonObjectExtractor.cs`
- `src/Helper.Api/Conversation/AnalysisConfidenceOutcomeStore.cs`
- `src/Helper.Api/Conversation/AnswerShapePolicy.cs`
- `src/Helper.Api/Conversation/AssistantUserFacingTextPolicy.cs`
- `src/Helper.Api/Conversation/BenchmarkComposedResponseGroundingRefresher.cs`
- `src/Helper.Api/Conversation/BenchmarkDraftQualityPolicy.cs`
- `src/Helper.Api/Conversation/BenchmarkEvidenceFallbackSummaryBuilder.cs`
- `src/Helper.Api/Conversation/BenchmarkReportComposer.cs`
- `src/Helper.Api/Conversation/BenchmarkResponseAssessmentWriter.cs`
- `src/Helper.Api/Conversation/BenchmarkResponseFormatter.cs`
- `src/Helper.Api/Conversation/BenchmarkResponseFormatterFactory.cs`
- `src/Helper.Api/Conversation/BenchmarkResponseSectionRenderer.cs`
- `src/Helper.Api/Conversation/BenchmarkResponseStructurePolicy.cs`
- `src/Helper.Api/Conversation/BenchmarkTopicalBodyExtractor.cs`
- `src/Helper.Api/Conversation/BlockedResponseMessagePolicy.cs`
- `src/Helper.Api/Conversation/CasualDialogueIntentPolicy.cs`
- `src/Helper.Api/Conversation/CasualDialogueResponseRenderer.cs`
- `src/Helper.Api/Conversation/ChatPromptFormatter.cs`
- `src/Helper.Api/Conversation/ChatStreamResumeHelper.cs`
- `src/Helper.Api/Conversation/ChatTurnAnswerService.cs`
- `src/Helper.Api/Conversation/ChatTurnCouncilLane.cs`
- `src/Helper.Api/Conversation/ChatTurnCritic.cs`
- `src/Helper.Api/Conversation/ChatTurnExecutionSupport.cs`
- `src/Helper.Api/Conversation/ChatTurnExecutor.cs`
- `src/Helper.Api/Conversation/ChatTurnExecutorDependencyFactory.cs`
- `src/Helper.Api/Conversation/ChatTurnFinalizer.cs`
- `src/Helper.Api/Conversation/ChatTurnImmediateService.cs`
- `src/Helper.Api/Conversation/ChatTurnPlanner.cs`
- `src/Helper.Api/Conversation/CitationGroundingService.cs`
- `src/Helper.Api/Conversation/CitationLabelNormalizer.cs`
- `src/Helper.Api/Conversation/CitationLineageStore.cs`
- `src/Helper.Api/Conversation/CitationProjectionService.cs`
- `src/Helper.Api/Conversation/CitationQuotePolicy.cs`
- `src/Helper.Api/Conversation/ClaimAbstractionMetric.cs`

## Module file-count shifts
| Module | Δ files |
|---|---:|
| `test/Helper.Runtime.Tests` | +27 |
| `src/Helper.Runtime.Core` | +15 |
| `src/Helper.Runtime` | +5 |
| `doc` | +4 |
| `src/Helper.Api` | +2 |
| `src/Helper.Runtime.Knowledge` | +1 |
| `test` | +1 |
| `scripts` | +1 |
| `src/Helper.RuntimeLogSemantics` | +1 |
| `eval` | +1 |
