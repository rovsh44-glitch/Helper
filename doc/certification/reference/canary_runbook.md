# Canary Runbook
Date: 2026-02-26

## Traffic shift plan
1. Stage 1: `5%` traffic for 24h.
2. Stage 2: `20%` traffic for 24h.
3. Stage 3: `50%` traffic for 48h.
4. Stage 4: `100%` traffic when all KPI gates are green.

## KPI gates (must stay green for each stage)
- `conversation_success_rate >= 0.85`
- `helper_user_helpfulness_average >= 4.3`
- `citation_coverage >= 0.70` on factual turns
- `tool_call_correctness >= 0.90`
- `security_incidents = 0`

## Automatic stop conditions
- Any P0/P1 incident.
- Error rate spike > 2x baseline for 15m.
- TTFT p95 breach > 20% above target for 30m.

## Rollback actions
1. Revert traffic to previous stage immediately.
2. Disable last enabled feature flag from `doc/rollout_matrix_v2.md`.
3. Run `scripts/ci_gate.ps1` and `scripts/load_streaming_chaos.ps1` before retry.

## KPI gate automation
- Live gate: `powershell -ExecutionPolicy Bypass -File scripts/run_canary_gate.ps1 -ApiBase http://localhost:5000 -TrafficPercent 20`
- Offline validation (sample): `powershell -ExecutionPolicy Bypass -File scripts/run_canary_gate.ps1 -MetricsJsonPath doc/canary/sample_metrics_snapshot.json -TrafficPercent 20`
- Output report: `doc/canary_gate_report.md`.
