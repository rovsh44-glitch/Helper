# HELPER: глубокий комплексный критический аудит

Дата аудита: `2026-04-12`

## Итоговый вердикт

Проект выглядит инженерно зрелым по объему, покрытию и количеству встроенных gate-механизмов, но в текущем состоянии у него есть несколько системных проблем доверия к собственным сигналам качества.

Коротко:

- Базовая инженерная дисциплина в проекте сильная: `frontend:check`, `docs:check`, `config:check`, `security:scan:repo`, `build`, `ci:gate` и выборочные `dotnet test` проходят.
- Кодовая база большая и уже сложная: `1002` файлов кода, `122821` строк; из них `708` source-файлов и `189` test-файлов.
- Главный риск не в том, что проект “не работает”, а в том, что часть green-сигналов сегодня ненадежна или неполна.

Моя оценка:

- инженерная база: `выше средней`
- архитектурная управляемость: `средняя`
- доверие к release-gate без доработок: `среднее`
- доверие к runtime-security posture: `среднее, с условно опасными legacy-путями`

## Что я проверил

Проверены:

- структура репозитория, solution, csproj, frontend/backend composition
- архитектурная документация и DI-композиция
- ключевые runtime и API entrypoints
- гейты сборки, конфигурации, документации и безопасности
- выборочные тестовые контуры
- покрытие solution и соответствие реальному дереву проектов

Запущенные проверки:

- `npm run frontend:check`
- `npm run docs:check`
- `npm run config:check`
- `npm run build`
- `npm run security:scan:repo`
- `npm run ci:gate`
- `dotnet build src/Helper.Api/Helper.Api.csproj`
- `dotnet build src/Helper.Runtime/Helper.Runtime.csproj`
- `dotnet build src/Helper.Runtime.WebResearch.Browser/Helper.Runtime.WebResearch.Browser.csproj`
- `dotnet build src/Helper.RuntimeLogSemantics/Helper.RuntimeLogSemantics.csproj`
- `dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj --no-restore`
- `dotnet test test/Helper.Runtime.Api.Tests/Helper.Runtime.Api.Tests.csproj --no-restore`
- `dotnet test test/Helper.RuntimeSlice.Api.Tests/Helper.RuntimeSlice.Api.Tests.csproj --no-restore`
- `dotnet test test/Helper.Runtime.CompilePath.Tests/Helper.Runtime.CompilePath.Tests.csproj --no-restore`

Результаты выборочных тестов:

- `Helper.Runtime.Tests`: `214/214` passed
- `Helper.Runtime.Api.Tests`: `169/169` passed
- `Helper.RuntimeSlice.Api.Tests`: `4/4` passed
- `Helper.Runtime.CompilePath.Tests`: `20/20` passed
- внутри `ci:gate` также прошли browser/eval/tool-benchmark lanes

Ограничения аудита:

- я не запускал `ci:gate:heavy`
- NuGet vulnerability audit в текущей среде не был авторитетным из-за недоступности источника аудита и завершился как `audit_degraded_infrastructure_unavailable`

## Количественный снимок

- code files under `src/components/services/hooks/contexts/test`: `1002`
- total lines: `122821`
- source files under `src`: `708`
- source lines: `80797`
- test files under `test`: `189`
- test lines: `28400`
- `*.csproj` в `src` и `test`: `19`
- проектов, реально включенных в `Helper.sln`: `16`

Самые тяжелые файлы по размеру среди исходников без `bin/obj`:

- `test/Helper.Runtime.Tests/ConversationRuntimeTests.cs`
- `test/Helper.Runtime.Tests/RetrievalPipelineTests.cs`
- `src/Helper.Api/Backend/Configuration/BackendEnvironmentInventory.cs`
- `services/generatedApiClient.ts`
- `hooks/useSettingsViewState.ts`

Это уже говорит о росте когнитивной сложности и затрат на сопровождение.

