# Web-Research Post-GitHub 30-Day Collection Plan

Date: `2026-03-21`
Status: `operational plan`
Purpose: собрать публичный `>=200` dialog blind corpus и `>=8` external reviewer submissions для authoritative web-research parity proof после публичного запуска `Helper` на GitHub.

## 1. Базовый принцип

Опора на [HELPER GitHub Showcase Strategy](../archive/top_level_history/HELPER_GITHUB_SHOWCASE_STRATEGY_2026-03-24.md):

- публичный GitHub должен быть `витриной доверия`, а не обязательным раскрытием всего production core;
- основной код и коммерчески чувствительные части могут оставаться private;
- public repo может быть canonical evidence hub: docs, demos, issue/discussion intake, blind-eval artifacts, финальные parity reports.

Рекомендуемая модель:

- `public canonical repo`: showcase + public prompt intake + evidence publication;
- `private core repo`: production code, sensitive modules, infra, due-diligence materials.

## 2. Что именно должно считаться canonical evidence

К концу 30-дневного окна в public canonical repo должны появиться:

- `eval/web_research_blind_eval/source/response_pairs.jsonl`
- `eval/web_research_blind_eval/manifests/*`
- `eval/web_research_blind_eval/packets/*`
- `eval/web_research_blind_eval/merged/*`
- sanitized reviewer CSV submissions
- `doc/web_research_parity_report_latest.md`
- `doc/web_research_parity_report_latest.integrity.json`
- `doc/web_research_parity_report_latest.blind_pack_validation.json`
- `doc/web_research_parity_report_latest.blind_eval_authoritative.md`

Hard target:

- `>=200` real web-research dialog pairs
- `>=8` external reviewers recruited
- `>=4` reviewers minimum in final authoritative counted set
- all structured note columns complete

## 3. Что должно быть публичным, а что нет

Публично можно и нужно:

- README с приглашением submit/review
- GitHub Discussions / Issue Forms для prompt intake
- reviewer application form
- frozen corpus after round close
- sanitized reviewer artifacts after round close
- финальные reports и manifests

Публично нельзя до закрытия раунда:

- live blind score sheets других reviewer-ов
- reveal map
- Helper/Baseline labels
- reviewer PII
- private user telemetry

Во время активного blind round reviewer submissions должны собираться в private intake channel и публиковаться в canonical repo только после закрытия раунда и sanitize-pass.

## 4. Intake architecture

### 4.1. Public prompt intake

Использовать в public GitHub repo:

- `GitHub Discussions` category: `Web Research Challenge`
- `Issue Form`: `Submit a web-research prompt`
- labels:
  - `blind-corpus:intake`
  - `task-family:latest`
  - `task-family:comparison`
  - `task-family:contradiction`
  - `task-family:regulation`
  - `task-family:local`
  - `task-family:sports-finance`
  - `task-family:follow-up`

Каждый submitter должен явно подтверждать:

- prompt не содержит PII, credentials, internal URLs, private documents;
- prompt можно публиковать в canonical repo;
- prompt можно использовать в blinded comparison against baseline systems.

### 4.2. Reviewer intake

Использовать отдельный public application path:

- `Issue Form`: `Apply as blind reviewer`
- либо `Discussion`: `Reviewer recruitment`

Собирать:

- public handle
- contact email
- languages
- domain familiarity
- timezone
- conflicts of interest

Минимальный pool для запуска:

- `8-12` external reviewers recruited
- coverage по языкам и task families

### 4.3. Private reviewer submission channel

Рекомендуемый путь:

- `private intake repo` или private mailbox для blind CSV

Не использовать public PR/Issues для live reviewer sheets, иначе ломается blindness.

После закрытия раунда:

- импортировать CSV
- sanitize reviewer identity до stable `reviewer_id`
- коммитить sanitized submissions в canonical repo

## 5. 30-day execution plan

## Days 1-3. Public launch setup

Сделать:

1. Опубликовать public showcase repo.
2. Добавить в `README` отдельные CTA:
   - submit a prompt
   - apply as reviewer
   - request demo / investor contact
3. Открыть `Discussions` и issue forms.
4. Опубликовать:
   - repository scope
   - privacy / submission rules
   - reviewer requirements
   - web-research evaluation program note

Exit criteria:

- public repo live
- prompt intake open
- reviewer application open
- no ambiguity about public/private boundary

## Days 4-10. Corpus intake burst

Сделать:

1. Ежедневно triage incoming prompts.
2. Удалять:
   - duplicates
   - vague prompts
   - prompts without real web-research need
   - prompts with private data
3. Нормализовать accepted prompts в candidate bank.
4. Следить за quota balance по task families.

Operational targets:

- `250-300` accepted candidate prompts
- не менее `30` prompts на critical family
- не более `30%` корпуса из одной family

Exit criteria:

- candidate bank достаточно широкий для freeze
- reviewer applications достигли хотя бы `8`

## Days 11-15. Corpus freeze and response-pair build

Сделать:

1. Заморозить final candidate set `220-240`.
2. Downselect to canonical `>=200` balanced prompts.
3. Для каждого prompt получить:
   - `helper_response`
   - `baseline_response`
4. Проверить schema и balance.
5. Сохранить corpus как:
   - `eval/web_research_blind_eval/source/response_pairs.jsonl`

