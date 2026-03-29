# HELPER Research Governance

Этот каталог хранит `model-side R&D` отдельно от `product-quality execution`.

Правила:

1. `model experiments` не являются release blocker по умолчанию.
2. `model experiments` не заменяют execution roadmap из `doc/archive/comparative/HELPER_EXECUTION_ORDER_TABLE_LFL300_2026-03-22.md`.
3. Каноническое текущее состояние research track хранится в:
   - `doc/research/active/CURRENT_MODEL_EXPERIMENT_TRACK.json`
   - `doc/research/active/CURRENT_MODEL_EXPERIMENT_TRACK.md`
4. Каноническая policy surface для этого трека:
   - `doc/research/MODEL_EXPERIMENT_TRACK_POLICY.md`
   - `doc/research/MODEL_EXPERIMENT_TRACK_REGISTRY.json`
5. Product-quality eval runner не должен использоваться как скрытый model-experiment runner:
   - canonical product path uses `scripts/run_eval_runner_v2.ps1`
   - model-side experiments должны оставаться `research_only` до отдельного RFC и отдельной evidence chain
6. Активные идеи research track сейчас ограничены темами:
   - `selective_residual_memory`
   - `evidence_aware_decoding`
   - `retrieval_conditioned_latent_routing`
7. Promotion в product roadmap возможен только если:
   - оформлен отдельный RFC;
   - есть before/after evidence на benchmark slices;
   - изменение не смешивается с already-authoritative quality-closure execution track.

Этот контур не заменяет:

- `doc/certification/active/*`
- `doc/parity_evidence/active/*`
- `doc/reasoning/active/*`

