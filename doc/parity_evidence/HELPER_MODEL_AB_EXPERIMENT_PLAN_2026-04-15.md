# Helper Model A/B Experiment Plan - 2026-04-15

## Purpose

This plan defines a separate model-switch experiment after the green LFL20 baseline has been stabilized and committed.

The model experiment must not be mixed with retrieval, browser/fetch recovery, local-library evidence fusion, analyzer policy, or prompt/corpus edits. Otherwise the result cannot prove whether the model improved or degraded Helper.

## Baseline

Use the current green baseline as Control A:

- Runtime: current live Helper API configuration.
- Corpus: `eval/web_research_parity/proof_bundle_lfl20_v1.jsonl`.
- Acceptance run: `artifacts/eval/proof_bundle_lfl20_v1_local_library_fusion_20260415_final_acceptance`.
- Analyzer report: `reports/analysis_summary.md`.
- Evidence audit: `reports/evidence_fusion_audit.md`.

Baseline acceptance result:

- Total cases: `20`.
- OK cases: `20`.
- Errors: `0`.
- Unsupported assertion cases: `0`.
- Best-effort potential overuse: `0`.
- Abstain potential overuse: `0`.
- Public local-path leaks: `0`.
- Missing local-source metadata: `0`.

## Candidate B

Candidate B is a model-only change.

Candidate example:

- `gemma4:26b` in Ollama, if locally available and operational.

Before running the A/B experiment, verify:

- The exact model tag exists locally or can be pulled intentionally.
- The model runs through the same Helper API path as Control A.
- Context length, timeout budget, and memory pressure are recorded.
- No retrieval, prompt, analyzer, corpus, or source-family changes are made in the same branch.

## Experimental Rules

Hard rules:

1. Freeze the corpus.
2. Freeze analyzer code.
3. Freeze retrieval code.
4. Freeze browser/fetch recovery code.
5. Freeze local-library evidence fusion code.
6. Change only model configuration.
7. Run Control A and Candidate B from the same commit.
8. Store reports in separate timestamped artifact directories.
9. Compare with the same analyzer version.
10. Do not promote Candidate B unless it wins or ties on quality without unacceptable latency/resource regression.

## Environment Capture

For each run, capture:

- Git commit SHA.
- Branch name.
- Model name and exact tag.
- Ollama version.
- Helper API relevant model env vars.
- Retrieval env vars.
- Timeout/budget env vars.
- Machine RAM and GPU/CPU availability if available.
- Start/end timestamps.
- Full analyzer output path.

Recommended output directories:

- Control A rerun: `artifacts/eval/proof_bundle_lfl20_model_ab_control_current_YYYYMMDD_HHMMSS`.
- Candidate B run: `artifacts/eval/proof_bundle_lfl20_model_ab_candidate_gemma4_26b_YYYYMMDD_HHMMSS`.

## Run Sequence

1. Confirm the working tree is clean.
2. Confirm the branch contains the committed green LFL20 baseline.
3. Start Helper API with Control A model configuration.
4. Run LFL20 once against Control A.
5. Run analyzer and evidence-fusion audit for Control A.
6. Stop Helper API.
7. Start Helper API with Candidate B model configuration only.
8. Run the same LFL20 corpus against Candidate B.
9. Run analyzer and evidence-fusion audit for Candidate B.
10. Compare Control A versus Candidate B.
11. Store the comparison summary in `doc/parity_evidence`.

## Metrics

Primary quality metrics:

- OK cases.
- Error cases.
- Unsupported assertion cases.
- Cases with analyzer issues.
- Grounding status distribution.
- Citation coverage.
- Verified claim ratio.
- Freshness coverage for web-required cases.
- Browser/fetch recovery unresolved cases.
- Regulatory source mismatch cases.
- Abstain potential overuse.
- Best-effort hypothesis potential overuse.

Evidence-fusion metrics:

- Web source count.
- Local-library source count.
- Attachment source count.
- Public path leak cases.
- Missing local source format.
- Missing local stable ID.
- Missing local display title.
- Missing local locator.

Runtime/resource metrics:

- Total run duration.
- Per-case duration.
- Timeout count.
- API bootstrap failures.
- Browser/fetch timeout count.
- Model memory pressure if available.
- Retry count.

## Promotion Criteria

Candidate B can be promoted only if all hard gates pass:

- `20/20` LFL20 cases are OK.
- `0` errors.
- `0` unsupported assertion cases.
- `0` abstain potential overuse cases.
- `0` best-effort hypothesis potential overuse cases.
- `0` public local-path leaks.
- No regression in web-required freshness cases.
- No regression in browser/fetch recovery cases.
- No new regulatory source mismatch issue.

Soft gates:

- Median per-case duration should not regress by more than `25%`.
- Worst-case duration should not exceed the existing timeout budget.
- Source quality should be equal or better.
- Local-library sources must remain clearly separated from web sources.

If Candidate B improves answer quality but violates runtime/resource soft gates, keep it as an experimental profile rather than the default profile.

## Failure Handling

If Candidate B fails hard gates:

- Do not change the default model.
- Keep the comparison report.
- Classify failures by model behavior, not by retrieval/runtime unless reproduced on Control A.
- Open a separate follow-up plan for prompt/model adaptation only if the failure pattern is stable.

If Candidate B fails due infrastructure:

- Repeat once after confirming API/model readiness.
- If the failure remains infrastructure-bound, mark the experiment inconclusive rather than failed.

## Deliverables

Required deliverables:

- Control A run directory.
- Candidate B run directory.
- Analyzer report for both runs.
- Evidence-fusion audit for both runs.
- A comparison markdown report in `doc/parity_evidence`.
- A clear decision: keep current default, promote Candidate B, or keep Candidate B as an optional experimental profile.

## Non-Goals

This A/B stage does not include:

- Adding new LFL20 cases.
- Editing analyzer policy.
- Changing retrieval source families.
- Changing browser/fetch recovery.
- Changing local-library parsing.
- Changing public positioning.
- Merging dependency-update PRs.

Those changes must remain separate so the model experiment stays interpretable.
