# Blind Eval Reviewer Handoff Pack

Дата: `2026-03-20`

## Purpose

Этот pack нужен, чтобы передать blind-eval задания reviewers без раскрытия `Helper/Baseline`.

## Canonical exporter

- [export_reviewer_handoff_pack.ps1](../../scripts/export_reviewer_handoff_pack.ps1)

## Input artifacts

- [live_blind_eval_packet.csv](../../eval/live_blind_eval/packets/live_blind_eval_packet.csv)
- [live_blind_eval_packet_manifest.json](../../eval/live_blind_eval/manifests/live_blind_eval_packet_manifest.json)
- [reviewer_assignment.json](../../eval/live_blind_eval/manifests/reviewer_assignment.json)

## Output structure

- `eval/live_blind_eval/handoff/active/README_COORDINATOR.md`
- `eval/live_blind_eval/handoff/active/REVIEWER_INSTRUCTIONS_RU.md`
- `eval/live_blind_eval/handoff/active/reviewer_response_template.csv`
- `eval/live_blind_eval/handoff/active/reviewers/<reviewer_id>/packet.csv`
- `eval/live_blind_eval/handoff/active/reviewers/<reviewer_id>/submission.csv`
- `eval/live_blind_eval/handoff/active/handoff_manifest.json`

## Submission schema

- `submission.csv` содержит:
  - четыре numeric rubric columns: `clarity`, `empathy_appropriateness`, `usefulness`, `factuality`
  - пять structured note columns: `robotic_repetition`, `unnatural_templating`, `language_mismatch`, `clarification_helpfulness`, `naturalness_feel`
- Export, import и reveal scripts сохраняют эти structured note columns end-to-end.

## Coordinator rules

1. Каждому reviewer отправляется только его собственная папка.
2. Reviewer не получает `reveal map`.
3. Reviewer не должен знать, где `Helper`, а где `Baseline`.
4. Reviewer возвращает только заполненный `submission.csv`.
5. Возвратные CSV кладутся в `eval/live_blind_eval/inbox`.

## Next step after collection

1. [import_live_blind_eval_reviews.ps1](../../scripts/import_live_blind_eval_reviews.ps1)
2. [reveal_live_blind_eval_scores.ps1](../../scripts/reveal_live_blind_eval_scores.ps1)
3. [validate_human_eval_integrity_v2.ps1](../../scripts/validate_human_eval_integrity_v2.ps1)
4. [generate_human_parity_report_v2.ps1](../../scripts/generate_human_parity_report_v2.ps1)
5. [refresh_parity_evidence_snapshot.ps1](../../scripts/refresh_parity_evidence_snapshot.ps1)
