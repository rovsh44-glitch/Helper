# HELPER: развёрнутый backlog устранения дефектов

Дата: `2026-04-10`
Статус реализации: `implementation completed on 2026-04-11`

Closure:

1. Итог реализации и верификации зафиксирован в `doc/analysis/HELPER_REMEDIATION_CLOSURE_2026-04-11.md`.
2. Все defect-блоки из backlog реализованы в коде, тестах и governance-артефактах.
3. Полный `ci:gate` теперь проходит remediation-related path и упирается только в `Generation parity gate`, который отражает текущее canonical parity truth-state, а не незакрытый remediation defect.

Основание:

1. `doc/analysis/HELPER_CRITICAL_ANALYSIS_2026-04-10.md`
2. `doc/analysis/HELPER_REMEDIATION_PLAN_2026-04-10.md`

## Назначение документа

Этот файл переводит remediation-план в рабочий execution backlog:

1. что делать;
2. в каком порядке;
3. в каких файлах;
4. чем подтверждать завершение;
5. какие зависимости есть между задачами.

## Принцип исполнения

Работать лучше пакетами:

1. сначала вернуть достоверный feedback loop;
2. потом чинить orchestration и scanner;
3. потом делать архитектурный refactor provider runtime;
4. в конце ужесточать security/infrastructure gates.

## Глобальные milestone

### Milestone A. Репозиторий снова даёт честный feedback

Состояние milestone:

1. `check_env_governance.ps1` зелёный;
2. `ci:gate` не падает на stale generated docs;
3. solution build либо исправлен, либо честно разделён;
4. `secret_scan.ps1` больше не даёт ложный `PASS`.

### Milestone B. Provider switching реально работает

Состояние milestone:

1. active profile меняет фактический transport;
2. `OpenAI-Compatible` работает по отдельному transport path;
3. нет функциональной зависимости от process-wide env mutation;
4. есть integration tests и ручной smoke.

### Milestone C. Gates снова заслуживают доверия

Состояние milestone:

1. `ci:gate` зелёный;
2. `nuget_security_gate.ps1` честно различает vulnerability result и infra failure;
3. closure report подтверждает исправление всех findings.

## Фаза 0. Подготовка и baseline

### Задача 0.1. Собрать baseline-логи

Цель:

1. иметь точку сравнения до любых исправлений.

Файлы/директории:

1. `temp/analysis/remediation-baseline/`

Команды:

1. `dotnet build Helper.sln`
2. `dotnet build src/Helper.Api/Helper.Api.csproj`
3. `dotnet build src/Helper.Runtime/Helper.Runtime.csproj`
4. `dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj --no-build`
5. `dotnet test test/Helper.Runtime.Api.Tests/Helper.Runtime.Api.Tests.csproj`
6. `dotnet test test/Helper.RuntimeSlice.Api.Tests/Helper.RuntimeSlice.Api.Tests.csproj`
7. `npm run build`
8. `npm run ci:gate`
9. `powershell -ExecutionPolicy Bypass -File scripts/secret_scan.ps1`
10. `powershell -ExecutionPolicy Bypass -File scripts/nuget_security_gate.ps1`

Выход:

1. текстовые логи;
2. краткая сводка `PASS/FAIL` по каждой команде.

Критерий завершения:

1. все baseline-сигналы сохранены;
2. нет секретов в логах.

Зависимости:

1. нет.

### Задача 0.2. Завести remediation checklist

Цель:

1. превратить findings в измеримые acceptance criteria.

Файлы:

1. можно добавить `temp/analysis/remediation-baseline/CHECKLIST.md`

Что зафиксировать:

1. как воспроизводится defect;
2. какая команда доказывает исправление;
3. какой тест защищает от регресса.

Критерий завершения:

1. для каждого defect есть `repro`, `fix proof`, `regression proof`.

## Фаза 1. Вернуть синхронность config governance

### Задача 1.1. Перегенерировать env-артефакты

Цель:

1. убрать stale state из generated docs.

Файлы:

1. `scripts/generate_env_reference.ps1`
2. `doc/config/ENV_REFERENCE.md`
3. `doc/config/ENV_INVENTORY.json`
4. `.env.local.example`

Команда:

1. `powershell -ExecutionPolicy Bypass -File scripts/generate_env_reference.ps1`

Что проверить руками:

1. появились `HELPER_MODEL_LONG_CONTEXT`
2. появились `HELPER_MODEL_DEEP_REASONING`
3. появились `HELPER_MODEL_VERIFIER`
4. появились `HELPER_WEB_SEARCH_LOCAL_URL`
5. появились `HELPER_WEB_SEARCH_SEARX_URL`

