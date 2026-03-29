# HELPER Unified Human + Golden Certification Master Plan (Rewritten, 2026-03-08)

Источник переработки:
1. `HELPER_UNIFIED_HUMAN_GOLDEN_CERTIFICATION_MASTER_PLAN_2026-03-03.md`

---

## 1. Назначение документа

Этот документ задаёт единую, логически непротиворечивую программу вывода HELPER к устойчивой сертификации по двум направлениям одновременно:

1. **Human-level dialog parity**
   - распознавание намерения,
   - корректные уточнения,
   - grounding,
   - безопасность,
   - стабильный streaming/runtime.

2. **Golden-template lifecycle quality**
   - routing,
   - forge,
   - promotion,
   - certification,
   - rollback readiness.

3. **Official 14-day certification**
   - 14 последовательных официальных дней,
   - строгие ежедневные и оконные гейты,
   - отсутствие bypass-механизмов в официальном контуре.

---

## 2. Базовый принцип

Целью программы является не «абсолютная безошибочность», потому что технически **zero-error guarantee недостижим**, а состояние **high-confidence release**, в котором одновременно выполняются следующие условия:

1. Нет открытых P0/P1.
2. Все обязательные daily gates зелёные.
3. Rolling 14-day strict window зелёный и полный.
4. Есть реальное доказательство устойчивости на протяжении 14 календарных дней.
5. Система rollback-ready и управляется без ручных обходов правил.

---

## 3. Формальная причинно-следственная модель

Основная цепочка зависимости:

1. **Runtime/chat instability**
   -> снижает generation success и увеличивает latency
   -> ухудшает `3.1`
   -> в итоге разрушает дневной пакет и затем оконный пакет `3.2`.

2. **Golden routing/promotion quality drift**
   -> ухудшает Golden Hit и compile stability
   -> приводит к провалу `3.1` и `3.3`.

3. **Weak deterministic repair + timeout handling**
   -> приводит к smoke failures, timeout tails и отсутствующим отчётам
   -> приводит к провалу `3.3`.

4. **Eval/human-data weakness**
   -> приводит к провалу `3.5`
   -> день считается failed
   -> официальный счётчик сертификации сбрасывается.

5. **Любой красный официальный день, либо неполное strict window**
   -> приводит к провалу `3.2`
   -> официальный запуск не может стартовать или продолжаться.

### Операционный вывод

1. `3.3` — это **ведущий индикатор нестабильности**, его нужно стабилизировать раньше остальных.
2. `3.2` — это **оконный строгий индикатор**, который нельзя честно закрыть до тех пор, пока не накоплено реальное 14-дневное доказательство.
3. Следовательно, `3.2` не должен интерпретироваться как «обязан быть полностью PASS с первого pre-cert дня».
4. Вместо этого `3.2` должен иметь два строго различаемых режима:
   - **Anchor Pending** — окно ещё честно не достроено, но day package операционно допустим;
   - **Strict Pass** — окно полностью собрано и прошло strict-проверку.

Именно это разделение устраняет ложный замкнутый круг зависимости.

---

## 4. Единая модель статусов дня

Чтобы не возникало логической путаницы, в документе используются **три разных статуса дня**, а не один:

### 4.1 Rehearsal Day Functional Pass

День считается **functional pass**, если:

1. `3.1 = PASS`
2. `3.3 = PASS`
3. `3.4 = PASS`
4. `3.5 = PASS`
5. `3.2` красный **только** потому, что strict 14-day window ещё объективно не заполнен.

### 4.2 Rehearsal Day Closed (Anchor Pending)

День считается **closed as GREEN_ANCHOR_PENDING**, если:

1. выполнены все условия functional pass;
2. нет ни одного уже закрытого rehearsal day внутри активного pre-cert цикла со статусом red;
3. причина незелёности `3.2` — **только неполнота окна**, а не нарушение качества;
4. день заархивирован как часть строящегося pre-cert anchor.

### 4.3 Fully Green Day

День считается **fully green**, если:

1. `3.1 = PASS`
2. `3.2 = PASS`
3. `3.3 = PASS`
4. `3.4 = PASS`
5. `3.5 = PASS`

### 4.4 Officially Valid Day

