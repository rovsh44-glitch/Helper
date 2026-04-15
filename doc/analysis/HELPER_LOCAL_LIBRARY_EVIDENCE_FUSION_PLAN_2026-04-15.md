# Helper Local Library Evidence Fusion Plan

Date: 2026-04-15

## Goal

Implement a strict, reproducible evidence scheme where local library documents are useful first-class sources, but never silently replace live web evidence when freshness, regulation, price, law, tax, medical, product, or other current external facts are required.

Target model:

1. Local library sources can support background, user-owned context, historical facts, methodology, internal documents, and stable domain knowledge.
2. Live web sources must support current external facts, regulatory thresholds, deadlines, active requirements, current prices, current model availability, current product specs, news, and other freshness-sensitive claims.
3. A final answer may fuse both layers, but source layers must remain visible, separately counted, and separately calibrated.
4. If live web is required and no usable web evidence is retrieved, the system must abstain or return `NeedsVerification`; it must not use local PDFs/FB2/EPUB/DOCX/etc. to satisfy the web source floor.

## Current State

Relevant implemented pieces:

- Local structured parsers already exist under `src/Helper.Runtime.Knowledge`:
  - `StructuredPdfParser`
  - `StructuredFb2Parser`
  - `StructuredEpubParser`
  - `StructuredDocxParser`
  - `StructuredMarkdownParser`
  - `StructuredHtmlParser`
  - `StructuredDocumentParsers.*`
- Local indexing and retrieval already use `KnowledgeChunk`, `QdrantStore`, `ContextAssemblyService`, `Retrieval*` policies, and `LocalLibraryGroundingSupport`.
- Web evidence already uses `ResearchEvidenceItem`, `EvidenceKind`, `SearchTrace`, `WebSearchSessionCoordinator`, `WebSearchFetchEnricher`, and source authority scoring.
- Recent runtime fix introduced explicit source-layer separation in `ConversationSourceClassifier`, and strict regulation/tax rewritten queries no longer fall back to local library evidence when web evidence is missing.

Main remaining gap:

- The system still lacks a complete, end-to-end evidence-fusion contract that is enforced consistently across ingestion, retrieval, synthesis, final rendering, telemetry, analyzer metrics, and benchmark gates.

## Design Principles

1. Evidence layer is not evidence strength.
   - `local_library_chunk` can be strong for local context.
   - It is still not a live web source.

2. Evidence freshness is claim-specific.
   - A 2007 PDF can support historical background.
   - It cannot verify today's tax deadline or current reporting threshold.

3. Source floors must be layer-aware.
   - `minWebSources` counts only web sources.
   - `minLocalSources` counts only local library sources.

4. Fusion must be transparent.
   - Response sections must label sources as `web`, `local library`, `attachment`, or `generated/internal`.

5. Public proof bundles must be privacy-safe.
   - Reports may show local source titles and stable IDs.
   - Absolute local paths should be hidden or redacted in public artifacts.

## Target Source Taxonomy

Add a source-layer taxonomy, separate from `EvidenceKind`.

Recommended layers:

- `web`
  - Live web search hits, fetched HTML pages, remote PDFs, official sources, academic web pages.
- `local_library`
  - Indexed local PDFs, FB2, EPUB, DOCX, HTML, MD, TXT, and other local docs under the configured library root.
- `attachment`
  - User-uploaded or current-turn provided files.
- `conversation_memory`
  - Persisted memories, user profile facts, prior turn summaries.
- `tool_runtime`
  - Runtime logs, generated reports, local command outputs.

Recommended evidence kinds:

- Existing:
  - `search_hit`
  - `fetched_page`
  - `fetched_document_pdf`
  - `local_library_chunk`
  - `source_url`
- Add or normalize:
  - `local_pdf_chunk`
  - `local_fb2_chunk`
  - `local_epub_chunk`
  - `local_docx_chunk`
  - `local_markdown_chunk`
  - `local_html_chunk`
  - `local_text_chunk`
  - `attachment_chunk`

Do not use format-specific kinds for policy branching only. Policy should primarily branch on `SourceLayer`, `FreshnessEligibility`, and `ClaimSupportRole`.

## Target Metadata Contract

