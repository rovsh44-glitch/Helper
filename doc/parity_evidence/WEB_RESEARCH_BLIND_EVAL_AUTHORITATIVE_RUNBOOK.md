# Web-Research Blind Eval Authoritative Runbook

Date: `2026-03-21`

## Purpose

This runbook is the canonical operator path for turning `web-research parity` blind-eval from `rehearsal/sample` into `authoritative`.

## Mandatory inputs

1. Real `response_pairs.jsonl` with at least `200` dialog pairs.
2. Each row must contain:
   - `conversation_id`
   - `language`
   - `task_family`
   - `prompt`
   - `helper_response`
   - `baseline_response`
3. The corpus must be genuinely web-research focused:
   - `latest/current`
   - `direct URL`
   - `comparison`
   - `contradiction/conflict`
   - `local/recommendation`
   - `stale follow-up`
4. At least `4` independent reviewers in the assignment pool.
5. Completed reviewer CSV files with all structured note columns filled.

## Prepare phase

Use this before reviewer collection:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_web_research_blind_eval_authoritative.ps1 `
  -Phase Prepare `
  -ReviewerPoolCsv reviewer_pool.csv
```

This phase:

- validates the response-pair corpus size;
- emits the pre-score blind packet with `collectionMode=authoritative`;
- assigns reviewers with the canonical assignment policy;
- exports the reviewer handoff pack into `eval/web_research_blind_eval/handoff/active`.

## Finalize phase

Use this only after reviewers return completed blind sheets into `eval/web_research_blind_eval/inbox`:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_web_research_blind_eval_authoritative.ps1 `
  -Phase Finalize
```

This phase:

- imports reviewer submissions;
- rejects incomplete structured notes;
- reveals blind labels;
- rebuilds the live blind-eval bundle;
- regenerates the canonical web-research blind-eval report in authoritative mode.

## Hard rules

The following are forbidden as authoritative web-research proof:

- `sample` or `synthetic` corpora;
- corpora that are not genuinely web-research focused;
- reviewer sheets with missing structured notes;
- reviewer pools below `4` unique reviewers;
- corpora below `200` dialog pairs.

## Smoke verification boundary

Synthetic smoke runs are allowed only for pipeline verification.

They may be used to confirm that:

- `Prepare` produces a valid blind packet and handoff pack;
- `Finalize` can close with `authoritative=YES` when integrity/diversity/provenance gates are satisfied;
- the web-research wrapper remains compatible with the shared blind-eval workflow.

They must not be treated as canonical evidence and must not replace the real `>=200` dialog live collection.

## Canonical outputs

- `eval/web_research_blind_eval/*`
- `doc/web_research_parity_report_latest.md`
- `doc/web_research_parity_report_latest.integrity.json`
- `doc/web_research_parity_report_latest.blind_pack_validation.json`
- `doc/web_research_parity_report_latest.blind_eval_authoritative.md`