## Главные findings

### 1. High: OpenAPI contract gate сейчас дает ложнозеленый результат

Суть:

- `scripts/openapi_gate.ps1:16-31` запускает `dotnet test` с фильтром `Category=Contract`, но считает gate успешным только по `$LASTEXITCODE`.
- В проекте уже есть более строгий общий helper `scripts/common/StrictDotnetFilteredTest.ps1:74-108`, который явно считает `"No test matches the given testcase filter"` ошибкой.
- В реальном прогоне `npm run ci:gate` на этом аудите OpenAPI step вывел:
  - `No test matches the given testcase filter Category=Contract`
  - затем всё равно `"[OpenApiGate] Passed."`

Доказательства:

- `scripts/openapi_gate.ps1:16-31`
- `scripts/common/StrictDotnetFilteredTest.ps1:74-108`

Почему это критично:

- Сейчас проект формально заявляет наличие contract gate, но фактически может не проверять ничего.
- Это снижает доверие к стабильности `services/generatedApiClient.ts` и к соответствию `doc/openapi_contract_snapshot.json` реальному backend contract.

Что исправить:

- перевести `openapi_gate.ps1` на `Invoke-StrictDotnetFilteredTest` и `Assert-StrictDotnetFilteredTestSucceeded`
- добавить отдельный hard fail, если matched tests = `0`
- добавить хотя бы один явный тест с `Category=Contract`

### 2. High: проверка coverage solution слепа к проектам, вообще не включенным в `Helper.sln`

Суть:

- В репозитории `19` файлов `*.csproj`, а в `Helper.sln` включено только `16`.
- Не включены:
  - `test/Helper.Runtime.CompilePath.Tests/Helper.Runtime.CompilePath.Tests.csproj`
  - `src/Helper.Runtime.WebResearch.Browser/Helper.Runtime.WebResearch.Browser.csproj`
  - `src/Helper.RuntimeLogSemantics/Helper.RuntimeLogSemantics.csproj`
- `scripts/check_solution_build_coverage.ps1:22-50` анализирует только те проекты, которые уже найдены внутри `.sln`, и поэтому не может обнаружить “выпавшие” проекты.
- При этом минимум один такой выпавший тестовый контур не декоративный: `Helper.Runtime.CompilePath.Tests` успешно запускается отдельно и содержит `20` passing tests.

Доказательства:

- `Helper.sln:8-40`
- `scripts/check_solution_build_coverage.ps1:22-50`
- сравнение дерева `*.csproj` с `.sln` в ходе аудита

Почему это критично:

- Зеленый `solution:coverage` сейчас не означает полный охват build/test graph.
- Риск особенно неприятен тем, что он process-level: команда может считать, что “всё в solution покрыто”, но часть реальной кодовой поверхности остается за периметром.

Что исправить:

- добавить в `check_solution_build_coverage.ps1` сравнение:
  - `rg --files src test -g "*.csproj"`
  - against `Helper.sln`
- сделать hard fail при наличии проектов вне solution
- определить политику:
  - либо все живые проекты обязаны быть в `.sln`
  - либо нужен отдельный явный manifest исключений с причиной

### 3. High: `/api/architecture/plan` использует planner с молчаливым ложным fallback

Суть:

- `SimplePlanner` зарегистрирован как production `IProjectPlanner`: `src/Helper.Api/Hosting/ServiceRegistrationExtensions.Core.cs:25-28`.
- Endpoint `/api/architecture/plan` реально использует этот интерфейс: `src/Helper.Api/Hosting/EndpointRegistrationExtensions.Evolution.Strategy.cs:44-57`.
- При любой ошибке десериализации ответа LLM planner не возвращает ошибку и не маркирует degraded state.
- Вместо этого он молча подставляет фиксированный WPF-план из двух файлов:
  - `MainWindow.xaml`
  - `MainWindow.xaml.cs`

Доказательства:

- `src/Helper.Runtime/SimplePlanner.cs:24-32`
- `src/Helper.Api/Hosting/ServiceRegistrationExtensions.Core.cs:25-28`
- `src/Helper.Api/Hosting/EndpointRegistrationExtensions.Evolution.Strategy.cs:44-57`

Почему это критично:

- Это не “fallback для dev-mode”, а ложный успешный ответ.
- Любой upstream consumer может принять такой plan как валидный architectural result.
- Ошибка здесь особенно опасна, потому что она тиха: система не сигнализирует о деградации, а подсовывает неверный artifact.

Что исправить:

- убрать silent fallback полностью
- возвращать structured failure envelope с причиной parse/validation failure
- разрешить fallback только в виде явно маркированного `degradedPlan`
- покрыть это route-level тестом на `/api/architecture/plan`

### 4. Medium-High: в DI до сих пор подключены prototype/legacy реализации вместо production-grade контрактов

Суть:

- Core DI регистрирует:
  - `SimplePlanner`
  - `SimpleCoder`
  - `PythonSandbox`
  - `SimpleTestGenerator`
- Это видно в `src/Helper.Api/Hosting/ServiceRegistrationExtensions.Core.cs:25-28`.
- Из них как минимум:
  - `PythonSandbox` всегда возвращает успех и ничего реально не исполняет: `src/Helper.Runtime/SupportingImplementations.cs:35-39`
  - `SimpleCoder` навязывает WPF/CommunityToolkit-specific guidance и using directives даже на общем контракте code generation: `src/Helper.Runtime/SimpleCoder.cs:26-77`

Почему это важно:

- Даже если часть этих путей не является текущим hot path, их наличие в основном DI-контуре означает высокий риск случайного использования через новые endpoints, refactor или composition drift.
- Это не безопасная “лабораторная зона”, а production composition root.

Что исправить:

- разделить prototype implementations и production implementations по разным composition roots
- либо убрать эти реализации из `ServiceRegistrationExtensions.Core.cs`
- либо явно маркировать их feature-flag'ами и runtime ban outside lab/dev mode

### 5. Medium: generic shell execution path условно опасен, если его включить

Суть:

- Tool registry публикует `shell_execute`: `src/Helper.Runtime/Infrastructure/BuiltinToolRegistry.cs:28-36`
- Tool service реально вызывает handler: `src/Helper.Runtime/ToolService.cs:43-64`
- Handler исполняет shell-команду через `ShellExecutor`: `src/Helper.Runtime/Infrastructure/ToolExecutionGateway.cs:48-72`
- `ToolPermitService` действительно держит `shell_execute` выключенным по умолчанию, если `HELPER_ALLOW_SHELL_TOOLS` не включён: `src/Helper.Runtime/ToolPermitService.cs:77-85`
- Но если флаг включить, `ProcessGuard` пропускает целые интерпретаторы `pwsh`, `powershell`, `python`, `node`, `cmd` по имени процесса и почти не анализирует их полезную нагрузку: `src/Helper.Runtime/ProcessGuard.cs:18-20`, `33-76`

Почему это важно:

- Сейчас это latent risk, а не active exploit path, потому что default policy защищает систему.
- Но безопасность здесь держится на одном env flag, а не на robust parser-level command policy.
- При включении `shell_execute` достаточно обернуть опасное действие в разрешённый интерпретатор, чтобы обойти большинство статических проверок.

Что исправить:

- по возможности удалить `shell_execute` как generic tool
- оставить только структурированные инструменты (`dotnet_test`, `read_file`, `write_file`, и т.д.)
- если shell всё же нужен:
  - разрешать только командные схемы, а не raw command string
  - разбирать payload для `powershell/cmd/python/node`
  - ввести deny-list не только по base command, но и по подкомандам/аргументам

## Дополнительные риски и наблюдения

### Build/Release

