# ADR — code-root and data-root separation

- Статус: accepted
- Дата: 2026-03-06

## Решение

- Code остаётся в `HELPER_ROOT`.
- Data-heavy/runtime-heavy trees живут в `HELPER_DATA_ROOT` вне репозитория.
- Canonical directories: `PROJECTS`, `library`, `LOG`, `forge_templates`.

## Причина

- Heavy runtime trees в repo root ломают watcher-ы, CI и DX.

## Последствия

- Новые runtime artefacts нельзя писать в repo root.
- CI gate `scripts/check_root_layout.ps1` блокирует откат к старой схеме.
