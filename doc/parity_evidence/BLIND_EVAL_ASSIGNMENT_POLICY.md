# Blind Eval Assignment Policy

Дата: `2026-03-19`

## Minimums

1. Минимум `4` уникальных reviewer-а на весь corpus.
2. Минимум `2` reviewer-а на каждый dialog.
3. Ни один reviewer не должен владеть больше чем `45%` assignment-ов.

## Coverage

1. Assignment должен учитывать `language`.
2. Assignment должен учитывать `task_family`.
3. Если reviewer pool объявляет ограничения по `languages` или `task_families`, assigner не должен выдавать несовместимые пары.

## Tooling

- `scripts/assign_blind_eval_reviewers.ps1` формирует canonical assignment manifest.
- Assignment manifest затем используется как source of truth для `scripts/import_live_blind_eval_reviews.ps1`.
