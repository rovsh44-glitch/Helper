# HELPER GitHub Deep Audit Remediation Plan 2026-04-11

Исходный аудит:

- [`HELPER_GITHUB_DEEP_AUDIT_2026-04-11.md`](doc/analysis/HELPER_GITHUB_DEEP_AUDIT_2026-04-11.md)

## Цель

Довести репозиторий `rovsh44-glitch/Helper` до состояния, при котором:

1. вкладка `Actions` даёт диагностически полезный и честный сигнал
2. branch-facing `repo-gate` и certification/heavy контуры не путаются между собой
3. GitHub-side governance и security surfaces либо реально включены, либо их отсутствие компенсировано и явно задокументировано
4. operational history перестаёт жить только в PR и markdown-файлах

## Что уже хорошо и не надо ломать

На `main` уже подтверждено:

1. `repo-gate` на push в `main` проходит
2. PR `#30` смержен
3. portability blocker с `rg` закрыт
4. Node 24 opt-in включён в workflow

Это означает, что remediation надо строить не как новый большой рефакторинг CI, а как точечное закрытие оставшихся операционных дыр.

## Ключевая причинно-следственная цепочка

1. `repo-gate` сейчас зелёный, но не покрывает heavy/certification claims.
2. `runtime-test-lanes` schedule системно красный, но текущий wrapper для `certification_compile` скрывает реальную причину падения.
3. Пока не исправлена observability `certification_compile`, невозможно честно устранить сам defect, потому что GitHub Actions не показывает root cause.
4. Даже после исправления `certification_compile` heavy parity-контур останется честно красным, если `ci_gate_heavy.ps1` продолжит оценивать parity gate без свежего parity batch и без реального окна для `window gate`.
5. Даже при зелёных workflow server-side enforcement остаётся неполным, пока у private-repo на текущем плане недоступны branch protection и rulesets.
6. Даже при хорошем custom CI вкладка `Security` остаётся почти пустой, пока GitHub-native security features выключены.

Следствие: правильный порядок работ такой:

1. восстановить диагностируемость `Actions`
2. устранить сам `certification_compile` defect
3. отделить deterministic branch-health от certification-heavy claims
4. закрыть GitHub governance/security gaps
5. оформить release/governance discipline

## Приоритеты

### P0

1. `certification_compile` observability
2. реальный root cause `certification_compile`

### P1

1. честный heavy parity contract
2. GitHub governance enforcement gap
3. GitHub security surfaces

### P2

1. issues/releases/tags discipline
2. documentation cleanup and operator guidance

## Wave 0. Зафиксировать исходную точку

Цель: перед исправлениями сохранить evidence текущего состояния, чтобы потом не потерять причинно-следственную историю.

### Шаги

1. Сохранить run ids и status snapshot:
   - `24285458028` `repo-gate success`
   - `24274532576` `runtime-test-lanes schedule failure`
   - `24226861144` `runtime-test-lanes schedule failure`
   - `24172530478` `runtime-test-lanes schedule failure`
2. Сохранить ссылки на failing logs `certification_compile`.
3. Сохранить факт platform limits:
   - `branches/main/protection` -> `403`
   - `rulesets` -> `403`
   - `code-scanning` -> disabled
   - `dependabot alerts` -> disabled
   - `secret scanning` -> disabled

### Артефакты

1. Этот план
2. [`HELPER_GITHUB_DEEP_AUDIT_2026-04-11.md`](doc/analysis/HELPER_GITHUB_DEEP_AUDIT_2026-04-11.md)

### Definition of Done

Все исходные audit-facts остаются восстановимыми после remediation.

## Wave 1. Починить observability `certification_compile`

### Проблема

[`scripts/run_certification_compile_tests.ps1`](scripts/run_certification_compile_tests.ps1) запускает дочерний процесс так, что `Actions` показывает только `exit code 1`, но не реальную причину compile/test failure.

Критические строки:

