# ADR — buffered streaming UI

- Статус: accepted
- Дата: 2026-03-06

## Решение

- Token stream буферизуется отдельно от основной message history.
- Message history коммитится пакетно.
- Markdown не парсится на каждом token chunk.
- Progress/log panels вынесены из message list и capped.

## Причина

- Full rerender history на каждый token создавал UI lag и scroll thrash.

## Последствия

- Новый streaming UI код не должен мутировать весь `messages[]` по каждому token.
- Scroll policy — только debounced/bottom-only, без `smooth` в token loop.
