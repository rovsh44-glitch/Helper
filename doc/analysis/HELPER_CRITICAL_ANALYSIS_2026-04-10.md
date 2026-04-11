# HELPER: глубокий критический анализ

Дата: `2026-04-10`

## Область анализа

- Локальный workspace: `C:\WORKSPACE\HELPER`
- Remote: `https://github.com/rovsh44-glitch/Helper.git`
- После `git fetch origin`: `HEAD == origin/main == 0539e7e8573ab9213325f82f7eec4f6b3c71354d`
- Следовательно, выводы ниже относятся и к локальному проекту, и к текущему состоянию GitHub-репозитория на этом коммите.

## Короткий вывод

Проект не выглядит разваленным на уровне основного кода: фронтенд-сборка проходит, ключевые .NET-проекты собираются, три крупных тестовых набора зелёные. Но репозиторий находится в плохом operational-state:

1. заявленное runtime-переключение provider profile архитектурно не доведено и в текущем виде не может надёжно переключать транспорт;
2. `Helper.sln` сломан как orchestration-layer: `dotnet build Helper.sln` падает без compile errors;
3. верхнеуровневый `ci:gate` сейчас красный из-за дрейфа config-governance артефактов;
4. `secret_scan.ps1` даёт ложный `PASS` на реальном `.env.local` с секретами, если значения записаны обычным `.env`-форматом без кавычек.

Итог: основной код в целом живой, но доверять текущим заявлениям репозитория о self-governance и operator readiness без правок нельзя.

## Подтверждённые findings

### 1. `High`: runtime-переключение provider profile в текущей архитектуре фактически недоведено

#### Почему это критично

В коде есть UI/API-поверхность для активации provider profile, включая `OpenAI-Compatible`, но runtime-клиент моделей (`AILink`) не перестраивает транспорт и вообще разговаривает только по Ollama-style endpoint-ам.

#### Доказательства

- `AILink` регистрируется как singleton: `src/Helper.Api/Hosting/ServiceRegistrationExtensions.Core.cs:15`.
- Singleton создаётся через `services.AddSingleton<AILink>()`, то есть DI не подаёт ему runtime-сконфигурированные `baseUrl` и `defaultModel`.
- Конструктор `AILink` жёстко захардкожен на `http://localhost:11434` и `qwen2.5-coder:14b`: `src/Helper.Runtime/AILink.cs:25-39`.
- `AILink` discovery и inference используют только Ollama-style API:
  - `GET /api/tags`: `src/Helper.Runtime/AILink.cs:42-59`
  - `POST /api/generate`: `src/Helper.Runtime/AILink.Chat.cs:52-79`
  - stream также идёт в `POST /api/generate`: `src/Helper.Runtime/AILink.Chat.cs:107-156`
- Provider activation endpoint реально опубликован: `src/Helper.Api/Hosting/EndpointRegistrationExtensions.Settings.ProviderProfiles.cs:26-29`.
- Но `ProviderProfileResolver.ApplyToRuntime()` только:
  - пишет process-wide env vars;
  - вызывает `_aiLink.SwitchModel(...)`;
  - не пересоздаёт `AILink`;
  - не меняет `HttpClient.BaseAddress`;
  - не переключает protocol-shape c Ollama на OpenAI-compatible:
    `src/Helper.Api/Backend/Providers/ProviderProfileResolver.cs:64-90`.

#### Практический эффект

- Активация профиля создаёт иллюзию переключения, но сетевой транспорт остаётся тем, с чем singleton `AILink` был поднят.
- `OpenAI-Compatible` профиль выглядит заявленным, но код inference-layer не умеет работать через OpenAI-compatible surface.
- Любая попытка runtime-switch между профилями даёт глобальные побочные эффекты через `Environment.SetEnvironmentVariable(...)`, что опасно даже в single-process сценарии и особенно плохо при нескольких сессиях/запросах.

#### Что нужно сделать