Критерий завершения:

1. generated files обновлены;
2. содержимое соответствует `BackendEnvironmentInventory.cs`, а не только timestamp.

Зависимости:

1. задача 0.1.

### Задача 1.2. Вернуть зелёный `check_env_governance`

Файлы:

1. `scripts/check_env_governance.ps1`
2. `doc/config/ENV_REFERENCE.md`
3. `doc/config/ENV_INVENTORY.json`
4. `.env.local.example`

Команда:

1. `powershell -ExecutionPolicy Bypass -File scripts/check_env_governance.ps1`

Критерий завершения:

1. скрипт проходит без `stale` ошибок.

Зависимости:

1. задача 1.1.

### Задача 1.3. Добавить enforcement на generated docs

Цель:

1. не допустить повторения дрейфа.

Файлы:

1. `scripts/check_env_governance.ps1`
2. `scripts/ci_gate.ps1`
3. возможно `.github/workflows/...`
4. возможно новый helper script, например `scripts/check_generated_env_artifacts.ps1`

Рекомендуемое решение:

1. запускать generation в `temp/`;
2. сравнивать output с tracked файлами;
3. падать, если diff не пустой.

Критерий завершения:

1. PR с изменением inventory без regeneration гарантированно красный.

Зависимости:

1. задача 1.2.

### Задача 1.4. Повторно прогнать `ci:gate`

Команда:

1. `npm run ci:gate`

Критерий завершения:

1. `ci:gate` больше не падает на `Config governance`.

Зависимости:

1. задачи 1.1-1.3.

## Фаза 2. Починить `Helper.sln`

### Задача 2.1. Принять решение по пяти тестовым проектам

Проблемные проекты:

1. `Helper.Runtime.Api.Tests`
2. `Helper.Runtime.Integration.Tests`
3. `Helper.Runtime.Browser.Tests`
4. `Helper.Runtime.Certification.Tests`
5. `Helper.Runtime.Certification.Compile.Tests`

Цель:

1. определить, должны ли они участвовать в canonical solution build.

Файлы:

1. `Helper.sln`
2. возможно новый `.slnf` или новая `.sln`

Варианты:

1. включить все пять в canonical solution;
2. вынести тяжёлые тестовые пакеты в отдельную solution;
3. оставить canonical solution “быстрой”, а тяжёлые suites вынести отдельно и явно документировать.

Критерий завершения:

1. принято одно явное решение;
2. оно отражено в структуре решения, а не только в комментариях.

Зависимости:

1. задача 0.1.

### Задача 2.2. Исправить `ProjectConfigurationPlatforms`

Цель:

1. убрать solution-level failure без compile diagnostics.

Файлы:

1. `Helper.sln`

Что сделать:

1. добавить отсутствующие `Build.0`, если проект должен строиться;
2. либо удалить неконсистентные `ActiveCfg`, если проект intentionally excluded;
3. выровнять матрицу `Debug/Release` и `Any CPU/x64`.

Критерий завершения:

1. `dotnet build Helper.sln` перестаёт падать;
2. поведение solution предсказуемо.

Зависимости:

1. задача 2.1.

### Задача 2.3. Добавить ранний CI smoke-check на solution build

Файлы:

1. `scripts/ci_gate.ps1`
2. возможно `.github/workflows/...`

Что сделать:

1. добавить ранний шаг `dotnet build Helper.sln -v minimal`;
2. ставить его до тяжёлых parity/eval/generation стадий.

Критерий завершения:

1. solution drift ловится в начале pipeline.

Зависимости:

1. задача 2.2.

### Задача 2.4. Подтвердить solution repair

Команды:

1. `dotnet build Helper.sln`
2. `dotnet build src/Helper.Api/Helper.Api.csproj`
3. `dotnet build src/Helper.Runtime/Helper.Runtime.csproj`

Критерий завершения:

1. solution build зелёный;
2. project build зелёный;
3. новый smoke-check тоже зелёный.

Зависимости:

1. задачи 2.2-2.3.

## Фаза 3. Исправить `secret_scan.ps1`

### Задача 3.1. Создать test fixtures для scanner

Цель:

1. перевести работу scanner из “ручной веры” в воспроизводимую верификацию.

Директория:

1. `temp/analysis/secret-scan-fixtures/`

Fixtures:

1. `env_unquoted_api_key.env`
2. `env_unquoted_session_key.env`
3. `env_placeholder.env`
4. `env_quoted_secret.env`
5. `env_commented_secret.env`
6. `env_empty_value.env`

Критерий завершения:

1. fixtures покрывают оба defect-сценария и минимум два anti-false-positive сценария.

