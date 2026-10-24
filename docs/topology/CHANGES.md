# Topology drift

`06c479a3` (2026-08-31 06:31 UTC) → `d9954c60` (2026-10-24 06:31 UTC)

| Metric | Prev | Curr | Δ |
|---|---:|---:|---:|
| Tracked files | 3005 | 3109 | 104 |
| Project edges | 63 | 63 | 0 |
| File-graph edges | 8210 | 8513 | 303 |

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
- `scripts/sanitize_section_noise_payload.ps1`
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
- `src/Helper.Runtime.Core/AdSourceCues.cs`
- `src/Helper.Runtime.Core/EstablishmentRecommendationCues.cs`
- `src/Helper.Runtime.Core/EvidenceRequestCues.cs`
- `src/Helper.Runtime.Core/LocatorWhitespaceSanitizer.cs`
- `src/Helper.Runtime.Core/SectionNoiseSanitizer.cs`
- `src/Helper.Runtime.Core/UnderspecifiedPromptCues.cs`
- `src/Helper.Runtime/AbsoluteGuaranteeQualifierPolicy.cs`
- `src/Helper.Runtime/BaselineKnowledgePrompt.cs`
- `src/Helper.Runtime/DeterministicClarifyAskPolicy.cs`
- `src/Helper.Runtime/Generation/MetricsArtifactPathPolicy.cs`
- `src/Helper.Runtime/ProseCoherencePolicy.cs`
- `src/Helper.Runtime/RenderedLinkLivenessPolicy.cs`
- `src/Helper.Runtime/RenderStubSweepPolicy.cs`
- `src/Helper.Runtime/ResearchFragmentBodyGuard.cs`
- `src/Helper.Runtime/SourcesFooterOnTopicRenderPolicy.cs`
- … and 40 more

## Module file-count shifts
| Module | Δ files |
|---|---:|
| `test/Helper.Runtime.Tests` | +35 |
| `scripts` | +15 |
| `doc` | +14 |
| `src/Helper.Runtime` | +12 |
| `eval` | +10 |
| `src/Helper.Api` | +9 |
| `src/Helper.Runtime.Core` | +6 |
| `(root)` | +2 |
| `test` | +1 |