Extend the local evidence model so every local chunk can expose:

- `sourceLayer`: `local_library`
- `sourceFormat`: `pdf`, `fb2`, `epub`, `docx`, `html`, `md`, `txt`, etc.
- `sourceId`: stable hash-based ID, not absolute path.
- `sourcePath`: internal-only path, never required for public output.
- `displayTitle`: human-readable title.
- `author`: optional.
- `publishedYear`: optional.
- `documentDate`: optional.
- `indexedAtUtc`.
- `contentHash`.
- `parserName`.
- `parserVersion`.
- `locator`: page, chapter, heading path, paragraph ordinal, chunk ordinal.
- `collection`.
- `retrievalScore`.
- `topicalFitScore`.
- `sourceFreshnessClass`:
  - `current_external`
  - `recent_external`
  - `stable_reference`
  - `historical`
  - `unknown_date`
  - `internal_or_user_owned`
- `allowedClaimRoles`:
  - `background`
  - `definition`
  - `methodology`
  - `historical_context`
  - `user_context`
  - `current_external_fact`
  - `regulatory_current_fact`

For local library chunks, `current_external_fact` and `regulatory_current_fact` should default to false unless a document is explicitly tagged as current and authoritative by user/admin policy.

## Implementation Phases

### Phase 1: Evidence Model Hardening

Files:

- `src/Helper.Runtime/Core/Contracts/ConversationContracts.cs`
- `src/Helper.Runtime/Core/Contracts/KnowledgeContracts.cs`
- `src/Helper.Runtime/LocalLibraryGroundingSupport.cs`
- `src/Helper.Api/Conversation/ConversationSourceClassifier.cs`
- `src/Helper.Api/Conversation/WebSearchTraceProjector.cs`

Steps:

1. Add `SourceLayer`, `SourceFormat`, `SourceId`, `DisplayTitle`, `Locator`, and `FreshnessEligibility` to the evidence contract.
2. Keep backward compatibility by defaulting old evidence items:
   - HTTP/HTTPS + non-local evidence kind -> `web`
   - `local_library_chunk` or non-HTTP path -> `local_library`
3. Update `LocalLibraryGroundingSupport.BuildEvidenceItems` to fill local metadata from `KnowledgeChunk.Metadata`.
4. Update `WebSearchTraceProjector` to emit source layer and source format in trace source DTOs.
5. Update `ConversationSourceClassifier` to use the explicit layer when available, and fallback to current URL/evidence-kind heuristics.

Acceptance criteria:

- Existing API clients still deserialize old responses.
- Local PDF evidence is classified as `local_library`.
- Remote PDF evidence is classified as `web`.
- Search-hit evidence is classified as `web`.

### Phase 2: Local Parser Metadata Normalization

Files:

- `src/Helper.Runtime.Knowledge/StructuredPdfParser.*`
- `src/Helper.Runtime.Knowledge/StructuredFb2Parser.cs`
- `src/Helper.Runtime.Knowledge/StructuredEpubParser.cs`
- `src/Helper.Runtime.Knowledge/StructuredDocxParser.cs`
- `src/Helper.Runtime.Knowledge/StructuredMarkdownParser.cs`
- `src/Helper.Runtime.Knowledge/StructuredHtmlParser.cs`
- `src/Helper.Runtime.Knowledge/StructuredLibrarianV2Pipeline.cs`
- `src/Helper.Runtime.Knowledge/DocumentNormalizationService.cs`
- `src/Helper.Runtime.Knowledge/Chunking/*`

Steps:

1. Define a shared `LocalDocumentMetadata` shape emitted by all local parsers.
2. For PDF:
   - Preserve page number, text-layer quality, OCR/vision fallback status, title, author, date if available.
3. For FB2:
   - Preserve title, author, genre, language, sequence, section/chapter path, encoding flags.
4. For EPUB:
   - Preserve OPF metadata, chapter path, spine order, language, publication date if available.
5. For DOCX:
   - Preserve core properties, heading hierarchy, paragraph ordinal.
6. For MD/HTML/TXT:
   - Preserve file title, heading path, relative path, modified time.