Зависимости:

1. задача 0.1.

### Задача 3.2. Добавить паттерны для unquoted `.env` значений

Файлы:

1. `scripts/secret_scan.ps1`

Что изменить:

1. оставить поддержку quoted secrets;
2. добавить `HELPER_API_KEY` для unquoted format;
3. добавить `HELPER_SESSION_SIGNING_KEY` для unquoted format.

Критерий завершения:

1. scanner ловит секреты из обычного `.env` формата.

Зависимости:

1. задача 3.1.

### Задача 3.3. Уточнить allowlist

Файлы:

1. `scripts/secret_scan.ps1`

Что сделать:

1. оставить placeholder-allowlist;
2. не позволить allowlist “съедать” реальные длинные значения;
3. отдельно проверить session secrets.

Критерий завершения:

1. placeholder-строки не дают false positive;
2. реальные секреты не пропускаются.

Зависимости:

1. задача 3.2.

### Задача 3.4. Добавить regression harness

Файлы:

1. `scripts/secret_scan.ps1`
2. новый test script или test project

Рекомендуемый вариант:

1. PowerShell harness, который поднимает временный fixture tree и запускает scanner как subprocess.

Минимальные проверки:

1. unquoted `HELPER_API_KEY` => fail
2. unquoted `HELPER_SESSION_SIGNING_KEY` => fail
3. `<set-me>` => pass
4. `.env.local.example` => pass

Критерий завершения:

1. scanner behaviour закреплён тестами.

Зависимости:

1. задачи 3.2-3.3.

### Задача 3.5. Принять policy по `.env.local`

Цель:

1. определить, чего проект на самом деле хочет от scanner.

Нужно выбрать:

1. `repo-only scan`
2. `workspace hygiene scan`
3. оба режима через переключатель

Рекомендуемый вариант:

1. поддержать два режима;
2. в CI использовать `repo-only`;
3. локально оператору давать `workspace hygiene`.

Файлы:

1. `scripts/secret_scan.ps1`
2. `scripts/ci_gate.ps1`
3. `README.md`
4. `doc/security/...`

Критерий завершения:

1. policy зафиксирована кодом и документацией.

Зависимости:

1. задача 3.4.

### Задача 3.6. Подтвердить scanner fix

Команды:

1. `powershell -ExecutionPolicy Bypass -File scripts/secret_scan.ps1`
2. запуск regression harness

Критерий завершения:

1. scanner больше не даёт ложный `PASS` на реальном `.env.local`;
2. `.env.local.example` остаётся зелёным.

Зависимости:

1. задачи 3.2-3.5.

## Фаза 4. Перестроить provider runtime

Это главный архитектурный блок. Он должен идти после восстановления feedback loop.

### Задача 4.1. Зафиксировать целевую runtime architecture

Цель:

1. не чинить `ApplyToRuntime()` косметически;
2. сначала определить правильную модель.

Файлы:

1. новый ADR или design note в `doc/adr/` или `doc/architecture/`

Что описать:

1. active provider state
2. lifecycle transport client
3. routing between `Ollama` and `OpenAI-Compatible`
4. concurrency model
5. hot-switch semantics

Критерий завершения:

1. есть краткий approved design note.

Зависимости:

1. фазы 1-3 завершены.

### Задача 4.2. Ввести transport abstraction

Файлы:

1. `src/Helper.Api/Backend/ModelGateway/HelperModelGateway.cs`
2. `src/Helper.Api/Backend/Providers/...`
3. новые transport contracts/classes

Что сделать:

1. добавить `IModelTransportClient`;
2. добавить `IModelTransportFactory`;
3. добавить runtime accessor для active provider config.

Критерий завершения:

1. model gateway работает через abstraction, а не напрямую через singleton `AILink`.

Зависимости:

1. задача 4.1.

### Задача 4.3. Выделить Ollama transport

Файлы:

1. `src/Helper.Runtime/AILink.cs`
2. `src/Helper.Runtime/AILink.Chat.cs`
3. `src/Helper.Runtime/AILink.Http.cs`

Что сделать:

1. либо переименовать `AILink` в Ollama-specific client;
2. либо ограничить его явно одним transport-kind.

Критерий завершения:

1. в коде больше нет ложного впечатления, что `AILink` универсален.

Зависимости:

1. задача 4.2.

### Задача 4.4. Реализовать `OpenAI-Compatible` transport client

Файлы:

1. новый transport client class
2. возможно новые DTO/stream parsers

Что сделать:

1. discovery path через OpenAI-compatible API;
2. request path для response generation;
3. streaming path;
4. auth header wiring;
5. model/base URL binding.

