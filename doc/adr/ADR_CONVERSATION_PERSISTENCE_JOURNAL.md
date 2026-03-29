# ADR — conversation persistence through journal + periodic snapshot

- Статус: accepted
- Дата: 2026-03-06

## Решение

- Conversation store использует debounce flush.
- Dirty conversations пишутся в journal.
- Snapshot делается периодически по compaction threshold.
- Health/readiness учитывают persistence loaded/ready state.

## Причина

- Sync full snapshot на каждый turn держал disk I/O в interactive path.

## Последствия

- Новая persistence логика должна быть append-first и write-behind.
- Любые blocking full-snapshot writes в turn path запрещены.