Официальный день считается **officially valid**, если он fully green и закрыт внутри официального цикла без каких-либо bypass-исключений.

---

## 5. Unified KPI contract

### 5.1 Daily mandatory KPIs

Каждый официальный день обязан проходить все условия:

1. Golden Hit Rate >= 90%
2. Generation Success Rate >= 95%
3. P95 Ready <= 25s
4. Unknown Error Rate <= 5%
5. Smoke compile pass rate >= 0.90
6. Smoke p95 <= 120s
7. Eval real-model pass >= 85% и runtime errors = 0
8. Human parity thresholds pass на достаточной выборке

### 5.2 Window mandatory KPIs

1. Rolling parity window gate должен возвращать:
   - `Passed = true`
   - `WindowComplete = true`
   - `WindowDays = 14`
   - strict mode
2. Должно существовать 14 последовательных официальных дней, каждый из которых officially valid.

### 5.3 Важное уточнение

Фраза **«all KPI green»** применяется:

1. **только к fully green day**, либо
2. к официальному дню.

Для pre-cert дней `R-Day-01 ... R-Day-13` корректный термин — **functional green with anchor pending**, а не «all KPI green».

Это уточнение устраняет прежнее логическое противоречие.

---

## 6. Критические блокеры

## 6.1 Blocker B-3.3 (real-task smoke)

### Текущий паттерн, который нужно устранить

1. Timeout tail около `~124s`
2. `REPORT_NOT_FOUND` после timed-out runs
3. Compile failures вида `CS0119` и `CS0428`

### Действия

1. Добавить deterministic timeout failure persistence в generation path:
   - писать `validation_report.json` и `generation_runs.jsonl` даже при timeout/cancel;
   - файлы:
     - `src/Helper.Runtime/HelperOrchestrator.cs`
     - `src/Helper.Runtime/Generation/GenerationValidationReportWriter.cs`

2. Расширить deterministic compile repair trigger set:
   - направлять `CS0119` и `CS0428` через method-group fix path;
   - файл:
     - `src/Helper.Runtime/Generation/CompileGateRepairService.cs`

3. Добавить regression tests для обоих классов ошибок и для timeout-report persistence:
   - `test/Helper.Runtime.Tests/*`

4. Выполнять stabilization loops:
   - `Runs = 20` до 3 последовательных зелёных батчей;
   - затем `Runs = 50` как официальный validation pack.

### Acceptance

1. `smoke_compile_pass_rate >= 0.90`
2. `smoke_p95_duration_sec <= 120`
3. В official 50-run pack отсутствует `REPORT_NOT_FOUND` среди top errors.

---

## 6.2 Blocker B-3.2 (rolling 14-day strict window)

### Факты

1. Strict window gate падает, если окно неполное.
2. Strict window gate падает, если внутри последних 14 закрытых дней есть красный день.
3. В официальной сертификации incomplete-window bypass запрещён.
4. Поэтому **official Day 01 не может стартовать с пустой истории**.
5. Следовательно, официальному циклу обязан предшествовать **реальный pre-cert anchor из 14 rehearsal days**.

### Действия

1. Держать строгую среду:
   - `HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE=false`
2. Закрыть 14 rehearsal/pre-cert days реальными дневными снапшотами.
3. Первые 13 дней допускается закрывать как `GREEN_ANCHOR_PENDING`, если соблюдены правила раздела 4.
4. 14-й rehearsal day обязан закрыться как fully green с `3.2 = PASS`.
5. Только после этого pre-cert anchor считается замкнутым, архивируется и становится основанием для official Day 01.
6. Исторические снапшоты нельзя редактировать вручную.
7. Разрешены только реальные новые проходы дня.

### Acceptance

1. До official Day 01:
   - `scripts/run_generation_parity_window_gate.ps1 -WindowDays 14` возвращает:
     - `Passed: true`
     - `WindowComplete: true`
2. Во время official run moving last-14 window остаётся зелёным каждый день.
3. Последние 14 закрытых дней содержат ноль нарушений.

---

## 7. Строгая модель двух контуров

Документ использует **двухконтурную схему**, но теперь она формализована без циклической двусмысленности.

## 7.1 Loop A — Pre-certification hardening