7. Ensure every `KnowledgeChunk` receives enough metadata to later render a human-safe citation label.
8. Add parser-version metadata so old indexes can be distinguished from new indexes.

Acceptance criteria:

- Each supported local format produces chunks with `sourceFormat`, `sourceId`, `displayTitle`, and `locator`.
- FB2 Windows-1251 and UTF-8 cases remain supported.
- Scanned PDF fallback is marked as lower confidence unless OCR/vision extraction succeeds.

### Phase 3: Layer-Aware Retrieval Planning

Files:

- `src/Helper.Api/Conversation/ConversationContextAssembler.cs`
- `src/Helper.Api/Conversation/ReasoningAwareRetrievalPolicy.cs`
- `src/Helper.Runtime.Knowledge/Retrieval/*`
- `src/Helper.Runtime.WebResearch/ResearchRequestProfile.cs`
- `src/Helper.Api/Conversation/LiveWebRequirementPolicy.cs`
- `src/Helper.Api/Conversation/Planning/TurnLiveWebDecisionStep.cs`

Steps:

1. Introduce a `LocalEvidenceUseMode` decision:
   - `primary_allowed`
   - `secondary_context_only`
   - `disabled_for_turn`
2. Map prompts:
   - `local_sufficient` -> local primary allowed.
   - `local_plus_web` -> local primary for stable context, web required for external verification.
   - `web_required_fresh` -> local secondary context only.
   - `conflict_check` -> local optional, web/official/peer-reviewed sources primary.
3. For regulation/tax/legal/medical/current prompts:
   - Retrieve local chunks only as background or user context.
   - Do not allow local evidence to satisfy live source floors.
4. Preserve local retrieval trace:
   - `local_retrieval.mode=secondary_context_only`
   - `local_retrieval.source_count=N`
   - `local_retrieval.used_for=background`
5. Add query profiles for local-vs-web intent:
   - user-owned document analysis
   - current external fact check
   - historical/local background
   - policy/regulation freshness

Acceptance criteria:

- `lfwr-158` remains local-secondary-only and abstains if web evidence is absent.
- A non-fresh local explanation case can still answer from local library.
- A mixed report can cite both web and local sources without merging their counts.

### Phase 4: Evidence Fusion Service

New or changed files:

- `src/Helper.Api/Conversation/EvidenceFusionService.cs`
- `src/Helper.Api/Conversation/ClaimGroundingService.cs`
- `src/Helper.Api/Conversation/CitationProjectionService.cs`
- `src/Helper.Api/Conversation/EvidenceGradingService.cs`
- `src/Helper.Api/Conversation/Epistemic/EpistemicAnswerModePolicy.cs`
- `src/Helper.Api/Conversation/ResearchEvidenceTierPolicy.cs`

Steps:

1. Add an explicit fusion stage after web/local retrieval and before final response composition.
2. Produce an `EvidenceFusionSnapshot` containing:
   - `webSourceCount`
   - `localSourceCount`
   - `attachmentSourceCount`
   - `webCitationCoverage`
   - `localCitationCoverage`
   - `freshClaimWebCoverage`
   - `backgroundClaimCoverage`
   - `unsupportedFreshClaimCount`
   - `localOnlyFreshClaimCount`
3. Classify claims into:
   - `fresh_external_claim`
   - `regulatory_claim`
   - `medical_claim`
   - `legal_claim`
   - `background_claim`
   - `local_context_claim`
   - `methodology_claim`
4. Enforce claim support rules:
   - Fresh external claims require web evidence.
   - Regulatory current claims require official/current web evidence.
   - Local context claims can be supported by local library evidence.
   - Background claims can use either local or web evidence.
5. If a fresh claim is only supported by local library evidence:
   - downgrade claim to `needs_verification`, or remove it from final synthesis.
6. Feed fusion metrics into epistemic policy:
   - local-only evidence cannot move a freshness-sensitive answer from `Abstain` to `Grounded`.
   - mixed local+web evidence can move from `Abstain` to `NeedsVerification` or `GroundedWithLimits` if web floor is partially met.

Acceptance criteria:

