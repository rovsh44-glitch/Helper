# HELPER: пошаговый план устранения дефектов

Дата: `2026-04-10`
Основание: `doc/analysis/HELPER_CRITICAL_ANALYSIS_2026-04-10.md`

## Цель плана

Закрыть все дефекты и ошибки, зафиксированные в критическом анализе:

1. недоведённое runtime-переключение `provider profile`;
2. сломанный `Helper.sln`;
3. устаревшие `config-governance` артефакты;
4. blind spot в `secret_scan.ps1`;
5. хрупкость `nuget_security_gate.ps1` в offline/proxy-среде.

## Важное разграничение

### Приоритет риска

По опасности дефектов приоритет такой:

1. `provider runtime architecture`
2. `Helper.sln`
3. `config governance drift`
4. `secret_scan blind spot`
5. `NuGet audit gate resilience`

### Приоритет исполнения

Для безопасной реализации лучше сначала восстановить инженерный feedback loop:

1. вернуть зелёный/достоверный локальный gate;
2. починить `Helper.sln`;
3. закрыть blind spot в `secret_scan.ps1`;
4. затем делать глубокий refactor `provider runtime`;
5. в конце ужесточить `NuGet`-gate и закрыть release-criteria.

То есть риск и порядок исполнения здесь не совпадают. Это нормально.

## Целевое конечное состояние

Работа считается завершённой только если одновременно выполнены все условия:

1. `dotnet build Helper.sln` проходит стабильно.
2. `npm run ci:gate` проходит в штатной онлайн-среде.
3. `scripts/secret_scan.ps1` ловит реальные unquoted секреты в `.env`-формате.
4. `provider profile` действительно переключает runtime transport, а не только env-переменные и model-id.
5. для `OpenAI-Compatible` есть отдельный transport path, а не Ollama-совместимая имитация.
6. есть тесты, которые удерживают все четыре исправления от регресса.

## Общая стратегия поставки

Рекомендуемая разбивка на изменения:

1. `PR-1`: стабилизация feedback loop
2. `PR-2`: исправление `Helper.sln`
3. `PR-3`: исправление `secret_scan.ps1`
4. `PR-4`: рефакторинг provider runtime
5. `PR-5`: hardening `nuget_security_gate.ps1`
6. `PR-6`: финальная сверка CI, docs, release baseline

Если репозиторий активно меняется, не стоит смешивать эти блоки в один коммит.

## Фаза 0. Базовая подготовка

### Шаг 0.1. Зафиксировать baseline

Запустить и сохранить текущие результаты:

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

Результат:

- положить текстовые логи в `temp/analysis/remediation-baseline/`
- не коммитить туда machine-local секреты

### Шаг 0.2. Зафиксировать рабочие критерии для каждого дефекта

Создать короткий internal checklist:

1. Что считается воспроизведением дефекта.
2. Что считается устранением дефекта.
3. Какой automated check подтверждает устранение.

### Шаг 0.3. Ввести временный change-freeze на смежные зоны

До окончания remediation не смешивать параллельно:

1. новые provider profile features;
2. новые env keys;
3. новые solution-проекты;
4. изменения в security scan scripts без тестов.

## Фаза 1. Быстрый возврат доверия к feedback loop

### Шаг 1.1. Перегенерировать config-governance артефакты

Запустить:

1. `powershell -ExecutionPolicy Bypass -File scripts/generate_env_reference.ps1`

Проверить:

1. `doc/config/ENV_REFERENCE.md`
2. `doc/config/ENV_INVENTORY.json`
3. `.env.local.example`

Цель:

- вернуть синхронность generated artifacts и source of truth из `src/Helper.Api/Backend/Configuration/BackendEnvironmentInventory.cs`

### Шаг 1.2. Проверить смысловой diff, а не только timestamp

Сравнить regenerated files с предыдущими версиями и отдельно убедиться, что появились:

1. `HELPER_MODEL_LONG_CONTEXT`
2. `HELPER_MODEL_DEEP_REASONING`
3. `HELPER_MODEL_VERIFIER`
4. `HELPER_WEB_SEARCH_LOCAL_URL`
5. `HELPER_WEB_SEARCH_SEARX_URL`