1. [`run_certification_compile_tests.ps1`](scripts/run_certification_compile_tests.ps1):373
2. [`run_certification_compile_tests.ps1`](scripts/run_certification_compile_tests.ps1):374
3. [`run_certification_compile_tests.ps1`](scripts/run_certification_compile_tests.ps1):375
4. [`run_certification_compile_tests.ps1`](scripts/run_certification_compile_tests.ps1):397

### Что сделать

1. Переделать wrapper так, чтобы stdout/stderr дочернего `dotnet test` сохранялись в явные файлы внутри run-root.
2. На failure печатать в родительский workflow log:
   - command line
   - exit code
   - последние строки stdout
   - последние строки stderr
   - путь к полным логам
3. Не удалять diagnostic context при failure.
4. Cleanup run-root делать только на success или только если внутри действительно нет полезных артефактов.
5. Если уже есть reusable helper для monitored `dotnet test`, рассмотреть переиспользование вместо второго собственного process-wrapper.

### Файлы

1. [`scripts/run_certification_compile_tests.ps1`](scripts/run_certification_compile_tests.ps1)
2. возможно [`scripts/run_dotnet_test_with_monitor.ps1`](scripts/run_dotnet_test_with_monitor.ps1)
3. при необходимости [`doc/operator/HELPER_RUNTIME_TEST_LANES_2026-04-01.md`](doc/operator/HELPER_RUNTIME_TEST_LANES_2026-04-01.md)

### Верификация

1. Локально воспроизвести искусственный failure и убедиться, что parent script печатает root cause.
2. Запустить hosted `workflow_dispatch` для `runtime-test-lanes` с `run_certification_compile=true`.
3. Проверить, что при failure GitHub log больше не заканчивается на голом `Process completed with exit code 1`.

### Definition of Done

Любой следующий failure `certification_compile` диагностируется из вкладки `Actions` без доступа к локальной машине.

## Wave 2. Устранить реальный defect `certification_compile`

### Проблема

Сейчас известно, что compile lane падает стабильно, но сама причина скрыта wrapper-логикой. Поэтому Wave 2 начинается только после Wave 1.

### Что сделать

1. После observability fix запустить `runtime-test-lanes` вручную только для `certification_compile`.
2. Зафиксировать конкретный failing command/test/project.
3. Классифицировать defect:
   - compile error
   - runtime dependency drift
   - flaky test
   - path/output isolation defect
   - hosted-runner specific issue
4. Исправить defect в owning scope, а не маскировать его в workflow.
5. Повторно прогнать:
   - manual `certification_compile`
   - full `runtime-test-lanes` where relevant

### Файлы

TBD после получения root cause. До observability fix это гадание.

### Верификация

1. `workflow_dispatch` с `run_certification_compile=true` -> `success`
2. Следующий scheduled-run `runtime-test-lanes` не падает на `certification_compile`

### Definition of Done

`certification_compile` больше не системно красный ни вручную, ни по schedule.

## Wave 3. Привести heavy parity contract в честное состояние

### Проблема

[`scripts/ci_gate_heavy.ps1`](scripts/ci_gate_heavy.ps1) сейчас оценивает parity KPI без генерации свежего parity workload evidence и одновременно требует `window gate`, который по определению нельзя честно закрыть same-day прогоном.

### Что сделать

#### 3A. Починить same-day heavy path

1. Вставить `run_parity_golden_batch.ps1 -Runs 24 -FailOnThresholds` перед `run_generation_parity_gate.ps1`.
2. Убедиться, что generated evidence попадает в корректные repo-owned места, а не в ad hoc paths.
3. Проверить, что после same-day batch обычный parity gate считает свежие данные, а не stale history.

#### 3B. Разделить same-day heavy и multi-day certification

Рекомендованная модель:

1. `ci_gate_heavy.ps1`
   - load/chaos
   - parity golden batch
   - parity gate
   - остальные same-day heavy checks
2. `window gate`
   - вынести в отдельный scheduled/manual certification workflow
   - не требовать его в ad hoc same-day heavy closure

Если оставить `window gate` внутри same-day heavy, то heavy path будет оставаться честно красным по design, а не по defect.

