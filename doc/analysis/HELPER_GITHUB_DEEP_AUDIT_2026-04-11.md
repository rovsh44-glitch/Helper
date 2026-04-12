# HELPER GitHub Deep Audit 2026-04-11

## Область и проверенный срез

Аудит выполнен по живому удалённому репозиторию `https://github.com/rovsh44-glitch/Helper` и его актуальному `main`.

Проверенный срез на момент аудита:

- `origin/main`: `b4ec4183885d8047c5584ce403bdd3e36e6bc687`
- локальный рабочий кодовый срез содержательно совпадает с `origin/main`: `git diff --stat origin/main` пустой
- локальный `HEAD` остаётся на pre-squash коммите `cb8c9c990772582e90a59c9b704efac08b04b48f`, но дерево чистое и контентно эквивалентно `origin/main`

Источники фактов:

- `gh repo view`
- `gh api repos/...`
- `gh workflow list`
- `gh run list`
- `gh run view --log-failed`
- локальное чтение workflow/script файлов

## Краткий вывод

Репозиторий в GitHub-смысле заметно оздоровлен: remediation из PR `#30` уже в `main`, deterministic `repo-gate` на `main` проходит, а PR-путь стал рабочим и воспроизводимым.

Но глубокий аудит показывает, что operational maturity ещё не доведена до конца. Главные оставшиеся риски не в базовом коде продукта, а в GitHub-операционке:

1. scheduled `runtime-test-lanes` уже минимум три дня подряд падает в `certification_compile`
2. эти падения почти недиагностируемы из `Actions`, потому что wrapper-script скрывает причину дочернего `dotnet test`
3. зелёный `repo-gate` не означает зелёный heavy/certification-контур
4. private-repo на текущем плане GitHub не даёт branch protection/rulesets, поэтому server-side enforcement фактически отсутствует
5. GitHub-native security surfaces выключены: `code scanning`, `Dependabot alerts`, `secret scanning`

## Критические findings

### 1. Scheduled `runtime-test-lanes` системно красный, а `Actions` не объясняет почему

Подтверждённые факты:

- В репозитории сейчас два активных workflow: `repo-gate` и `runtime-test-lanes`.
- Последние три scheduled-run `runtime-test-lanes` завершились `failure`:
  - `24172530478` от `2026-04-09`
  - `24226861144` от `2026-04-10`
  - `24274532576` от `2026-04-11`
- Во всех трёх случаях падает job `certification_compile`.
- GitHub logs в этих run показывают только:
  - старт lane-процесса
  - cleanup пустого run-root
  - `Process completed with exit code 1`

Причинно-следственная связь:

- [`scripts/run_certification_compile_tests.ps1`](../../scripts/run_certification_compile_tests.ps1) на строках `373-375` запускает дочерний процесс с:
  - `RedirectStandardOutput = $false`
  - `RedirectStandardError = $false`
  - `CreateNoWindow = $true`
- затем на строке `397` выбрасывает только общий `exit code`:
  - `Certification compile lane failed with exit code ...`

Практический эффект:

- `Actions` честно показывает красный scheduled-lane, но не даёт root cause.
- Это operational defect, а не просто cosmetic logging issue: следующий падёж снова будет opaque.
- Текущая вкладка `Actions` поэтому частично теряет ценность как диагностика compile-certification regression.

Оценка: `High`

## 2. Зелёный `repo-gate` не закрывает красный heavy/certification контур

Подтверждённые факты:

- PR-run `repo-gate` после фикса portability blocker прошёл:
  - run `24285184405`
- Push-run `repo-gate` на `main` после merge PR `#30` тоже прошёл:
  - run `24285458028`
  - итог: `success`
  - длительность: `9m43s`
- Но heavy-path остаётся отдельным и всё ещё structurally red по parity KPI.

Кодовые факты:

- [`scripts/ci_gate_heavy.ps1`](../../scripts/ci_gate_heavy.ps1):
  - строка `23`: вызывает `run_generation_parity_gate.ps1`
  - строка `31`: вызывает `run_generation_parity_window_gate.ps1 -WindowDays 7`
  - при этом не вызывает `run_parity_golden_batch.ps1`
- [`scripts/run_parity_golden_batch.ps1`](../../scripts/run_parity_golden_batch.ps1):
  - строка `159`: `success_rate >= 0.95`
  - строка `161`: `golden_attempts >= 20`
  - строка `162`: `golden_hit_rate >= 0.90`
  - строка `176`: `Thresholds failed`

Данные репозитория подтверждают, что parity evidence по-прежнему плохое:

- `doc/parity_nightly/history/parity_2026-04-11_13-53-17.json`
- `doc/parity_nightly/history/parity_2026-04-11_06-13-43.json`
- `doc/parity_nightly/daily/parity_2026-04-11.json`

Во всех этих snapshots зафиксирован один и тот же alert:

- `insufficient_golden_sample: 0 < 20 (GoldenSource=runtime_fallback)`

Практический эффект:

- `main` выглядит зелёным по branch-facing workflow.
- Но certification claim по heavy parity всё ещё не закрыт реальными данными.
- Это не ломает текущий merge-path, но создаёт разрыв между “репозиторий green” и “система сертификационно здорова”.

Оценка: `High`

## 3. Server-side enforcement отсутствует как класс платформы

Подтверждённые факты:

- API `branches/main/protection` вернул `403`:
  - `Upgrade to GitHub Pro or make this repository public to enable this feature.`
- API `rulesets` тоже вернул `403` с тем же ограничением.

Что это значит practically:

- На текущем private-repo и текущем GitHub-плане нельзя опереться на нормальные branch protection / rulesets.
- Текущий процесс держится на дисциплине, workflow health и правах администратора, а не на server-enforced policy.

Риск:

- Даже хороший `repo-gate` остаётся мягким контрактом.
- При прямом admin-push или обходном процессе GitHub сам не сможет жёстко запретить невалидный merge так, как это делается на защищённых ветках.

Оценка: `High`

## 4. GitHub-native security surfaces выключены

Подтверждённые факты:

- `code scanning` API:
  - `Code scanning is not enabled for this repository.`
- `Dependabot alerts` API:
  - `Dependabot alerts are disabled for this repository.`
- `secret scanning` API:
  - `Secret scanning is disabled on this repository.`

Следствие:

- Репозиторий сейчас в основном опирается на свои кастомные guards, например `secret_scan.ps1` и `nuget_security_gate.ps1`.
- Это лучше, чем ничего, но хуже нативного layered coverage в GitHub.

Риск:

- Нет GitHub-side alert stream по supply-chain, secret exposure и code scanning findings.
- Вкладка `Security` фактически пустая как operational surface.

Оценка: `High`

## 5. Governance surface незрелая: `Issues` пусты, `Releases` нет, `Tags` нет

Подтверждённые факты:

- `Issues` включены, но список пуст.
- `Releases` пусты.
- `Tags` пусты.
- При этом репозиторий недавно пережил серию существенных remediation-работ и CI/governance fixes.

Следствие:

- История реальных проблем и решений сейчас живёт в:
  - PR history
  - workflow history
  - markdown artifacts в `doc/analysis`
- Но не в issue/release discipline.

Риск:

- Операционные дефекты трудно отслеживать как backlog на уровне GitHub.
- Нет release boundary, поэтому `main` фактически одновременно является и dev line, и release line.

Оценка: `Medium`

## 6. Лицензионный surface честный, но для GitHub классифицируется как `NOASSERTION`

Подтверждённые факты:

- В репозитории есть [`LICENSE`](../../LICENSE), [`CONTACT.md`](../../CONTACT.md) и [`SECURITY.md`](../../SECURITY.md).
- Текст `LICENSE` фактически proprietary:
  - `All rights reserved.`
  - использование без письменного разрешения запрещено.
- GitHub API классифицирует лицензию как:
  - `licenseInfo.key = other`
  - `spdx_id = NOASSERTION`

Следствие:

- Это не ошибка как таковая.
- Но для внешних потребителей и tooling GitHub репозиторий не имеет стандартной SPDX-лицензии.

Если репозиторий снова станет `Public`, это потребует отдельного product/legal решения.

Оценка: `Medium`

## Audit по вкладкам репозитория

### `Code`

Состояние:

- `main` живой, не архивный, private.
- Актуальное содержимое стабилизировано после merge PR `#30`.
- Локальный кодовый аудит подтверждает, что merged remediation реально присутствует в дереве.

Плюсы:

- deterministic CI-path починен
- workflow-файлы синхронизированы с текущим contract
- `LICENSE`, `CONTACT.md`, `SECURITY.md` присутствуют

Минусы:

- heavy/certification path всё ещё не замкнут в зелёный operational contract

### `Pull requests`

Состояние:

- PR-driven история выглядит дисциплинированной.
- PR `#30`:
  - `Implement unified remediation and repo gate closure`
  - `MERGED`
- Репозиторий настроен на:
  - `allow_squash_merge = true`
  - `allow_merge_commit = false`
  - `allow_rebase_merge = false`
  - `delete_branch_on_merge = true`

Плюсы:

- история PR чистая и последовательная
- squash-only политика уменьшает шум истории

