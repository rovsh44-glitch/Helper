# ADR — async audit isolation

- Статус: accepted
- Дата: 2026-03-06

## Решение

- Post-turn audit идёт через bounded queue + background worker.
- Queue overflow и audit failures попадают в dead-letter store.
- Audit stage имеет отдельные metrics и не влияет на interactive latency budget.

## Причина

- Audit backlog не должен конкурировать с user-facing turn path.

## Последствия

- Любая новая enrichment/audit работа должна быть async by default.
- Interactive turn path не должен ждать audit completion.