### Файлы

1. [`scripts/ci_gate_heavy.ps1`](scripts/ci_gate_heavy.ps1)
2. [`scripts/run_parity_golden_batch.ps1`](scripts/run_parity_golden_batch.ps1)
3. возможно новый workflow:
   - `.github/workflows/runtime-certification-nightly.yml`
   - или расширение [`runtime-test-lanes.yml`](.github/workflows/runtime-test-lanes.yml)
4. operator docs:
   - [`doc/operator/HELPER_RUNTIME_TEST_LANES_2026-04-01.md`](doc/operator/HELPER_RUNTIME_TEST_LANES_2026-04-01.md)
   - [`README.md`](README.md)

### Верификация

1. `powershell -ExecutionPolicy Bypass -File scripts/ci_gate_heavy.ps1`
2. отдельный scheduled/manual прогон `window gate`
3. проверить, что same-day heavy больше не падает только потому, что нет 7-дневного окна

### Definition of Done

1. ad hoc heavy gate и multi-day certification gate имеют разные честные контракты
2. `repo-gate` остаётся deterministic
3. parity failures означают реальные KPI проблемы, а не broken orchestration

## Wave 4. Закрыть GitHub governance gap

### Проблема

На текущем private-repo и текущем плане GitHub:

1. `branch protection` недоступен
2. `rulesets` недоступны

Это platform-limit, а не bug в коде.

### Что сделать

#### Вариант A. Минимально реалистичный на текущем плане

1. Явно задокументировать, что enforcement process-based, а не server-enforced.
2. Закрепить `PR-only main discipline` как обязательную operational policy.
3. Держать `repo-gate` как required human process before merge.
4. Ограничить прямые pushes организационно.

#### Вариант B. Предпочтительный

1. Перевести репозиторий на GitHub plan, где доступны protected branches и rulesets для private repos.
2. После этого включить:
   - required status checks
   - disallow direct pushes
   - required PR reviews if нужны
   - repository rulesets

#### Вариант C. Public-visibility path

Если когда-либо возвращаться к обсуждению `Public`, это открывает часть features, но только при условии, что репозиторий реально public-ready и legal/security decisions уже приняты. Это не быстрый технический shortcut.

### Файлы

1. [`README.md`](README.md)
2. возможно новый governance doc:
   - `doc/operator/GITHUB_GOVERNANCE_MODEL_2026-04-11.md`
3. при upgrade/public:
   - GitHub settings, не только код

### Верификация

1. Documentation truthfulness check
2. Если выбран upgrade path:
   - API protection/rulesets больше не возвращают `403`

### Definition of Done

У репозитория есть честно описанная и выполнимая модель server/process enforcement.

## Wave 5. Включить GitHub-native security surfaces

### Проблема

Сейчас выключены:

1. code scanning
2. Dependabot alerts
3. secret scanning

### Что сделать

1. Включить `Dependabot alerts` и `security updates`, если план это позволяет.
2. Оценить включение `code scanning`:
   - если подходит CodeQL, добавить workflow
   - если нет, зафиксировать причину и compensating controls
3. Включить `secret scanning`, если доступно на текущем плане/visibility.
4. Если какая-то функция всё ещё platform-gated, задокументировать это как accepted limitation, а не просто оставить выключенной без объяснения.

### Файлы

1. возможно `.github/dependabot.yml`
2. возможно `.github/workflows/codeql.yml`
3. docs:
   - `SECURITY.md`
   - `doc/security/...`

### Верификация

1. API для соответствующих surfaces перестаёт возвращать `disabled`
2. во вкладке `Security` появляются реальные operational surfaces

### Definition of Done

Security tab либо реально используется, либо её ограничения формально описаны вместе с compensating controls.

## Wave 6. Восстановить GitHub governance surface: Issues, Releases, Tags

### Проблема

Сейчас:

1. `Issues` пусты
2. `Releases` нет
3. `Tags` нет

Это не ломает код, но ослабляет repo governance.

### Что сделать