### Шаг 1.3. Вернуть зелёный `config governance`

Запустить:

1. `powershell -ExecutionPolicy Bypass -File scripts/check_env_governance.ps1`
2. `npm run ci:gate`

Ожидаемый промежуточный результат:

- `ci:gate` перестаёт падать на шаге `Config governance`

### Шаг 1.4. Зафиксировать generated-artifacts policy

Добавить правило в workflow команды:

1. любые изменения `BackendEnvironmentInventory.cs` обязаны сопровождаться regeneration;
2. PR без обновлённых generated artifacts не принимается.

Если нужен технический enforcement:

1. добавить pre-commit hook script;
2. либо lightweight CI step, который запускает generation в `temp/` и сравнивает с tracked files.

## Фаза 2. Исправление `Helper.sln`

### Шаг 2.1. Подтвердить источник падения

Перепроверить solution-конфигурацию и конкретно пять проектов:

1. `Helper.Runtime.Api.Tests`
2. `Helper.Runtime.Integration.Tests`
3. `Helper.Runtime.Browser.Tests`
4. `Helper.Runtime.Certification.Tests`
5. `Helper.Runtime.Certification.Compile.Tests`

Проверить в `Helper.sln`, какие `ProjectConfigurationPlatforms` отсутствуют.

### Шаг 2.2. Выбрать стратегию

Нужно принять одно решение:

1. либо эти проекты должны реально участвовать в solution build;
2. либо они intentionally excluded и тогда их надо либо убрать из canonical solution, либо явно документировать отдельную solution-модель.

Рекомендуемое решение:

1. если проекты нужны для реального engineering workflow, добавить отсутствующие `Build.0`;
2. если это operator-only или expensive suites, вынести их в отдельную `.slnf` или отдельную solution.

### Шаг 2.3. Исправить `ProjectConfigurationPlatforms`

После выбранной стратегии:

1. обновить `Helper.sln`;
2. выровнять `Debug|Any CPU`, `Debug|x64`, `Release|Any CPU`, `Release|x64`;
3. убрать неконсистентные `ActiveCfg` без `Build.0`, если проект должен строиться.

### Шаг 2.4. Добавить минимальный smoke-check

Добавить в CI ранний шаг:

1. `dotnet build Helper.sln -v minimal`

Этот шаг должен идти до тяжёлых parity/eval стадий.

### Шаг 2.5. Подтвердить исправление

Обязательные проверки:

1. `dotnet build Helper.sln`
2. `dotnet build src/Helper.Api/Helper.Api.csproj`
3. `dotnet build src/Helper.Runtime/Helper.Runtime.csproj`

### Шаг 2.6. Защитить от повторного дрейфа

Добавить одно из решений:

1. script-проверку на invalid `.sln` project configuration;
2. либо policy: все новые test projects регистрируются в solution только через шаблон с полной matrix-конфигурацией.

## Фаза 3. Исправление `secret_scan.ps1`

### Шаг 3.1. Сформировать явный набор тестовых кейсов

Создать временные test fixtures в `temp/analysis/secret-scan-fixtures/`:

1. `.env.local` с real-looking `HELPER_API_KEY` без кавычек
2. `.env.local` с `HELPER_SESSION_SIGNING_KEY` без кавычек
3. `.env.local` с placeholder `HELPER_API_KEY=<set-me>`
4. `.env.local` с commented secret line
5. `.env.local` с quoted secret
6. `.env.local` с пустым значением

### Шаг 3.2. Обновить паттерны

В `scripts/secret_scan.ps1`:

1. оставить текущие паттерны для quoted значений;
2. добавить паттерны для обычного `.env` формата;
3. добавить отдельный паттерн для `HELPER_SESSION_SIGNING_KEY`;
4. не сканировать по слишком широкому regex, чтобы не утонуть в false positives.

Рекомендуемые формы:

1. `(?m)^HELPER_API_KEY=([^#\r\n]+)$`
2. `(?m)^HELPER_SESSION_SIGNING_KEY=([^#\r\n]+)$`

### Шаг 3.3. Оставить allowlist только для допустимых placeholder-значений

Проверить, чтобы allowlist пропускал:

1. `<set-me>`
2. `<redacted>`
3. `changeme`
4. `change_me`
5. `replace_me`
6. `${ENV_VAR}`
7. пустые и comment-строки

Но не пропускал:

1. произвольные длинные alphanumeric secrets;
2. base64-like session secrets;
3. hex-like API keys.

### Шаг 3.4. Добавить регрессионные тесты для scanner

Лучший вариант:

1. сделать отдельный deterministic PowerShell test harness для `secret_scan.ps1`

Допустимый вариант:

1. добавить lightweight .NET/PowerShell integration test, который создаёт временный fixture tree и вызывает scanner как subprocess.

### Шаг 3.5. Прогнать scanner по реальному workspace

Проверить:

1. `powershell -ExecutionPolicy Bypass -File scripts/secret_scan.ps1`

Ожидаемо:

1. scanner должен сработать на локальном `.env.local`, если там реально лежат non-placeholder секреты;
2. scanner не должен ложно падать на `.env.local.example`.

### Шаг 3.6. Принять policy-решение по `.env.local`

Нужно явно решить и задокументировать:

1. scanner должен падать на локальные секреты в workspace;
2. либо scanner должен игнорировать `.env.local`, а отдельный gate должен сканировать только tracked files.

Рекомендуемое решение:

1. разделить режимы:
   - `repo mode`: только tracked first-party surface;
   - `workspace hygiene mode`: tracked files + `.env.local`

Без этого проект будет продолжать смешивать разные цели одним скриптом.

## Фаза 4. Рефакторинг provider runtime

Это самый крупный блок. Его нельзя делать “точечной правкой” в `ApplyToRuntime()`.

### Шаг 4.1. Зафиксировать целевую архитектуру

Нужно сначала принять письменное решение:

1. какие transport kinds поддерживаются;
2. где хранится active provider state;
3. кто создаёт runtime client;
4. как происходит hot-switch;
5. что является source of truth: env, store, DI scope или in-memory runtime registry.

Рекомендуемая цель:

1. `ProviderProfileStore` хранит активный profile id;
2. runtime получает `ProviderRuntimeConfiguration` через resolver/factory;
3. transport client создаётся factory-слоем, а не через `Environment.SetEnvironmentVariable(...)`;
4. `AILink` больше не является универсальным singleton для всех transport-типов.

### Шаг 4.2. Разделить transport contracts

Ввести явные интерфейсы:

1. `IModelTransportClient`
2. `IModelTransportFactory`
3. `IProviderRuntimeContext` или `IActiveProviderAccessor`

Функции transport client:

1. `DiscoverModelsAsync`
2. `AskAsync`
3. `StreamAsync`
4. `WarmAsync`
5. optional `GetCapabilities`

### Шаг 4.3. Выделить Ollama transport отдельно

Текущий `AILink` фактически уже Ollama-specific. Его нужно:

1. либо переименовать в `OllamaTransportClient`;
2. либо оставить `AILink`, но ограничить его ролью только для `ProviderTransportKind.Ollama`.

Обязательно:

1. убрать implicit претензию, что этот клиент универсален.

### Шаг 4.4. Реализовать `OpenAI-Compatible` transport отдельно

Создать новый клиент с отдельной протокольной реализацией:

1. `/v1/models` или эквивалентный discovery path;
2. `chat/completions` или используемый проектом OpenAI-compatible path;
3. header-based auth;
4. отдельная обработка stream format;
5. нормальная передача `baseUrl`, `apiKey`, `model`.

### Шаг 4.5. Убрать process-wide mutation из `ProviderProfileResolver.ApplyToRuntime()`

Перестать использовать как runtime-API:

1. `Environment.SetEnvironmentVariable("HELPER_OPENAI_BASE_URL", ...)`
2. `Environment.SetEnvironmentVariable("HELPER_OPENAI_API_KEY", ...)`
3. `Environment.SetEnvironmentVariable("HELPER_AI_BASE_URL", ...)`
4. другие process-wide mutations, влияющие на transport routing

Разрешённый компромисс:

1. можно оставить `HELPER_ACTIVE_PROVIDER_PROFILE_ID` только как telemetry/debug marker, если он не участвует в логике runtime routing.