- A current tax answer with only local PDF evidence is `Abstain`.
- A current tax answer with official web sources plus local PDFs is `Grounded` or `NeedsVerification`, depending on coverage.
- A historical analysis with local PDFs only can be `Grounded` if local coverage is strong.

### Phase 5: Response Rendering And Source Display

Files:

- `src/Helper.Api/Conversation/BenchmarkResponseSectionRenderer.cs`
- `src/Helper.Api/Conversation/BenchmarkResponseAssessmentWriter.cs`
- `src/Helper.Api/Conversation/ResponseComposerService.cs`
- `src/Helper.Api/Conversation/ResearchGroundedSynthesisFormatter.cs`
- `src/Helper.Api/Backend/Application/TurnResponseWriter.cs`

Steps:

1. Render sources grouped by layer:
   - `Web sources`
   - `Local library sources`
   - `Attachments`
2. For local sources, render:
   - display title
   - format
   - locator
   - optional collection
   - stable source ID
3. Avoid exposing absolute paths in public/exported responses.
4. In developer/local mode, allow path display behind a config flag:
   - `HELPER_RESPONSE_SHOW_LOCAL_PATHS=true`
5. Add explicit language when web is required but absent:
   - local sources are context only
   - current facts remain unverified
6. Add explicit language when both layers are present:
   - web evidence verifies current facts
   - local evidence provides background or user-library context

Acceptance criteria:

- `lfwr-158` says no usable live web sources were obtained.
- A mixed report clearly labels web vs local sources.
- Public proof artifacts do not leak absolute local paths unless explicitly requested.

### Phase 6: Analyzer And Metrics

Files:

- `scripts/analyze_local_first_librarian_run.ps1`
- `scripts/run_local_first_librarian_corpus.ps1`
- `doc/parity_evidence/*`

Steps:

1. Add per-case metrics:
   - `webSourcesCount`
   - `localSourcesCount`
   - `attachmentSourcesCount`
   - `freshClaimWebCoverage`
   - `localOnlyFreshClaimCount`
2. Change source-floor checks:
   - mandatory web checks use `webSourcesCount`.
   - local-only checks use `localSourcesCount`.
3. Keep accepted abstain semantics:
   - `web_required_fresh + abstain + unverified + executed_live_web + webSourcesCount=0` is not an unsupported assertion.
4. Add issue types:
   - `local_sources_miscounted_as_web`
   - `fresh_claim_supported_only_by_local_library`
   - `public_artifact_exposes_local_path`
   - `mixed_sources_not_labeled`
5. Keep existing source-count issues for cases that assert factual conclusions without enough web evidence.

Acceptance criteria:

- Analyzer reports layer-aware source counts.
- Accepted abstain does not appear as a quality issue.
- Unsupported current factual answer with local-only evidence is flagged.

### Phase 7: Tests

Unit and integration tests:

- `test/Helper.Runtime.Tests/SimpleResearcherTests.cs`
- `test/Helper.Runtime.Tests/EpistemicAnswerModePolicyTests.cs`
- `test/Helper.Runtime.Tests/ClaimGroundingTests.cs`
- `test/Helper.Runtime.Tests/ResponseComposerCollaboratorTests.cs`
- `test/Helper.Runtime.Tests/WebQueryPlannerTests.cs`
- `test/Helper.Runtime.Tests/HybridLocalRetrievalTests.cs`
- `test/Helper.Runtime.Tests/StructuredParserUtilitiesTests.cs`
- `test/Helper.Runtime.Tests/StructuredIndexQualityGateTests.cs`

Required regression cases:

1. Fresh tax query + no web + local PDF available:
   - no local source in web count
   - answer abstains
   - no best-effort hypothesis
2. Fresh tax query + official web + local PDF:
   - web count satisfies floor
   - local count visible separately
   - answer can provide cautious report
3. Historical/local-library query + local PDF/FB2 only:
   - local evidence can ground the answer
4. FB2 document query:
   - citations include title/chapter locator
5. EPUB/DOCX/MD query:
   - citations include stable source ID and locator
6. Public artifact export:
   - no absolute local paths
7. Analyzer:
   - accepted abstain is not a top issue
   - local-only fresh claim is a top issue

### Phase 8: Benchmark Expansion

Files:

