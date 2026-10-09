# Topology drift

`06c479a3` (2026-08-31 06:31 UTC) → `2c1c6bd0` (2026-10-09 10:54 UTC)

| Metric | Prev | Curr | Δ |
|---|---:|---:|---:|
| Tracked files | 3005 | 3088 | 83 |
| Project edges | 63 | 63 | 0 |
| File-graph edges | 8210 | 8437 | 227 |

## Added files (named buckets only)
- `CLAUDE.md`
- `scripts/check_selfhosted_lane_order.ps1`
- `scripts/ci/ephemeral_runner_loop.ps1`
- `scripts/common/TraceabilityGate.ps1`
- `scripts/config/flag_consolidation_exclusions.json`
- `scripts/config/prod_config_suite_baseline.json`
- `scripts/development/build_development_delta.ps1`
- `scripts/evolution/build_evolution_proposals.ps1`
- `scripts/governance/run_suite_prod_config.ps1`
- `scripts/knowledge/build_critic_digest.ps1`
- `scripts/knowledge/build_knowledge_gap_queue.ps1`
- `scripts/knowledge/build_real_traffic_drift.ps1`
- `scripts/knowledge/run_knowledge_gap_weekly.ps1`
- `scripts/ops/prod_supervisor.ps1`
- `scripts/stop_workspace_processes.ps1`
- `src/Helper.Api/Conversation/Analysis/RepoMetadataFabricationGuard.cs`
- `src/Helper.Api/Conversation/KnowledgeGapLedger.cs`
- `src/Helper.Api/Conversation/PlainCitationRenderPolicy.cs`
- `src/Helper.Api/Conversation/ShipGateBaselineFallthrough.cs`
- `src/Helper.Api/Conversation/TerminalDisplayPasses.cs`
- `src/Helper.Api/Conversation/UnderspecifiedStarterDirective.cs`
- `src/Helper.Api/Hosting/AtomicFileReplace.cs`
- `src/Helper.Api/Hosting/ModelPlaneResyncPolicy.cs`
- `src/Helper.Api/Hosting/RequestBudgetPolicy.cs`
- `src/Helper.Runtime.Core/EstablishmentRecommendationCues.cs`
- `src/Helper.Runtime.Core/EvidenceRequestCues.cs`
- `src/Helper.Runtime.Core/UnderspecifiedPromptCues.cs`
- `src/Helper.Runtime/BaselineKnowledgePrompt.cs`
- `src/Helper.Runtime/Generation/MetricsArtifactPathPolicy.cs`
- `src/Helper.Runtime/ProseCoherencePolicy.cs`
- `src/Helper.Runtime/RenderedLinkLivenessPolicy.cs`
- `src/Helper.Runtime/SourcesFooterOnTopicRenderPolicy.cs`
- `src/Helper.Runtime/SynthesisInputAnchorFilter.cs`
- `src/Helper.Runtime/VersionSensitiveHedgePolicy.cs`
- `test/Helper.Runtime.Tests/ArchitectureFitnessTests.ResponseStateMachine.cs`
- `test/Helper.Runtime.Tests/AtomicFileReplaceTests.cs`
- `test/Helper.Runtime.Tests/BlindRound2LeverTests.cs`
- `test/Helper.Runtime.Tests/ClarifyBlockingQuestionTests.cs`
- `test/Helper.Runtime.Tests/EstablishmentRecommendationCuesTests.cs`
- `test/Helper.Runtime.Tests/HumanLevelRenderWaveTests.cs`
- … and 20 more

## Module file-count shifts
| Module | Δ files |
|---|---:|
| `test/Helper.Runtime.Tests` | +24 |
| `scripts` | +14 |
| `doc` | +13 |
| `eval` | +10 |
| `src/Helper.Api` | +9 |
| `src/Helper.Runtime` | +7 |
| `src/Helper.Runtime.Core` | +3 |
| `(root)` | +2 |
| `test` | +1 |
