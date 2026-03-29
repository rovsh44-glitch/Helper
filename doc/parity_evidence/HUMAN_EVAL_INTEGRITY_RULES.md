# Human-Eval Integrity Rules

Blind human-eval считается доказательно пригодным только после integrity-check.

Текущие проверки:

1. `reviewer concentration`
   Один reviewer не должен доминировать выборку.
2. `task-family balance`
   Одна family сценариев не должна почти полностью определять весь корпус.
3. `pattern uniqueness`
   Score-patterns не должны подозрительно часто повторяться.
4. `variance floor`
   Оценки не должны быть подозрительно ровными.
5. `mirrored delta patterns`
   Разница `Helper` vs `Baseline` не должна быть почти одинаковой во всём корпусе.

Итог:

- `PASS`: можно рассматривать blind-eval как потенциально authoritative при корректном evidence level.
- `WARN`: артефакт живой, но доверие ограничено.
- `FAIL`: blind-eval нельзя считать authoritative.
