# Blind Eval Collection Flow

Дата: `2026-03-19`

## Canonical flow

1. Подготовить response pairs:
   - `conversation_id`
   - `language`
   - `task_family`
   - `prompt`
   - `helper_response`
   - `baseline_response`
2. Для authoritative collection использовать orchestration wrapper:
   - `scripts/run_live_blind_eval_authoritative.ps1 -Phase Prepare`
3. Выпустить reviewer-facing blind packet:
   - `scripts/prepare_live_blind_eval_packets.ps1`
4. Выпустить reviewer assignment manifest:
   - `scripts/assign_blind_eval_reviewers.ps1`
5. Собрать reviewer CSV sheets на blind labels `A/B`.
6. Импортировать completed reviews:
   - `scripts/import_live_blind_eval_reviews.ps1`
7. Выполнить controlled reveal:
   - `scripts/reveal_live_blind_eval_scores.ps1`
8. Пропустить revealed scored CSV через:
   - `scripts/validate_human_eval_integrity_v2.ps1`
   - `scripts/generate_human_parity_report_v2.ps1`
   - `scripts/refresh_parity_evidence_snapshot.ps1`
9. Для canonical authoritative finalization использовать:
   - `scripts/run_live_blind_eval_authoritative.ps1 -Phase Finalize -RefreshParityEvidenceSnapshot`

## Forbidden shortcuts

- Нельзя считать blind proof-ом CSV, который изначально содержал `Helper/Baseline` и был только постфактум сериализован в `A/B`.
- Нельзя хранить reveal map в reviewer-facing packet.
- Нельзя поднимать corpus до `authoritative`, если reviewer assignment не соответствует policy.
