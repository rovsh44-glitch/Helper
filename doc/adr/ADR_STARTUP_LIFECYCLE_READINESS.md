# ADR — startup lifecycle through readiness, not port-open

- Статус: accepted
- Дата: 2026-03-06

## Решение

- Startup разделён на `listening`, `catalog_ready`, `warming_background`, `warmup_complete`.
- UI и launcher ориентируются на `/api/readiness`, а не на факт открытия порта.
- Startup mode фиксируется как `fast|minimal warmup` или `warm|full warmup`.

## Причина

- Открытый порт не означает, что chat path уже пригоден для пользователя.
- Readiness должен учитывать persistence и минимальную model readiness.

## Последствия

- Любой launcher/CI smoke обязан ждать `readyForChat=true`.
- Warmup не имеет права блокировать первый реальный turn.