### Цель

Доказать, что дневной пакет может проходиться повторяемо и что 14-дневное strict window реально собирается честным способом.

### Разрешения

1. Code changes разрешены.
2. Фиксы, тесты и стабилизация разрешены.
3. Повтор rehearsal day разрешён после исправления причины сбоя.

### Обязательные правила

1. Каждый fail обязан порождать:
   - incident,
   - root cause,
   - fix,
   - preventive action.
2. `R-Day-01 ... R-Day-13` могут быть закрыты только как:
   - `GREEN_ANCHOR_PENDING`, либо
   - `FULLY_GREEN`, если strict window уже по факту замкнулся.
3. `R-Day-14` обязан быть закрыт как `FULLY_GREEN`.
4. Ни один closed rehearsal day внутри активного pre-cert цикла не может оставаться красным.
5. Никакой synthetic backfill не допускается.
6. Никакой manual snapshot editing не допускается.

### Условие выхода из Loop A

Loop A считается завершённым **только если одновременно выполнены все условия**:

1. `R-Day-01 ... R-Day-13` закрыты как `GREEN_ANCHOR_PENDING` или `FULLY_GREEN`.
2. `R-Day-14` закрыт как `FULLY_GREEN`.
3. Последний strict rolling window report зелёный:
   - `Passed = true`
   - `WindowComplete = true`
4. Locked pre-cert anchor архивирован как неизменяемая ссылка.

## 7.2 Loop B — Official 14-day certification run

### Цель

Получить 14 последовательных официальных календарных дней, где каждый день fully green, а движущееся strict window не разрушается.

### Правила

1. Включается code freeze для некритических изменений.
2. Official Day 01 разрешён только если locked pre-cert anchor уже существует, архивирован и зелёный на старте.
3. Каждый официальный день обязан быть fully green.
4. Любой `FAIL`:
   - день маркируется failed;
   - инцидент открывается;
   - официальный счётчик сбрасывается в `0`.
5. Повторный старт с official Day 01 разрешён только после:
   - устранения блокера,
   - проверки фикса,
   - возврата `3.2` в зелёный strict moving-window mode.

---

## 8. Формальная модель `3.2` без логической ошибки

Ниже зафиксирована окончательная интерпретация `3.2`:

1. `3.2` — это **не стартовый daily gate**, а **strict rolling-window gate**.
2. На pre-cert днях `R-Day-01 ... R-Day-13` он может иметь статус **ANCHOR_PENDING**, но только если:
   - все функциональные пакеты зелёные;
   - причина неуспеха `3.2` — только неполное окно;
   - внутри активного rehearsal-цикла нет закрытых красных дней.
3. На `R-Day-14` `3.2` обязан стать `PASS`.
4. После завершения Loop A official Day 01 разрешён только на следующем календарном дне.
5. На каждом official day `3.2` вычисляется по moving last-14 closed daily snapshots.
6. До official Day 14 это окно содержит:
   - хвост зелёного pre-cert anchor,
   - плюс накопленные official days.
7. Начиная с official Day 14 окно содержит только official days.
8. Любой красный официальный день отравляет moving window, как только попадает в набор последних 14 дней.
9. Reset counter не отменяет требования strict window.
10. Incomplete-window bypass, synthetic backfill и manual snapshot editing запрещены для official days.

Это означает, что здесь **нет требования, чтобы первые пять или тринадцать дней зависели от уже завершённого шестого или четырнадцатого дня**. Вместо этого существует честный bootstrap:

- сначала накапливается anchor,
- затем strict window реально замыкается,
- только потом начинается официальный отсчёт.

---

## 9. Day-by-day protocol before official run

Для каждого rehearsal day профиля `R-Day-01 ... R-Day-14`:

1. Выполнить полный пакет `3.1 .. 3.5`.
2. Для `R-Day-01 ... R-Day-13`:
   - разрешить `3.2 = ANCHOR_PENDING` вместо generic `FAIL`,
   - но только если причина — неполное окно pre-cert anchor.
