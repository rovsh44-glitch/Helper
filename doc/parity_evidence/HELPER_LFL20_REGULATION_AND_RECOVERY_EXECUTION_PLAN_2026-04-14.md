## HELPER LFL20 Regulation And Recovery Execution Plan

Date: `2026-04-14`

Scope:

- `regulation_freshness retrieval`
- `browser/fetch recovery`

Context:

- `artifacts/eval/proof_bundle_lfl20_v1_final_rerun/reports/analysis_summary.json`
- `artifacts/eval/proof_bundle_lfl20_v1_final_rerun/responses/lfwr-090.json`
- `artifacts/eval/proof_bundle_lfl20_v1_final_rerun/responses/lfwr-095.json`
- `artifacts/eval/proof_bundle_lfl20_v1_final_rerun/responses/lfwr-100.json`
- `artifacts/eval/proof_bundle_lfl20_v1_final_rerun/responses/lfwr-125.json`
- `artifacts/eval/proof_bundle_lfl20_v1_final_rerun/responses/lfwr-153.json`
- `artifacts/eval/proof_bundle_lfl20_v1_final_rerun/responses/lfwr-168.json`

## Diagnosis

The remaining `LFL20` failures have collapsed into two system-level groups instead of many unrelated defects:

1. `browser_or_fetch_recovery_unresolved`
   - Cases: `lfwr-090`, `lfwr-095`, `lfwr-100`, `lfwr-125`
   - Current shape:
     - search produces hits
     - page fetch hits transport failures
     - browser render recovery is attempted in trace
     - runtime still records `browser_render.disabled`
     - final answer falls back to search-hit-only evidence

2. `regulation_freshness retrieval`
   - Cases: `lfwr-153`, `lfwr-168`
   - Current shape:
     - provider returns candidates
     - current prompt normalization is too weak
     - current freshness expansion is generic
     - quality gate rejects the candidate set as `regulatory_source_mismatch`
     - mandatory-web answer correctly abstains with zero sources

## Execution Order

The work should be done in this order:

1. Fix `browser/fetch recovery` structural blockers.
2. Fix `regulation_freshness retrieval` query targeting and quality admission.
3. Add regression tests for both families.
4. Re-run targeted failing cases.
5. Re-run full `LFL20`.

This order is intentional:

- recovery fixes remove a cross-cutting structural blocker affecting four cases at once;
- regulation fixes are more query-family-specific and should be validated after recovery is stable.

## Phase A. Browser And Fetch Recovery

### A1. Runtime Composition Root

Files:

- `src/Helper.Api/Hosting/ServiceRegistrationExtensions.ResearchAndTooling.cs`
- `src/Helper.Runtime.WebResearch/Rendering/BrowserRenderFallbackService.cs`
- `src/Helper.Runtime.WebResearch/Rendering/DisabledBrowserRenderFallbackService.cs`

Required change:

- stop treating browser render fallback as permanently disabled in the research path
- make service registration respect `HELPER_WEB_RENDER_ENABLED`
- preserve a safe disabled path when the feature is explicitly off

Acceptance:

- runtime no longer hard-wires `DisabledBrowserRenderFallbackService`
- traces for recovery cases stop reporting unconditional `browser_render.disabled`

### A2. Render Budget And Transport Recovery

Files:

- `src/Helper.Runtime.WebResearch/Rendering/RenderedPageBudgetPolicy.cs`
- `src/Helper.Runtime.WebResearch/Fetching/FetchStabilityPolicy.cs`
- `src/Helper.Runtime.WebResearch/WebSearchFetchEnricher.cs`
- `src/Helper.Runtime.WebResearch/Fetching/WebPageFetcher.RenderRecovery.cs`

Required change:

- improve recovery behavior for transport-failure-heavy web cases
- allow more useful recovery attempts for evidence-sensitive and sparse-evidence cases
- ensure successful search-hit projection counts as recovered fetch evidence when browser recovery still fails

Acceptance:

- `web_page_fetch.extracted_count` becomes non-zero for recovery cases that still have usable search hits
- analyzer stops raising `browser_or_fetch_recovery_unresolved`

### A3. Search-Hit Garbage Suppression For Recovery Cases

Files:

- `src/Helper.Runtime.WebResearch/Quality/WebDocumentQualityPolicy.cs`
- `src/Helper.Runtime.WebResearch/Ranking/LowTrustDomainRegistry.cs`
- `src/Helper.Runtime.WebResearch/Ranking/SourceAuthorityScorer.cs`

Required change:

- reject low-signal UGC and consumer noise more aggressively for sparse-evidence/recommended-web cases
- keep useful general technical articles, but demote irrelevant search hits

Acceptance:

- recovery cases stop citing `otvet.mail.ru`, `t.me`, irrelevant `jingyan.baidu.com`, and similar noise

## Phase B. Regulation Freshness Retrieval

### B1. Prompt Classification And Topic-Core Rewrite

Files:

- `src/Helper.Runtime.WebResearch/QueryPlanning/SearchQueryIntentProfile.cs`
- `src/Helper.Runtime.WebResearch/QueryPlanning/SearchTopicCoreRewritePolicy.cs`
- `src/Helper.Runtime.WebResearch/ResearchRequestProfile.cs`

Required change:

- classify `Составь ... checklist ...` and similar prompts as human prompts
- force meaningful topic-core rewrites for filing/reporting and visa-path prompts
- preserve strict live-evidence routing

Acceptance:

- traces for `lfwr-153` and `lfwr-168` show `search_query.rewrite stage=topic_core applied=yes`

### B2. Domain-Specific Expansion For Filing And Visa Queries

Files:

- `src/Helper.Runtime.WebResearch/QueryPlanning/SearchQueryExpansionPolicy.cs`
- `src/Helper.Runtime.WebResearch/QueryPlanning/SelectiveMultiQueryExpansionPolicy.cs`
- `src/Helper.Runtime.WebResearch/SearchIterationPolicy.cs`

Required change:

- replace generic freshness expansions with domain-specific regulatory expansions
- add official-source-targeted rewrites for:
  - Uzbekistan filing/reporting/tax/invoicing
  - Germany visa/work-permit/skilled-worker routes
- allow strict regulation cases to use a three-branch path:
  - `primary`
  - `official`
  - `freshness` or comparative follow-up

Acceptance:

- `lfwr-153` and `lfwr-168` stop ending after two empty iterations
- official branch appears in the search trace

### B3. Regulatory Quality Admission

Files:

- `src/Helper.Runtime.WebResearch/Quality/WebDocumentQualityPolicy.cs`
- `src/Helper.Runtime.WebResearch/Ranking/LowTrustDomainRegistry.cs`

Required change:

- keep rejecting mismatched or low-trust regulatory content
- expand recognition of legitimate official/legal sources for the target jurisdictions

Acceptance:

- `lfwr-153` and `lfwr-168` obtain at least `3` admissible sources
- analyzer stops raising:
  - `no_sources_in_mandatory_web_case`
  - `low_citation_coverage_for_web_case`
  - `web_sources_below_minimum`

## Phase C. Regression Coverage

Files:

- `test/Helper.Runtime.Tests/WebPageFetcherTests.cs`
- `test/Helper.Runtime.Tests/WebDocumentQualityPolicyTests.cs`
- `test/Helper.Runtime.Tests/WebQueryPlannerTests.cs`
- `test/Helper.Runtime.Tests/SimpleResearcherTests.cs`

Required change:

- add focused tests for:
  - env-aware browser render registration behavior
  - recovery projection for transport-failed renderable sources
  - stricter rejection of UGC/noise in sparse-evidence web cases
  - topic-core rewrite for Uzbekistan filing prompts
  - multibranch planning for regulation freshness prompts

## Verification Path

1. Run targeted unit tests for the changed planner, quality, recovery, and researcher paths.
2. Re-run:
   - `lfwr-090`
   - `lfwr-095`
   - `lfwr-100`
   - `lfwr-125`
   - `lfwr-153`
   - `lfwr-168`
3. Re-run full `proof_bundle_lfl20_v1.jsonl`
4. Re-run analyzer across:
   - `initial`
   - `rerun`
   - `final rerun`
   - `new repaired rerun`

## Exit Criteria

Implementation is considered complete only when:

- `browser_or_fetch_recovery_unresolved = 0`
- `no_sources_in_mandatory_web_case = 0`
- `web_sources_below_minimum = 0`
- `low_citation_coverage_for_web_case = 0`
- the repaired `LFL20` run is cleaner than both prior reruns
- the proof bundle is again truth-preserving, not merely score-improved