Минусы:

- без branch protection эта дисциплина не enforced server-side

### `Issues`

Состояние:

- вкладка включена
- issues сейчас отсутствуют

Критическая оценка:

- это governance gap
- для репозитория с реальными CI и runtime defect-history пустая issue-surface означает, что GitHub Issues не используются как официальный operational backlog

### `Actions`

Состояние:

- активные workflow:
  - `repo-gate`
  - `runtime-test-lanes`

Хорошее:

- `repo-gate` на PR теперь стабилен
- `repo-gate` на `main` после merge PR `#30` прошёл полностью
- portability bug с отсутствующим `rg` устранён
- в workflow добавлен `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: "true"`

Плохое:

- `runtime-test-lanes` schedule красный три дня подряд
- failing job повторяется: `certification_compile`
- GitHub logs не раскрывают реальную причину child-process failure

Дополнительное замечание:

- `repo-gate` после opt-in на Node 24 всё ещё даёт annotation:
  - actions `checkout/setup-dotnet/setup-node@v4` target Node 20, но принудительно исполняются на Node 24
- Сейчас это не blocker, но это будущий maintenance item, а не полностью закрытая тема

### `Projects`

Состояние:

- выключены: `has_projects = false`

Оценка:

- допустимо для маленькой команды
- но в комбинации с пустыми `Issues` это усиливает governance vacuum

### `Wiki`

Состояние:

- выключена: `has_wiki = false`

Оценка:

- проблемой само по себе не является
- роль wiki у этого проекта фактически выполняет `doc/`

### `Discussions`

Состояние:

- выключены: `has_discussions = false`

Оценка:

- нейтрально
- для private operator-repo это нормально

### `Security`

Состояние:

- code scanning: disabled
- Dependabot alerts: disabled
- secret scanning: disabled

Оценка:

- это реальный слабый участок
- security tab не несёт рабочей ценности как monitoring surface

### `Releases`

Состояние:

- releases отсутствуют

Оценка:

- нет release discipline
- невозможно быстро понять, какие состояния `main` считались выпускными

### `Tags`

Состояние:

- tags отсутствуют

Оценка:

- нет даже lightweight release anchors

### `Insights`

Что можно утверждать по доступным данным:

- активность по PR высокая
- merge cadence интенсивный
- operational changes идут быстро

Что нельзя честно утверждать:

- без дополнительных GitHub surfaces нет смысла делать вид, что вкладка `Insights` даёт здесь зрелый release-quality signal

## Что реально улучшилось по сравнению с предыдущим состоянием

1. `repo-gate` теперь живой и подтверждённо проходит на `main`.
2. PR-path больше не ломается на отсутствии `rg` в runner environment.
3. Node 24 transition частично учтён через workflow-level opt-in.
4. PR `#30` действительно доставил remediation в удалённый `main`, а не остался только локальным набором патчей.

## Что остаётся сделать

### Priority 1

Починить observability `certification_compile` lane:

1. изменить [`scripts/run_certification_compile_tests.ps1`](../../scripts/run_certification_compile_tests.ps1)
2. перестать терять stdout/stderr дочернего `dotnet test`
3. публиковать tail ошибки прямо в workflow log
4. не удалять весь diagnostic context при cleanup

### Priority 2

Принять честное решение по heavy parity contract:

1. либо добавить `run_parity_golden_batch.ps1` перед `run_generation_parity_gate.ps1`
2. либо вынести parity-window gate в отдельный scheduled certification contract
3. либо перестать трактовать текущий heavy lane как готовый certification gate

### Priority 3

Закрыть GitHub governance gap:

1. либо перейти на GitHub plan / visibility mode, где доступны branch protection и rulesets
2. либо честно признать, что enforcement остаётся process-based, а не platform-enforced

### Priority 4

Включить GitHub-native security surfaces:

1. code scanning
2. Dependabot alerts
3. secret scanning

### Priority 5

Добавить release/governance discipline:

1. начать заводить реальные issues под operational defects
2. определить release/tag policy
3. перестать держать всю operational историю только в PR и `doc/analysis`

## Итоговая оценка

Состояние репозитория на `2026-04-11`:

- как `mergeable and branch-facing repo`: `good`
- как `deterministic CI repo`: `good`
- как `deep operationally governed repo`: `incomplete`
- как `certification-grade GitHub surface`: `not yet`

Главная мысль аудита:

`Helper` уже больше не выглядит как сломанный репозиторий.  
Но он всё ещё выглядит как репозиторий, у которого зелёный основной gate опережает зрелость его GitHub governance, security surfaces и heavy certification discipline.
