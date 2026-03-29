# ADR — frontend API transport policy

- Статус: accepted
- Дата: 2026-03-07

## Решение

- Низкоуровневый browser transport разрешён только в `services/httpClient.ts`.
- Typed/backend-aware browser API разрешён только в `services/generatedApiClient.ts`.
- Единственное исключение для `services/apiConfig.ts` — session bootstrap через `POST /api/auth/session`.

## Причина

- Это исключает разрастание прямых `fetch`/`fetchWithTimeout` вызовов по UI.
- Это делает OpenAPI snapshot, generated client и UI gate единым source-of-truth.

## Последствия

- Любой новый browser endpoint call должен добавляться через `services/generatedApiClient.ts`.
- Gate `scripts/check_ui_api_usage.ps1` блокирует прямой transport вне sanctioned modules.
- Изменение API контракта должно сопровождаться обновлением `doc/openapi_contract_snapshot.json`.