#### 6A. Issues

1. Создать backlog-issues хотя бы для:
   - `certification_compile observability`
   - `runtime-test-lanes schedule failure`
   - `heavy parity contract split`
   - `GitHub security surfaces enablement`
2. Завести базовые labels:
   - `ci`
   - `github-actions`
   - `security`
   - `governance`
   - `certification`

#### 6B. Releases and tags

1. Определить release policy:
   - manual tagged release on stable milestone
   - или internal milestone tags
2. Создать первый baseline tag после закрытия P0/P1 defects.
3. Если нужны release notes, генерировать их из PR history.

### Файлы

1. GitHub settings
2. возможно:
   - `.github/ISSUE_TEMPLATE/...`
   - `.github/labels` tooling if used
   - release process doc under `doc/operator/`

### Верификация

1. Вкладка `Issues` перестаёт быть пустой случайно
2. Появляется первый tag/release boundary

### Definition of Done

Операционная история и release boundaries видны в GitHub, а не только в markdown docs.

## Рекомендуемый порядок исполнения

1. Wave 1
2. Wave 2
3. Wave 3
4. Wave 4
5. Wave 5
6. Wave 6

Это не косметический порядок, а причинный:

1. без Wave 1 невозможно честно сделать Wave 2
2. без Wave 2 `Actions` останется системно красным
3. без Wave 3 heavy certification signal останется нечестным
4. без Wave 4-5 GitHub как платформа всё ещё будет слабее, чем локальная codebase governance

## Минимальный рабочий backlog

### Task 1

Починить [`run_certification_compile_tests.ps1`](scripts/run_certification_compile_tests.ps1) так, чтобы GitHub logs показывали root cause failure.

### Task 2

Запустить `runtime-test-lanes` вручную только на `certification_compile` и устранить конкретный defect.

### Task 3

Изменить [`ci_gate_heavy.ps1`](scripts/ci_gate_heavy.ps1):

1. сначала `run_parity_golden_batch.ps1`
2. потом `run_generation_parity_gate.ps1`
3. `window gate` вынести из same-day closure

### Task 4

Принять решение по GitHub enforcement model:

1. current-plan process discipline
2. GitHub plan upgrade
3. future public path

### Task 5

Включить доступные GitHub security surfaces и задокументировать недоступные.

### Task 6

Завести issues/tags/releases как реальные governance surfaces.

## Команды проверки

### Локально

```powershell
powershell -ExecutionPolicy Bypass -File scripts/ci_gate.ps1
powershell -ExecutionPolicy Bypass -File scripts/ci_gate_heavy.ps1
```

### GitHub

```powershell
& 'C:\Program Files\GitHub CLI\gh.exe' workflow run runtime-test-lanes --repo rovsh44-glitch/Helper -f run_certification_compile=true
& 'C:\Program Files\GitHub CLI\gh.exe' run list --repo rovsh44-glitch/Helper --limit 20
& 'C:\Program Files\GitHub CLI\gh.exe' run watch <run-id> --repo rovsh44-glitch/Helper --exit-status
& 'C:\Program Files\GitHub CLI\gh.exe' run view <run-id> --repo rovsh44-glitch/Helper --log-failed
```

## Финальный критерий готовности

План считается закрытым только когда одновременно выполнено всё ниже:

1. `repo-gate` на PR и на `main` стабильно зелёный
2. `runtime-test-lanes` schedule больше не системно красный на `certification_compile`
3. failure любого certification lane диагностируется прямо из `Actions`
4. heavy parity contract честный и операционно выполнимый
5. GitHub enforcement model либо реально включён, либо явно ограничен и компенсирован
6. Security tab перестаёт быть пустой по умолчанию или её ограничения формально приняты
7. Issues/Releases/Tags начинают выполнять роль governance surfaces

## Короткий вывод

Следующий правильный шаг не “ещё один зелёный repo-gate”, а именно Wave 1:

починить diagnostic contract `certification_compile`, потому что сейчас это главный блокер для всей оставшейся GitHub remediation.

