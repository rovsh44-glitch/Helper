# ADR — browser auth via scoped session bootstrap

- Статус: accepted
- Дата: 2026-03-06

## Решение

- Browser больше не получает долгоживущий backend/model secret.
- Browser делает `POST /api/auth/session` и получает scoped short-lived bearer token.
- UI использует session token для chat/signalr, а root API key остаётся только в backend/launcher.

## Причина

- Это убирает утечку секретов в bundle и devtools.
- Это делает auth revocable и role/scope-aware.

## Последствия

- Любой новый browser flow должен использовать session bootstrap, а не raw API key.
- CI gate `scripts/secret_scan.ps1` остаётся обязательным.
