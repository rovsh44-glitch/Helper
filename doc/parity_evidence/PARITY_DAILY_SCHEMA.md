# Parity Daily Snapshot Schema

Date: `2026-03-19`
Status: `active`

## Purpose

`parity_daily` is the operational journal for `HELPER_HUMAN_LEVEL_PARITY`.

Each snapshot records one daily measurement point and links it to the underlying evidence artifacts. The directory may contain `sample`, `synthetic`, `dry_run`, `live_non_authoritative`, and `authoritative` snapshots, but only counted snapshots participate in the 14-day certification window.

## Counted vs non-counted snapshots

- `sample`, `synthetic`, `dry_run`: valid for examples, debugging, and schema tests; do not count toward the 14-day window.
- `live_non_authoritative`, `authoritative`: count toward the 14-day window if the snapshot is schema-valid.

## Required fields

All daily snapshots must contain:

- `schemaVersion`
- `generatedAt`
- `date`
- `evidenceLevel`
- `ttft_local_ms`
- `ttft_network_ms`
- `conversation_success_rate`
- `helpfulness`
- `citation_precision`
- `citation_coverage`
- `tool_correctness`
- `security_incidents`
- `open_p0_p1`
- `blind_human_eval_status`
- `real_model_eval_status`
- `release_baseline_status`
- `sourceLinks`

## Field meanings

- `schemaVersion`: current schema version. Current value: `2`.
- `generatedAt`: timestamp when the snapshot was created.
- `date`: logical day covered by the snapshot, `YYYY-MM-DD`.
- `evidenceLevel`: one of `sample`, `synthetic`, `dry_run`, `live_non_authoritative`, `authoritative`.
- `ttft_local_ms`: local time-to-first-token, milliseconds.
- `ttft_network_ms`: end-to-end network time-to-first-token, milliseconds.
- `conversation_success_rate`: daily success rate in `[0,1]`.
- `helpfulness`: daily helpfulness score.
- `citation_precision`: daily citation precision in `[0,1]`.
- `citation_coverage`: daily citation coverage in `[0,1]`.
- `tool_correctness`: daily tool correctness in `[0,1]`.
- `security_incidents`: number of security incidents for the day.
- `open_p0_p1`: number of open `P0/P1` issues at snapshot time.
- `blind_human_eval_status`: linked blind-eval status for the day.
- `real_model_eval_status`: linked real-model eval status for the day.
- `release_baseline_status`: linked release baseline status for the day.
- `sourceLinks`: object containing paths to linked evidence artifacts.
- `notes`: optional operator notes.

## Validation rules

Validation is performed by:

- `scripts/common/ParityEvidenceCommon.ps1`
- `scripts/capture_parity_daily_snapshot.ps1`
- `scripts/certify_parity_14d_v2.ps1`
- `scripts/build_parity_evidence_bundle.ps1`

A snapshot is considered valid only if:

1. all required fields are present;
2. `evidenceLevel` is one of the allowed values;
3. the file is parseable JSON.

Only valid snapshots with `evidenceLevel` in `live_non_authoritative` or `authoritative` are counted toward the certification window.

## Example

Canonical schema example:

- `doc/parity_daily/sample_snapshot.json`

This example is intentionally marked `sample` and is not part of the counted certification window.