Критерий завершения:

1. `OpenAI-Compatible` обслуживается своим transport client.

Зависимости:

1. задача 4.2.

### Задача 4.5. Убрать functional dependency от `Environment.SetEnvironmentVariable(...)`

Файлы:

1. `src/Helper.Api/Backend/Providers/ProviderProfileResolver.cs`
2. `src/Helper.Api/Backend/Providers/ProviderProfileActivationService.cs`
3. `src/Helper.Api/Backend/Providers/ProviderProfileCatalog.cs`

Что сделать:

1. перестать использовать process-wide env как runtime routing mechanism;
2. оставить env только как bootstrap defaults или telemetry marker.

Критерий завершения:

1. runtime switching работает без global env mutation.

Зависимости:

1. задачи 4.2-4.4.

### Задача 4.6. Перевести DI на новую модель lifetime

Файлы:

1. `src/Helper.Api/Hosting/ServiceRegistrationExtensions.Core.cs`
2. `src/Helper.Api/Hosting/ServiceRegistrationExtensions.ProvidersAndDiagnostics.cs`

Что сделать:

1. убрать singleton `AILink` как универсальный client;
2. зарегистрировать transport factory;
3. зарегистрировать active-provider accessor.

Критерий завершения:

1. последующие запросы используют новый active profile без перезапуска процесса;
2. нет mixed state при конкурентной работе.

Зависимости:

1. задачи 4.2-4.5.

### Задача 4.7. Добавить integration tests на provider activation

Файлы:

1. provider-related test project(s)

Минимальные тесты:

1. activate local provider => следующая операция идёт в local transport
2. activate OpenAI-compatible => следующая операция идёт в OpenAI-compatible transport
3. `DiscoverAsync` вызывается после активации
4. параллельные запросы не ловят partial switch
5. process-wide env не является functional dependency

Критерий завершения:

1. runtime switching защищён автотестами.

Зависимости:

1. задачи 4.4-4.6.

### Задача 4.8. Добавить API-level end-to-end test

Файлы:

1. API integration tests

Сценарий:

1. `POST /api/settings/provider-profiles/activate`
2. следующий runtime request
3. подтверждение фактического transport route

Критерий завершения:

1. endpoint и runtime связаны реальным поведением.

Зависимости:

1. задача 4.7.

### Задача 4.9. Ручной smoke на provider switching

Сценарии:

1. local -> OpenAI-compatible
2. OpenAI-compatible -> local
3. streaming
4. discovery
5. warmup

Критерий завершения:

1. ручной smoke повторяет test results и не выявляет расхождения.

Зависимости:

1. задача 4.8.

### Задача 4.10. Обновить docs по provider runtime

Файлы:

1. `README.md`
2. `doc/architecture/...`
3. `doc/security/...`
4. provider-related docs

Критерий завершения:

1. docs описывают фактическую архитектуру, а не старую env-driven модель.

Зависимости:

1. задачи 4.7-4.9.

## Фаза 5. Harden `nuget_security_gate.ps1`

### Задача 5.1. Разделить infra failure и security result

Файлы:

1. `scripts/nuget_security_gate.ps1`

Что сделать:

1. ввести разные исходы:
   - vulnerabilities found
   - audit data unavailable
   - restore/list hard failure

Критерий завершения:

1. `NU1900` больше не маскируется под тот же тип failure, что и реальные уязвимости.

Зависимости:

1. фазы 1-4 желательно завершены.

### Задача 5.2. Ввести execution mode

Файлы:

1. `scripts/nuget_security_gate.ps1`
2. `scripts/ci_gate.ps1`

Что сделать:

1. добавить `strict-online`;
2. добавить `best-effort-local`.

Критерий завершения:

1. поведение gate предсказуемо и зависит от режима, а не от случайного окружения.

Зависимости:

1. задача 5.1.

### Задача 5.3. Улучшить диагностику среды

Файлы:

1. `scripts/nuget_security_gate.ps1`

Что вывести:

1. источники NuGet;
2. наличие proxy env vars;
3. выбранный режим;
4. итоговый статус.

Критерий завершения:

1. оператор понимает, почему gate упал.

Зависимости:

1. задача 5.2.

### Задача 5.4. Добавить regression harness

Файлы:

1. `scripts/nuget_security_gate.ps1`
2. test harness/scripts

Сценарии:

1. simulated `NU1900`
2. simulated vulnerability report
3. clean report

Критерий завершения:

1. gate не регрессирует по semantics.

Зависимости:

1. задачи 5.1-5.3.

## Фаза 6. Финальная сверка

### Задача 6.1. Полный automated verification sweep

Команды:

