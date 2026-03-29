# Live Blind Eval Authoritative Runbook

Date: `2026-03-21`

## Purpose

This runbook is the canonical operator path for turning blind human-eval from `rehearsal/sample` into `authoritative`.

## Mandatory inputs

1. Real `response_pairs.jsonl` with at least `200` dialog pairs.
2. Each row must contain:
   - `conversation_id`
   - `language`
   - `task_family`
   - `prompt`
   - `helper_response`
   - `baseline_response`
3. At least `4` independent reviewers in the assignment pool.
4. Completed reviewer CSV files with all structured note columns filled.

## Prepare phase

Use this phase before reviewer collection:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_live_blind_eval_authoritative.ps1 `
  -Phase Prepare `
  -ReviewerPoolCsv reviewer_pool.csv
```

This phase:

- validates the response-pair corpus size;
- emits the pre-score blind packet with `collectionMode=authoritative`;
- assigns reviewers with the canonical assignment policy;
- exports the reviewer handoff pack.

Successful prepare output means the run is ready for live collection. It does not mean the evidence is already authoritative.

## Finalize phase

Use this only after reviewers return completed blind sheets into `eval/live_blind_eval/inbox`:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_live_blind_eval_authoritative.ps1 `
  -Phase Finalize `
  -RefreshParityEvidenceSnapshot
```

This phase:

- imports reviewer submissions;
- rejects incomplete structured notes;
- reveals blind labels;
- rebuilds the live blind-eval bundle;
- regenerates the canonical blind human-eval report in authoritative mode;
- optionally refreshes the active parity evidence bundle/state.

## Hard rules

The following are forbidden as authoritative blind-eval proof:

- `sample` or `synthetic` corpora;
- post-hoc `Helper/Baseline -> A/B` serialization;
- reviewer sheets with missing structured notes;
- reviewer pools below `4` unique reviewers;
- corpora below `200` dialog pairs.

## Canonical outputs

- `eval/live_blind_eval/*`
- `doc/archive/top_level_history/human_eval_parity_report_latest.md`
- `doc/human_eval_parity_report_latest.integrity.json`
- `doc/human_eval_parity_report_latest.blind_pack_validation.json`
- `doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_STATE.json`