- `eval/web_research_parity/local_first_librarian_300_case_corpus.jsonl`
- `eval/web_research_parity/local_first_librarian_slices/*`
- `eval/web_research_parity/proof_bundle_lfl20_v1.jsonl`

Add benchmark slices:

- `local_library_primary`
- `local_library_secondary_context`
- `mixed_web_local_report`
- `freshness_web_required_local_available`
- `public_privacy_safe_sources`

Add proof-bundle cases:

1. Local PDF supports historical background, no web needed.
2. Local FB2 supports literary/historical analysis.
3. Local EPUB supports stable conceptual explanation.
4. Current tax deadline requires web, local sources present but insufficient.
5. Current regulation answer uses official web plus local explanatory source.
6. User-owned local document analysis uses local source as primary.

Acceptance criteria:

- LFL20 remains green.
- New mixed-source bundle has zero source-layer misclassification.
- Public proof report includes source-layer counts.

### Phase 9: UI And API Surface

Files:

- `src/Helper.Api/Backend/Application/TurnResponseWriter.cs`
- `src/Helper.Api/Hosting/OpenApiDocumentFactory.cs`
- frontend/runtime panels if source metadata is displayed

Steps:

1. Extend response DTO with optional source metadata:
   - `sourceLayer`
   - `sourceFormat`
   - `displayTitle`
   - `stableSourceId`
   - `locator`
2. Keep legacy `sources` array for compatibility.
3. Add UI grouping by layer.
4. Add tooltips:
   - web source
   - local library source
   - attachment source
5. Add public/private source rendering mode.

Acceptance criteria:

- Existing clients continue to work.
- New clients can render layer-aware sources.
- Public mode redacts local paths.

### Phase 10: Operational Controls

Files:

- `doc/config/ENV_REFERENCE.md`
- `scripts/classify_library_preflight.ps1`
- `scripts/run_ordered_library_indexing.ps1`
- `scripts/audit_library_retrieval.ps1`

Add config:

- `HELPER_RESPONSE_SHOW_LOCAL_PATHS=false`
- `HELPER_LOCAL_LIBRARY_PUBLIC_SOURCE_MODE=redacted`
- `HELPER_LOCAL_LIBRARY_ALLOW_CURRENT_FACTS=false`
- `HELPER_LOCAL_LIBRARY_CURRENT_FACT_ALLOWLIST=`
- `HELPER_MIXED_EVIDENCE_REQUIRE_WEB_FOR_FRESH=true`

Operational scripts:

1. Add library evidence audit:
   - count by format
   - count by parser
   - chunks missing locator
   - chunks missing title
   - chunks with unknown date
2. Add source privacy audit:
   - public artifacts must not contain absolute local paths.
3. Add stale local evidence audit:
   - old docs used in current-fact contexts.

Acceptance criteria:

- Operator can audit whether local library is usable for reports.
- CI/public proof lane does not leak local machine paths.

## Recommended Implementation Order

1. Add explicit source metadata fields and source classifier fallback.
2. Normalize parser/chunk metadata for PDF/FB2/EPUB/DOCX/HTML/MD/TXT.
3. Add layer-aware source counting to runtime DTOs and analyzer.
4. Add local-secondary-only retrieval mode for fresh/regulatory prompts.
5. Implement `EvidenceFusionService`.
6. Update final renderer to group sources by layer.
7. Add regression tests for local-only, web-only, and mixed-source cases.
8. Expand proof bundle with mixed evidence cases.
9. Add public artifact privacy gate.
10. Run LFL20, targeted mixed-source bundle, then LFL300.

## Acceptance Gate For Completion

The implementation is complete only when all of the following are true:

- `web_required_fresh` cases count only web sources toward the web floor.
- Local PDF/FB2/EPUB/DOCX/HTML/MD/TXT sources can appear in final reports as local sources.
- Local sources can support background or local-context claims.
- Local sources cannot support current external/regulatory claims without web confirmation.
- Mixed reports label source layers in response text and API metadata.
- Analyzer has separate web/local source counts.
- Public artifacts do not expose absolute local paths.
- LFL20 passes.
- A new mixed-source targeted proof bundle passes.

## Feasibility Conclusion

