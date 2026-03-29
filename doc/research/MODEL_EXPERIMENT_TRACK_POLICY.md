# HELPER Model Experiment Track Policy

Дата: `2026-03-23`

## Purpose

Этот документ фиксирует границу между:

- `product-quality execution track`
- `model-side experiment track`

`STEP-016` считается выполненным только если эта граница существует не только в prose, но и как repo-level governance surface.

## Product-Quality Track

Authoritative execution track для retrieval/evidence quality closure:

- [HELPER_EXECUTION_ORDER_TABLE_LFL300_2026-03-22.md](../archive/comparative/HELPER_EXECUTION_ORDER_TABLE_LFL300_2026-03-22.md)
- [HELPER_EXECUTION_DASHBOARD_LFL300_2026-03-22.md](../archive/comparative/HELPER_EXECUTION_DASHBOARD_LFL300_2026-03-22.md)

Canonical operator path для product-quality evaluation:

- [run_eval_runner_v2.ps1](../../scripts/run_eval_runner_v2.ps1)
- [run_local_first_librarian_corpus.ps1](../../scripts/run_local_first_librarian_corpus.ps1)
- [analyze_local_first_librarian_run.ps1](../../scripts/analyze_local_first_librarian_run.ps1)

## Research-Only Track

Model-side experiments допускаются только как `research_only`.

Разрешённые темы текущего track:

1. `selective_residual_memory`
2. `evidence_aware_decoding`
3. `retrieval_conditioned_latent_routing`

Для этого трека запрещено:

1. заявлять benchmark uplift как product result без отдельного reproduction на product-quality track.
2. встраивать model-specific forks прямо в execution order table.
3. переопределять canonical product runner под experimental mode без отдельного RFC.
4. использовать experiment artifacts как release or parity evidence.

## Proposal Contract

Каждый новый experiment proposal должен иметь:

1. `experiment_id`
2. `theme_id`
3. `hypothesis`
4. `artifact_root`
5. `benchmark_slice_targets`
6. `success_criteria`
7. `rollback_or_no_promotion_rule`

## Promotion Contract

Experiment может перейти из `research_only` в product track только если:

1. создан отдельный RFC;
2. есть before/after evidence хотя бы на relevant benchmark slices;
3. effect reproduced without weakening current governance or analyzer honesty;
4. execution docs explicitly updated as a separate change.

## Active State

Текущее active state хранится в:

- [CURRENT_MODEL_EXPERIMENT_TRACK.json](./active/CURRENT_MODEL_EXPERIMENT_TRACK.json)
- [CURRENT_MODEL_EXPERIMENT_TRACK.md](./active/CURRENT_MODEL_EXPERIMENT_TRACK.md)