### Шаг 4.6. Переписать `ProviderProfileActivationService`

После активации профиля сервис должен:

1. сохранить active profile id;
2. инвалидировать текущий transport client;
3. пересоздать client/configuration через factory;
4. выполнить bounded `DiscoverAsync`;
5. вернуть честный результат активации;
6. не полагаться на глобальные env side effects.

### Шаг 4.7. Обновить DI lifetime model

Нужно пересмотреть lifetime:

1. singleton `AILink` в текущем виде убрать;
2. вместо него использовать singleton factory + thread-safe runtime accessor;
3. actual transport client может быть singleton-per-active-profile или lazily refreshed singleton behind accessor.

Критерий:

1. смена active profile должна влиять на последующие запросы без перезапуска процесса;
2. параллельные запросы не должны получать partially switched state.

### Шаг 4.8. Вычистить env-зависимости из model routing

Перепроверить:

1. `ProviderProfileCatalog`
2. `ProviderProfileResolver`
3. `HelperModelGateway`
4. `ConversationModelSelectionPolicy`
5. `ReasoningEffortPolicy`

Нужно отделить:

1. bootstrap env defaults;
2. runtime active profile selection;
3. request-time model resolution.

### Шаг 4.9. Добавить тесты на provider runtime

Минимально обязательные тесты:

1. активация `Ollama` меняет base URL и используется в следующем запросе;
2. активация `OpenAI-Compatible` меняет transport type и следующий запрос уходит через OpenAI-compatible client;
3. смена профиля не меняет процесс-wide env как functional dependency;
4. `DiscoverAsync` вызывается после активации;
5. параллельные запросы во время переключения не получают mixed config state.

### Шаг 4.10. Добавить тест на опубликованный API contract

Проверить end-to-end:

1. `POST /api/settings/provider-profiles/activate`
2. затем runtime request
3. затем подтверждение, что фактический transport соответствует активному профилю

### Шаг 4.11. Провести ручную валидацию

Сценарии:

1. запуск с локальным Ollama profile
2. переключение на OpenAI-compatible profile
3. возврат обратно на local profile
4. проверка streaming path
5. проверка discovery/warmup path

### Шаг 4.12. Обновить docs

После завершения refactor обновить:

1. docs по provider profile
2. docs по supported transports
3. operator docs
4. любые security/trust docs, где ранее implied runtime switching через env

## Фаза 5. Hardening `nuget_security_gate.ps1`

Это не основной product bug, но важный operational дефект.

### Шаг 5.1. Явно разделить типы провалов

Сценарии должны различаться:

1. `vulnerability data retrieved and vulnerable packages found`
2. `vulnerability data unavailable`
3. `restore/list command failed for unrelated reasons`

Сейчас `NU1900` смешивается с security-result.

### Шаг 5.2. Ввести режимы исполнения

Предлагаемые режимы:

1. `strict-online`: любой `NU1900` = fail
2. `best-effort-local`: `NU1900` = degraded warning + distinct exit code/message

### Шаг 5.3. Улучшить диагностику среды

Перед аудитом выводить:

1. используемые NuGet sources
2. наличие proxy env vars
3. online/offline intent

Без вывода секретов.

### Шаг 5.4. Сделать отдельный итоговый статус

Скрипт должен явно писать:

1. `audit_passed`
2. `audit_failed_vulnerabilities_found`
3. `audit_failed_infrastructure_unavailable`

### Шаг 5.5. Обновить CI policy

Для CI:

1. online runners должны использовать strict-online mode
2. local operator runs могут использовать best-effort mode

### Шаг 5.6. Добавить regression checks

Минимум:

1. fixture на simulated `NU1900`
2. fixture на vulnerable package JSON
3. fixture на empty-safe report

## Фаза 6. Финальная стабилизация и сверка

### Шаг 6.1. Прогнать полную верификацию

Обязательный финальный набор:

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

### Шаг 6.2. Прогнать targeted manual smoke

Проверить руками:

1. settings UI для provider profiles
2. активацию профиля
3. chat request после активации
4. streaming response после активации
5. runtime doctor

### Шаг 6.3. Обновить аналитические документы

