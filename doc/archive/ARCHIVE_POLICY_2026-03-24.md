# Helper Archive Policy

Дата: `2026-03-24`

Цель:

- сохранить исторические evidence bundles и raw outputs;
- убрать их из активного code-navigation surface;
- не допустить повторного превращения `doc/` в неструктурированный data dump.

## Archive Classes

1. Human-readable reports
   Это итоговые аудиты, remediation-планы, closure reports и summary-документы.
   Они остаются в основном `doc/` surface и должны быть пригодны для прямого чтения человеком.

2. Historical evidence bundles
   Это сохранённые execution traces, операторские логи, comparative snapshots и большие historical packs.
   Они живут под `doc/archive/` или под специализированными archival roots внутри `doc/pre_certification/`.

3. Raw generated runtime artifacts
   Это `probe_runtime`, `files/PROJECTS`, временные generated outputs, machine-local debris и тяжёлые bundle folders.
   Они не считаются частью активного рабочего surface и должны быть исключены из обычного поиска через `.ignore` либо вынесены за пределы repo-root в `HELPER_DATA_ROOT\runtime`.

## Navigation Rules

1. Активные engineering документы публикуются в `doc/` верхнего уровня.
2. Historical материалы публикуются в `doc/archive/` или в явно помеченных archival поддеревьях.
3. Raw runtime artifacts не должны появляться в новых active doc paths.
4. Если доказательство требует raw bundle, в основном документе должен оставаться pointer, а не дублироваться весь bundle.

## Search And Storage Hygiene

Текущие ignore-rules обязаны скрывать из основного поиска:

- `doc/pre_certification/**/probe_runtime/**`
- `doc/pre_certification/**/files/PROJECTS/**`
- `doc/archive/**/probe_runtime/**`
- `doc/archive/**/files/PROJECTS/**`

Дополнительно:

- `HELPER_DATA_ROOT/runtime/root_archive/**` должен жить вне repo-root и не возвращаться в рабочую code surface.

Это не удаляет evidence, а только отделяет archival шум от активной рабочей поверхности.

## Publishing Rule

Любой новый remediation/audit cycle обязан:

1. публиковать human-readable report в `doc/`;
2. складывать bulky raw outputs только в archival roots;
3. обновлять `.ignore`, если появляется новый класс archive-noise.