Важно:

- corpus должен быть `genuinely web-research focused`, как требует [WEB_RESEARCH_BLIND_EVAL_AUTHORITATIVE_RUNBOOK.md](./WEB_RESEARCH_BLIND_EVAL_AUTHORITATIVE_RUNBOOK.md)
- local/latest/current prompts должны быть реально currentness-sensitive

Exit criteria:

- `response_pairs.jsonl` готов
- `>=200` dialogs
- balanced family distribution validated

## Days 16-18. Reviewer pool close and assignment

Сделать:

1. Закрыть reviewer applications.
2. Отфильтровать conflicts of interest.
3. Сформировать final reviewer pool `8-12`.
4. Подготовить assignment file.
5. Запустить authoritative prepare phase:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_web_research_blind_eval_authoritative.ps1 `
  -Phase Prepare `
  -ReviewerPoolCsv reviewer_pool.csv
```

Exit criteria:

- blind packet generated
- assignment manifest generated
- handoff pack exported

## Days 19-24. Blind review round

Сделать:

1. Разослать reviewer handoff pack.
2. Дать fixed deadline `5-6` дней.
3. Collect blind CSV submissions privately.
4. Каждый день проверять только:
   - file receipt
   - schema completeness
   - missing columns

Нельзя в этот момент:

- делать reveal
- публиковать промежуточные scores
- подсказывать другим reviewer-ам distribution или current averages

Exit criteria:

- `>=4` complete reviewer submissions minimum
- лучше `>=8` total received

## Days 25-27. Validation and top-up

Сделать:

1. Проверить completion и structured notes.
2. Если часть reviewer-ов выпала, добрать reserve reviewers.
3. Убедиться, что reviewer diversity проходит policy из [REVIEWER_REQUIREMENTS.md](./REVIEWER_REQUIREMENTS.md).
4. Только после этого запускать finalization.

Exit criteria:

- no missing structured-note columns
- reviewer diversity likely pass
- counted reviewer pool sufficient

## Days 28-30. Finalization and publication

Запустить:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_web_research_blind_eval_authoritative.ps1 `
  -Phase Finalize
```

Сделать:

1. Import blind reviews.
2. Controlled reveal.
3. Integrity validation.
4. Generate canonical report.
5. Commit sanitized blind-eval artifacts into public canonical repo.
6. Publish short summary post in GitHub Discussions / Releases / README changelog.

Success state:

- authoritative report generated
- provenance PASS
- reviewer diversity PASS
- integrity PASS
- canonical repo contains full audit trail

## 6. Canonical repo mechanics

Рекомендуемая структура public repo:

- `README.md`
- `CONTACT.md`
- `docs/`
- `media/`
- `eval/web_research_blind_eval/source/`
- `eval/web_research_blind_eval/manifests/`
- `eval/web_research_blind_eval/packets/`
- `eval/web_research_blind_eval/merged/`
- `doc/parity_evidence/*`

Рекомендуемые GitHub surfaces:

- `Discussions`: prompt intake, reviewer recruitment, release summaries
- `Issue Forms`: prompt submission, reviewer application
- `Releases`: demo/reports snapshots
- `Projects`: intake board with columns `New / Accepted / Rejected / Frozen / Reviewed / Published`

## 7. Anti-abuse and trust rules

Нужно жёстко держать:

- no synthetic prompts in canonical round
- no internal/private support logs
- no anonymous post-hoc editing of accepted prompts after freeze
- no reviewer self-assignment
- no public reveal before import closes
- no publication of raw reviewer PII

Рекомендуемые controls:

- frozen corpus commit hash
- assignment manifest commit hash
- blind packet validation artifact
- final integrity sidecar
- maintainer log of excluded submissions

## 8. KPI targets

Launch-month targets:

- `250-300` prompt submissions
- `220-240` accepted candidates
- `>=200` frozen canonical dialogs
- `8-12` external reviewers recruited
- `>=6` complete blind reviewer returns desirable
- `>=4` counted reviewers minimum authoritative gate

Quality targets:

- no single task family > `30%`
- no single reviewer > `45%` of counted rows
- preferred reviewer spread per family: `4`

## 9. Why this works for buyer/investor search

Эта схема совместима с логикой из [HELPER GitHub Showcase Strategy](../archive/top_level_history/HELPER_GITHUB_SHOWCASE_STRATEGY_2026-03-24.md):

- public repo показывает зрелость и external validation;
- parity evidence повышает trust;
- private core сохраняет IP exclusivity;
- buyer/investor видит не просто claims, а проверяемый public audit trail.

То есть GitHub в этой модели работает как:

- showcase for serious interest
- intake surface for public prompts and reviewer recruitment
- canonical evidence hub after round close

а не как обязательный full source disclosure.

## 10. Final operational rule

До GitHub launch нужно подготовить tooling и docs.

После GitHub launch нужно собирать:

- prompts публично;
- reviewer applications публично;
- reviewer score sheets приватно;
- canonical evidence публиковать только после round close и sanitize/finalize chain.

Именно эта схема позволяет честно собрать публичный `>=200` blind corpus и external reviewer submissions, не разрушая blindness и не раскрывая коммерчески чувствительное ядро проекта.