3. Требовать зелёности всех functional package gates для rehearsal day.
4. Если любой functional package красный, тот же rehearsal day не закрывается и должен быть повторён только после исправления.
5. День помечается как closed только в одном из допустимых статусов:
   - `GREEN_ANCHOR_PENDING` для `R-Day-01 ... R-Day-13`;
   - `FULLY_GREEN` для `R-Day-14`.

### Promotion to official run

Переход в официальный цикл разрешён только если одновременно выполнены все условия:

1. Все rehearsal day profiles закрыты.
2. Последний strict rolling window gate зелёный.
3. Locked pre-cert anchor заархивирован и неизменяем.
4. Release approver подписал Go на official Day 01.

---

## 10. Daily command set (strict mode)

Корень артефактов ниже показывается как `doc/certification_<OFFICIAL_DAY14_UTC>/...` и должен быть заменён активной директорией официального цикла.

```powershell
dotnet build Helper.sln -c Debug
dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj -c Debug --no-build

powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_gate.ps1 -ReportPath "doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/HELPER_PARITY_GATE_dayXX.md"

$env:HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE = "false"
powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_window_gate.ps1 -WindowDays 14 -ReportPath "doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/HELPER_PARITY_WINDOW_GATE_dayXX.md"

powershell -ExecutionPolicy Bypass -File scripts/run_smoke_generation_compile_pass.ps1 -Runs 50 -TimeoutSec 120 -Prompt "Generate a minimal C# WPF TODO app with model, interface and service. Keep blueprint compact and compile-oriented."

powershell -ExecutionPolicy Bypass -File scripts/run_closed_loop_predictability.ps1 -IncidentCorpusPath "eval/incident_corpus.jsonl" -ReportPath "doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/CLOSED_LOOP_PREDICTABILITY_dayXX.md"

powershell -ExecutionPolicy Bypass -File scripts/run_eval_gate.ps1
powershell -ExecutionPolicy Bypass -File scripts/run_eval_real_model.ps1 -DatasetPath "eval/human_level_parity_ru_en.jsonl" -MaxScenarios 200 -OutputReport "doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/EVAL_REAL_MODEL_dayXX.md"
powershell -ExecutionPolicy Bypass -File scripts/generate_human_parity_report.ps1 -InputCsv "eval/human_eval_scores.csv" -OutputReport "doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/HUMAN_PARITY_dayXX.md" -FailOnThresholds
```

---

## 11. Governance and change control

### Обязательные правила управления

1. Никаких `-NoFailOnThreshold*` в certification.
2. Никакого incomplete-window bypass в official run.
3. Каждый фикс обязан иметь test coverage и evidence artifact.
4. Никакого manual snapshot backfill.
5. Никакого переписывания anchor days.
6. Ежедневный summary обязателен:
   - `doc/certification_<OFFICIAL_DAY14_UTC>/day-XX/DAILY_CERT_SUMMARY_dayXX.md`
7. Любой открытый P0/P1 блокирует следующий официальный день.

---

## 12. Completion criteria

Программа считается завершённой **только если одновременно выполнены все условия**:

1. Official `14/14` days passed consecutively.
2. Rolling 14-day strict window зелёный.
3. Human + auto eval thresholds зелёные.
4. Golden-template routing/promotion/certification остаётся стабильным без rollback-critical incidents.
5. Выпущен финальный отчёт:
   - `doc/HELPER_100_PERCENT_READINESS_REPORT.md`

---

## 13. Краткий итог логической коррекции

В этой переписанной версии устранены три источника прежней двусмысленности:

1. Убрано противоречие между фразами вида:
   - «`3.2` must be closed first»
   - и одновременно «`R-Day-01 ... R-Day-13` можно закрывать без полного `3.2`».

   Теперь корректная формулировка такая:
   - `3.2` обязан быть **полностью закрыт к завершению Loop A**, а не в первый же rehearsal day.

2. Убрано смешение понятий:
   - `all KPI green`
   - `functional green`
   - `anchor pending`.

   Теперь это три разные формальные категории.

3. Устранено ощущение замкнутого круга, где первые дни зависят от последнего.

   Теперь модель причинности такова:
   - сначала накапливаются реальные rehearsal days,
   - затем на 14-м дне strict window честно замыкается,
   - затем стартует официальный цикл.

Это делает документ логически целостным и пригодным для прямого использования как operational certification plan.