1. Вынести transport abstraction отдельно от `AILink`.
2. Перестать использовать process-wide env mutation как механизм runtime-switch.
3. Создавать provider-bound gateway/client через factory на основе active profile.
4. Добавить integration tests именно на `ProviderProfileActivationService` и реальное переключение transport/base URL.

### 2. `High`: `Helper.sln` сломан на уровне конфигурации решения

#### Симптом

`dotnet build Helper.sln` воспроизводимо падает:

- `Build FAILED.`
- `0 Warning(s)`
- `0 Error(s)`

То есть solution-level orchestration broken, хотя compile-layer проектов в целом жив.

#### Подтверждение

- `dotnet build Helper.sln` падает.
- При этом точечные сборки проходят:
  - `dotnet build src/Helper.Api/Helper.Api.csproj`
  - `dotnet build src/Helper.Runtime/Helper.Runtime.csproj`
  - `dotnet build test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj`
- В `.sln` у пяти тестовых проектов есть только `ActiveCfg`, но отсутствуют `Build.0` entries:
  - `Helper.sln:170-199`
- Для соседних проектов `Build.0` entries присутствуют, например:
  - `Helper.sln:161-169`

Фактически problem surface выглядит так:

- `Helper.Runtime.Api.Tests`
- `Helper.Runtime.Integration.Tests`
- `Helper.Runtime.Browser.Tests`
- `Helper.Runtime.Certification.Tests`
- `Helper.Runtime.Certification.Compile.Tests`

#### Практический эффект

- Команда верхнего уровня, которую ожидает любой оператор/CI (`dotnet build Helper.sln`), невалидна.
- Ошибка непрозрачна: build падает без compile diagnostics, что повышает стоимость поддержки.
- Любой процесс, опирающийся на solution build как на canonical health check, получает ложный negative signal.

#### Что нужно сделать

1. Починить `ProjectConfigurationPlatforms` для этих пяти test projects.
2. Либо добавить отсутствующие `Build.0`, либо явно убрать проекты из solution-конфигурации и перестать рекламировать `Helper.sln` как canonical build entrypoint.
3. Добавить отдельный smoke-check на `dotnet build Helper.sln` в CI до тяжёлых этапов.

### 3. `Medium-High`: config-governance артефакты устарели, из-за чего штатный `ci:gate` уже красный

#### Подтверждение

`npm run ci:gate` сейчас падает на шаге `Config governance`:

- `doc/config/ENV_INVENTORY.json is stale`
- `doc/config/ENV_REFERENCE.md is stale`

`check_env_governance.ps1` подтверждает тот же результат.

#### Фактический дрейф

Source of truth уже содержит переменные, которых нет в текущем `doc/config/ENV_REFERENCE.md`:

- `HELPER_MODEL_LONG_CONTEXT`
- `HELPER_MODEL_DEEP_REASONING`
- `HELPER_MODEL_VERIFIER`
- `HELPER_WEB_SEARCH_LOCAL_URL`
- `HELPER_WEB_SEARCH_SEARX_URL`

См. `src/Helper.Api/Backend/Configuration/BackendEnvironmentInventory.cs:387-389` и `:420-421`.

При этом текущий `doc/config/ENV_REFERENCE.md` в model-gateway секции заканчивается на:

- `HELPER_MODEL_FAST`
- `HELPER_MODEL_REASONING`
- `HELPER_MODEL_CRITIC`
- `HELPER_MODEL_SAFE_FALLBACK`

См. `doc/config/ENV_REFERENCE.md:61-66`.

#### Практический эффект

- Репозиторий сам нарушает собственный governance contract.
- Документация по env больше не является надёжной.
- Верхнеуровневый CI уже сломан на реальном HEAD.

#### Что нужно сделать

1. Перегенерировать config artifacts через `scripts/generate_env_reference.ps1`.
2. Добавить pre-commit или CI autofail именно на diff generated artifacts.
3. Рассматривать `BackendEnvironmentInventory.cs` как единственный source of truth и не редактировать derived docs вручную.