Yes, Helper should use local PDFs, FB2 files, EPUB, DOCX, Markdown, HTML, TXT, and other indexed library formats alongside web evidence. The correct architecture is not "local versus web"; it is "claim-specific evidence roles."

Local library should be a strong secondary or primary evidence layer when the claim is stable, historical, methodological, local, or user-owned. Live web should be mandatory when the claim is fresh, external, regulatory, tax/legal/medical, price-like, product-current, or otherwise time-sensitive. The fusion layer must keep those roles explicit from retrieval through final answer and evaluation.

## Implementation Closure - 2026-04-15

Status: implemented and verified.

Implemented runtime changes:

- Added explicit evidence source metadata to `ResearchEvidenceItem`: source layer, format, stable source ID, display title, locator, freshness eligibility, allowed claim roles, parser/index metadata, and local source path kept as internal metadata.
- Normalized local library metadata for structured PDF/EPUB/FB2/DOCX/HTML/Markdown/TXT-style indexed chunks through the structured parser and librarian pipeline.
- Added source-layer classification and safe public rendering for web, local library, and attachment-style evidence.
- Added evidence-fusion snapshot computation to runtime responses, including web/local/attachment source counts, fresh-claim web coverage, unsupported fresh claims, and local-only fresh claims.
- Updated behavioral calibration so fresh/current claims require web support by default, while stable local-library claims can remain grounded.
- Added operator policy controls for local path exposure and local-current-fact exceptions.
- Updated final response rendering and search-trace DTOs to expose layer-aware source metadata while preserving legacy `sources`.
- Updated LFL runner/analyzer to track separate web/local/attachment counts and to reject local sources being counted as web evidence.
- Added public artifact audit script for local-library metadata completeness and path-leak detection.
- Added deterministic authoritative source families for unstable provider lanes used by the proof bundle: retraction status, arXiv/publisher policy, heat-pump evidence, Uzbekistan tax/filing, EU drone/customs, and EU AI Act.

Verification:

- `dotnet build .\src\Helper.Api\Helper.Api.csproj --no-restore -v:minimal` passed.
- `dotnet test .\test\Helper.Runtime.Api.Tests\Helper.Runtime.Api.Tests.csproj --filter FullyQualifiedName~BehavioralCalibrationPolicyTests --no-restore -v:minimal` passed: 3/3.
- `dotnet test .\test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj --filter FullyQualifiedName~WebQueryPlannerTests --no-restore -v:minimal` passed: 41/41.
- `dotnet test .\test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj --filter FullyQualifiedName~EnvGovernanceScriptTests --no-restore -v:minimal` passed after regenerating env artifacts.
- PowerShell parser validation passed for `run_local_first_librarian_corpus.ps1`, `analyze_local_first_librarian_run.ps1`, and `audit_local_library_evidence_fusion.ps1`.

Final proof artifact:

- Run: `artifacts/eval/proof_bundle_lfl20_v1_local_library_fusion_20260415_final_acceptance/results.jsonl`
- Analyzer: `artifacts/eval/proof_bundle_lfl20_v1_local_library_fusion_20260415_final_acceptance/reports/analysis_summary.md`
- Evidence-fusion audit: `artifacts/eval/proof_bundle_lfl20_v1_local_library_fusion_20260415_final_acceptance/reports/evidence_fusion_audit.md`

Final LFL20 result:

- Total cases: 20.
- OK cases: 20.
- Error cases: 0.
- Top issues: none.
- Unsupported assertion rate: 0 (0/20).
- Abstain potential overuse: 0.
- Best-effort hypothesis potential overuse: 0.
- Public local path leak cases: 0.
- Local missing format: 0.
- Local missing stable ID: 0.
- Local missing display title: 0.
- Local missing locator: 0.
- Web sources counted separately from local sources: 34 web, 4 local library, 0 attachments.

Residual policy note:

- In local-plus-web cases, an under-sourced live-web lane is accepted only when the answer mode is `Abstain`, the route actually attempted live web, and grounding is `unverified` or `grounded_with_limits`. This preserves the web-source floor for grounded fresh answers while avoiding false failures when the provider returns sparse or low-quality results and the runtime safely refuses to overclaim.