- `ci:gate` в целом проходит, но один из его шагов уже сейчас ложнозеленый.
- NuGet audit в текущем прогоне завершился как `audit_degraded_infrastructure_unavailable`, значит supply-chain confidence сейчас не авторитетен.
- В `Directory.Build.props` при sandbox/codex-сценариях `NuGetAudit` отключается: `Directory.Build.props` снижает наблюдаемость зависимостей в ограниченных средах.

### Архитектура и сложность

- `Helper.Api` разросся до очень крупного host/runtime boundary с большим количеством policy-классов и partial endpoint registrations.
- В `src/Helper.Api/Hosting` широко используется `#pragma warning disable` для nullability-предупреждений в endpoint registration файлах. Это не баг само по себе, но это сигнал того, что типовая поверхность стала слишком сложной для строгого static safety.
- Самые крупные тестовые файлы уже монолитны. Например, `ConversationRuntimeTests.cs` и `RetrievalPipelineTests.cs` заметно усложняют локальную диагностику и точечную навигацию.

### Тестовая стратегия

- Тестовое покрытие количественно сильное.
- Но сейчас есть перекос: тестов много, а качество самих gate-сигналов неоднородно.
- Иными словами, проблема не “тестов мало”, а “не все тесты правильно включены в release semantics”.

### Security / trust model

- Secret scan в repo mode проходит.
- Browser auth/session boundary в целом выглядит продуманно: scoped session bootstrap, dedicated session signing key governance, startup validation guards.
- Однако security posture портится там, где рядом с сильными guard'ами остаются legacy execution primitives.

### Репозиторий и hygiene

- `.gitignore` в целом адекватен: `bin/obj/dist/node_modules/temp/artifacts/showcase_repo` и runtime-debris исключены.
- В рабочем дереве присутствуют `node_modules`, `dist`, локальный `.env.local`, но по итогам проверки они не выглядят как проблема git-tracking.
- Пустые директории `src/SelfEvolvingAI.Infrastructure*` выглядят как исторический шум и должны быть либо удалены, либо объяснены.

## Сильные стороны проекта

- Сильная дисциплина по docs/config/security gates.
- Неплохая архитектурная документация и честный статус-подход в `README`.
- Есть реальный, не игрушечный test surface по runtime/API/eval/compile-path.
- Frontend boundary контролируется отдельным gate и в текущем состоянии не деградировал.
- Есть попытка строить trustworthy local-first perimeter вместо безусловно доверенной браузерной модели.

## Приоритетный план исправлений

### P0

1. Починить `openapi_gate.ps1`, чтобы отсутствие matched tests было hard fail.
2. Починить `check_solution_build_coverage.ps1`, чтобы он ловил проекты вне `.sln`.
3. Убрать silent fallback из `SimplePlanner`.

### P1

1. Разделить prototype/legacy сервисы и production composition root.
2. Провести инвентаризацию всех сервисов, зарегистрированных в `ServiceRegistrationExtensions.Core.cs`.
3. Зафиксировать, какие реализации допустимы в production, а какие только в lab/dev.

### P2

1. Убрать generic `shell_execute` или радикально ужесточить policy.
2. Сократить broad nullability suppressions в endpoint registration слое.
3. Декомпозировать крупнейшие test/source hot-spots.

## Короткий вывод

HELPER не выглядит как “сломанный” проект. Он выглядит как быстро выросшая сложная система, где основная угроза уже не в отсутствии функциональности, а в несоответствии между декларируемыми quality gates и их фактической надежностью.

Главная задача сейчас не “дописать ещё больше логики”, а вернуть доверие к инженерному контуру:

- чтобы gate действительно проверял то, что обещает;
- чтобы production routes не зависели от silent prototype fallback'ов;
- чтобы реальные проекты и тесты не выпадали из solution-perimeter.

Пока эти три вещи не исправлены, зеленый статус проекта нельзя считать полностью репрезентативным.
