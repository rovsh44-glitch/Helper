# HELPER Parity Evidence

Этот каталог хранит отдельный truth-layer для `human-level parity evidence`.

Правила:

1. `parity evidence` не смешивается с release certification.
2. `sample`, `synthetic`, `dry_run` и любые иные `non-authoritative` артефакты не являются доказательством parity.
3. Каноническое текущее состояние хранится в:
   - `doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_STATE.json`
   - `doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_STATE.md`
4. Blind human-eval report считается authoritative только если:
   - sample sufficiency gates passed;
   - blind pack provenance/validation passed;
   - reviewer diversity gates passed;
   - integrity sidecar report passed;
   - evidence level помечен как `authoritative`.
   - canonical operator path uses:
     - `scripts/run_live_blind_eval_authoritative.ps1`
     - `doc/parity_evidence/LIVE_BLIND_EVAL_AUTHORITATIVE_RUNBOOK.md`
5. Web-research blind-eval report считается authoritative только если:
   - corpus genuinely web-research focused;
   - sample sufficiency gates passed;
   - blind pack provenance/validation passed;
   - reviewer diversity gates passed;
   - integrity sidecar report passed;
   - evidence level помечен как `authoritative`.
   - canonical operator path uses:
     - `scripts/run_web_research_blind_eval_authoritative.ps1`
     - `doc/parity_evidence/WEB_RESEARCH_BLIND_EVAL_AUTHORITATIVE_RUNBOOK.md`
   - post-GitHub collection plan:
     - `doc/parity_evidence/WEB_RESEARCH_POST_GITHUB_30_DAY_COLLECTION_PLAN.md`
6. Real-model eval считается authoritative только если:
   - режим не `dry_run`;
   - evidence level помечен как `authoritative`;
   - runtime/quality gates зелёные.
7. Для live-authoritative real-model path использовать:
   - `scripts/run_eval_real_model_authoritative.ps1`
   - `doc/parity_evidence/REAL_MODEL_EVAL_REQUIREMENTS.md`

Этот контур отвечает только за доказательную базу parity и не заменяет:

- `doc/certification/active/*`
- `doc/reasoning/active/*`

Поддерживающие blind-eval артефакты:

- `eval/human_eval_manifest.json`
- `eval/human_eval_blind_pack.csv`
- `doc/human_eval_blind_pack_validation.json`
- `doc/parity_evidence/REVIEWER_REQUIREMENTS.md`