После исправлений:

1. обновить критический анализ, если часть findings закрыта;
2. добавить closure report;
3. зафиксировать, какие дефекты закрыты, а какие перенесены в backlog.

### Шаг 6.4. Закрыть remediation только при полном доказательстве

Закрывать работы можно только если:

1. solution build зелёный;
2. provider switching подтверждён тестами и ручной валидацией;
3. config docs синхронны;
4. secret scan ловит реальные workspace secrets в выбранном режиме;
5. NuGet gate различает infra failure и vulnerability result.

## Карта изменений по файлам

### Блок `config governance`

Основные файлы:

1. `scripts/generate_env_reference.ps1`
2. `scripts/check_env_governance.ps1`
3. `doc/config/ENV_REFERENCE.md`
4. `doc/config/ENV_INVENTORY.json`
5. `.env.local.example`
6. `src/Helper.Api/Backend/Configuration/BackendEnvironmentInventory.cs`

### Блок `solution repair`

Основные файлы:

1. `Helper.sln`
2. возможно `.slnf` или отдельная solution, если будет принято такое решение
3. CI scripts/workflows, где canonical build entrypoint должен быть проверен

### Блок `secret scan`

Основные файлы:

1. `scripts/secret_scan.ps1`
2. новый script test harness или соответствующий test-project

### Блок `provider runtime`

Основные файлы:

1. `src/Helper.Api/Backend/Providers/ProviderProfileResolver.cs`
2. `src/Helper.Api/Backend/Providers/ProviderProfileActivationService.cs`
3. `src/Helper.Api/Backend/Providers/ProviderProfileCatalog.cs`
4. `src/Helper.Api/Backend/ModelGateway/HelperModelGateway.cs`
5. `src/Helper.Api/Hosting/ServiceRegistrationExtensions.Core.cs`
6. `src/Helper.Runtime/AILink.cs`
7. `src/Helper.Runtime/AILink.Chat.cs`
8. новые transport/factory classes
9. provider-related tests

### Блок `NuGet gate`

Основные файлы:

1. `scripts/nuget_security_gate.ps1`
2. возможно `scripts/ci_gate.ps1`
3. CI workflow definitions

## Риски выполнения

### Риск 1. Сломать текущий работающий local-only runtime

Снижение риска:

1. сначала покрыть существующее поведение тестами;
2. вводить новый transport layer параллельно старому;
3. переключить wiring только после прохождения тестов.

### Риск 2. Вернуть `ci:gate` в зелёный цвет, но оставить ложную безопасность

Снижение риска:

1. не ограничиваться regeneration docs;
2. отдельно тестировать scanner и provider switching.

### Риск 3. Исправить `.sln`, но сломать intended separation тяжёлых test suites

Снижение риска:

1. сначала принять решение, какие test projects должны участвовать в canonical build;
2. при необходимости вынести тяжёлые наборы в отдельные solutions, а не лечить всё одним шаблоном.

### Риск 4. Слишком большой refactor provider runtime

Снижение риска:

1. делать в два прохода:
   - сначала transport abstraction
   - потом OpenAI-compatible implementation

## Рекомендуемый порядок по дням

### День 1

1. baseline logs
2. regeneration config artifacts
3. возврат зелёного `check_env_governance`
4. исправление `Helper.sln`

### День 2

1. исправление `secret_scan.ps1`
2. добавление тестов на scanner
3. повторная проверка `ci:gate`

### День 3-4

1. проектирование provider runtime architecture
2. выделение transport abstraction
3. перенос Ollama path
4. внедрение OpenAI-compatible client

### День 5

1. integration tests provider activation
2. manual smoke
3. docs update

### День 6

1. hardening `nuget_security_gate.ps1`
2. финальный прогон всех команд
3. closure report

## Финальный критерий готовности

План считается исполненным, когда репозиторий можно честно описать так:

1. canonical solution действительно собирается;
2. CI gates отражают реальное состояние, а не генерируют ложный `PASS` или непрозрачный `FAIL`;
3. provider profile activation действительно меняет runtime behavior;
4. документация по env синхронна с исходным кодом;
5. security/hygiene слой снова заслуживает доверия.
