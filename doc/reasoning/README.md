# HELPER Reasoning State

Этот каталог хранит reasoning-specific artifacts отдельно от release certification.

Правила:

1. Reasoning benchmark не является release blocker по умолчанию.
2. Reasoning baseline хранится отдельно от `doc/certification/active/*`.
3. Каноническое текущее состояние reasoning хранится в:
   - `doc/reasoning/active/CURRENT_REASONING_STATE.json`
   - `doc/reasoning/active/CURRENT_REASONING_STATE.md`
4. Runtime-артефакты reasoning могут создаваться в `temp/verification/reasoning/`, но официальный snapshot публикуется только через `scripts/refresh_reasoning_snapshot.ps1`.