### 4. `Medium-High`: `secret_scan.ps1` пропускает реальные секреты в обычном `.env`-формате

#### Подтверждение

- В локальном `.env.local` есть реальные секретные ключи на строках:
  - `.env.local:2`
  - `.env.local:3`
- Значения я не воспроизвожу в отчёте; при анализе они были подтверждены в редактированном/redacted виде.
- При этом `scripts/secret_scan.ps1` возвращает:
  - `[SecretScan] No known secret patterns found in the first-party source surface.`

#### Корень проблемы

Regex для `HELPER_API_KEY` ищет только значение в кавычках:

- `scripts/secret_scan.ps1:10`
- паттерн: `HELPER_API_KEY\s*=\s*[''"][^''"]+[''"]`

Но стандартный `.env` формат в проекте использует запись без кавычек, например:

- `.env.local:2`
- `.env.local:3`

Дополнительно allowlist для `.env*` пропускает только пустые строки, комментарии и placeholder-значения:

- `scripts/secret_scan.ps1:17-20`
- `scripts/secret_scan.ps1:53-65`

То есть scanner не исключает `.env.local`, но и не ловит типичный unquoted secret.

#### Практический эффект

- Репозиторий получает ложный `PASS` именно там, где рассчитывает на secret hygiene.
- Это не текущая утечка в git history, потому что `.env.local` не tracked, но это серьёзный blind spot в защитном контуре.
- Пользователь/оператор может ошибочно считать workspace чистым.

#### Что нужно сделать

1. Добавить паттерны для unquoted `.env` значений:
   - `^HELPER_API_KEY=[^#\r\n]+$`
   - `^HELPER_SESSION_SIGNING_KEY=[^#\r\n]+$`
2. Отдельно разрешать только placeholder-значения.
3. Добавить unit/integration test на scanner с реальным `.env`-форматом без кавычек.

## Дополнительные наблюдения

### Позитивные сигналы

- `npm run build` проходит.
- `powershell -ExecutionPolicy Bypass -File scripts/check_docs_entrypoints.ps1` проходит.
- `powershell -ExecutionPolicy Bypass -File scripts/check_ui_api_usage.ps1` проходит.
- `powershell -ExecutionPolicy Bypass -File scripts/check_frontend_architecture.ps1 -SkipApiBoundary` проходит.
- `powershell -ExecutionPolicy Bypass -File scripts/check_root_layout.ps1` проходит.
- Тесты:
  - `Helper.Runtime.Tests`: `189/189`
  - `Helper.RuntimeSlice.Api.Tests`: `4/4`
  - `Helper.Runtime.Api.Tests`: `168/168`

### Environment-induced, но важный operational risk

`powershell -ExecutionPolicy Bypass -File scripts/nuget_security_gate.ps1` в этой среде падает на `NU1900`, потому что vulnerability audit data не могут быть получены. Дополнительно в окружении были выставлены proxy vars на `127.0.0.1:9`.

Это не даёт оснований автоматически считать репозиторий уязвимым, но показывает, что текущий security gate очень хрупок к офлайн/сломленному proxy окружению и не различает "уязвимости найдены" и "данные аудита недоступны".

## Приоритет исправлений

1. Сначала исправить provider runtime architecture.
2. Затем починить `Helper.sln`, чтобы вернуть валидный canonical build entrypoint.
3. Затем перегенерировать env governance docs и вернуть `ci:gate` в зелёное состояние.
4. После этого закрыть blind spot в `secret_scan.ps1` и добавить тесты на scanner и provider activation.

## Итоговая оценка

Проект в текущем состоянии нельзя назвать "разбитым", но и нельзя считать репозиторий операционно надёжным. Основной functional core выглядит существенно здоровее, чем governance/tooling layer. Самый опасный дефект не в UI и не в unit-тестах, а в ложной уверенности, что provider profile activation действительно меняет runtime transport. В текущем коде это утверждение не подтверждается.