1. `dotnet build Helper.sln`
2. `dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj`
3. `dotnet test test/Helper.Runtime.Api.Tests/Helper.Runtime.Api.Tests.csproj`
4. `dotnet test test/Helper.RuntimeSlice.Api.Tests/Helper.RuntimeSlice.Api.Tests.csproj`
5. `npm run build`
6. `powershell -ExecutionPolicy Bypass -File scripts/check_env_governance.ps1`
7. `powershell -ExecutionPolicy Bypass -File scripts/secret_scan.ps1`
8. `npm run ci:gate`

Если online environment доступен:

1. `powershell -ExecutionPolicy Bypass -File scripts/nuget_security_gate.ps1`

Критерий завершения:

1. весь baseline проблем закрыт;
2. нет новых красных шагов.

Зависимости:

1. фазы 1-5.

### Задача 6.2. Manual acceptance

Проверить:

1. settings UI
2. provider activation
3. runtime doctor
4. chat
5. stream

Критерий завершения:

1. ручной сценарий совпадает с автотестами.

Зависимости:

1. задача 6.1.

### Задача 6.3. Closure report

Файлы:

1. новый документ, например `doc/analysis/HELPER_REMEDIATION_CLOSURE_2026-04-XX.md`

Что включить:

1. список закрытых findings;
2. ссылки на коммиты/PR;
3. результаты команд;
4. оставшийся risk register.

Критерий завершения:

1. remediation formally closed.

Зависимости:

1. задача 6.2.

## Зависимости между фазами

### Жёсткие зависимости

1. фаза 1 должна завершиться до финальной проверки;
2. фаза 2 должна завершиться до closure;
3. фаза 3 должна завершиться до возврата доверия к hygiene layer;
4. фаза 4 должна завершиться до claim, что provider switching исправлен;
5. фаза 5 может идти позже, но должна завершиться до полного closure.

### Допустимый параллелизм

Можно параллелить:

1. фаза 2 и фаза 3;
2. часть фазы 5 с поздней частью фазы 4;
3. docs update в конце каждой фазы, если не мешает критическому пути.

Нежелательно параллелить:

1. refactor provider runtime и массовые изменения env inventory;
2. изменения `secret_scan.ps1` и общие security docs без завершённой policy;
3. изменение solution-структуры и добавление новых test projects.

## Рабочий порядок выполнения

### Wave 1

1. задача 0.1
2. задача 0.2
3. задача 1.1
4. задача 1.2
5. задача 1.3
6. задача 1.4

### Wave 2

1. задача 2.1
2. задача 2.2
3. задача 2.3
4. задача 2.4
5. задача 3.1
6. задача 3.2
7. задача 3.3
8. задача 3.4
9. задача 3.5
10. задача 3.6

### Wave 3

1. задача 4.1
2. задача 4.2
3. задача 4.3
4. задача 4.4
5. задача 4.5
6. задача 4.6
7. задача 4.7
8. задача 4.8
9. задача 4.9
10. задача 4.10

### Wave 4

1. задача 5.1
2. задача 5.2
3. задача 5.3
4. задача 5.4
5. задача 6.1
6. задача 6.2
7. задача 6.3

## Минимальный backlog для старта прямо сейчас

Если начинать с ближайших действий без дискуссий, первые конкретные шаги такие:

1. выполнить задачу 1.1 и закоммитить regenerated env artifacts;
2. выполнить задачи 2.1-2.2 и добиться зелёного `dotnet build Helper.sln`;
3. выполнить задачи 3.1-3.4 и сделать scanner testable;
4. только потом переходить к фазе 4.

## Definition of done по каждому defect-классу

### `config governance drift`

Done, если:

1. generated docs синхронны;
2. `check_env_governance.ps1` зелёный;
3. есть enforcement против повторного дрейфа.

### `Helper.sln broken`

Done, если:

1. `dotnet build Helper.sln` зелёный;
2. причина отсутствия `Build.0` устранена, а не замаскирована;
3. CI проверяет solution build рано.

### `secret_scan blind spot`

Done, если:

1. unquoted `.env` secrets ловятся;
2. placeholders не дают false positive;
3. есть regression harness;
4. policy `.env.local` зафиксирована.

### `provider runtime broken switching`

Done, если:

1. transport abstraction внедрена;
2. `OpenAI-Compatible` имеет отдельный client;
3. runtime switching не зависит от global env mutation;
4. есть integration tests и ручной smoke.

### `nuget gate fragility`

Done, если:

1. `NU1900` отделён от vulnerability findings;
2. есть режимы `strict-online` и `best-effort-local`;
3. логи объясняют причину провала.
