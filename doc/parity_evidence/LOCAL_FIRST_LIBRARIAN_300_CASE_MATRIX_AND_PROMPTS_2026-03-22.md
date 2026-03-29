# Local-First Librarian 300-Case Matrix And Prompts

Date: `2026-03-22`

Purpose: stratified benchmark corpus for evaluating a `local-library-first -> web escalation -> source display -> analysis -> conclusion -> opinion` research assistant without embedding that workflow into the user prompt itself.

## Benchmark Principle

This is a benchmark, not an instruction-following drill.

So the prompts below are written as natural user requests. The librarian workflow must live in:

- system policy;
- rubric;
- corpus metadata;
- evaluation logic.

It must not be repeated inside every prompt, otherwise the benchmark mostly measures prompt obedience instead of real research behavior.

## Coverage Model

The corpus uses a fixed matrix:

- `15` domains
- `4` task types
- `5` evidence modes

Formula:

- `15 x 4 x 5 = 300`

## Benchmark Slices

Canonical risk-aligned slices are layered on top of the frozen `300` prompts through corpus metadata and eval scripts, not through prompt rewrites.

- `medical_conflict`
  Health-and-medicine conflict cases where weak source mix and false reconciliation are the core risk.
- `regulation_freshness`
  Current-rule, filing, visa, customs, compliance, and policy cases where freshness is mandatory.
- `paper_analysis`
  Research-paper, literature-review, article-status, and evidence-evaluation cases.
- `multilingual_local_first`
  Non-English local-first cases. In the current frozen corpus this is effectively the Russian slice.
- `sparse_evidence`
  Cases where explicit uncertainty handling matters more than decisive resolution.
- `local_only_strength`
  Local-sufficient cases used to measure whether the local library can answer strongly before web escalation.

## Task Types

- `explain_and_structure`
- `compare_and_choose`
- `plan_actions`
- `review_diagnose_or_critique`

## Evidence Modes

- `local_sufficient`
  Local library should usually be enough; web is optional and only used if local evidence is weak.
- `local_plus_web`
  Start from local knowledge, then supplement or verify with external sources.
- `web_required_fresh`
  Freshness, current availability, current rules, or current market state makes web mandatory.
- `conflict_check`
  Local knowledge is not enough because sources are likely to disagree; assistant must compare and reconcile.
- `uncertain_sparse`
  Evidence is thin, unstable, or low confidence; assistant must be cautious and honest.

## Response Rubric Contract

Every case in this corpus is meant to evaluate whether the assistant can produce:

1. `Local Findings`
   What the local library already supports.
2. `Web Findings`
   What external sources added, corrected, updated, or contradicted.
3. `Sources`
   Explicit source display for everything actually used.
4. `Analysis`
   Comparison, reconciliation, and reasoning across local and web evidence.
5. `Conclusion`
   A clear actionable or factual bottom line.
6. `Opinion`
   A clearly separated judgment or recommendation, labeled as opinion.

## JSONL Field Template

Recommended corpus fields:

```json
{
  "id": "lfwr-001",
  "language": "ru",
  "kind": "local_first_librarian_case",
  "domain": "health_and_medicine",
  "taskType": "explain_and_structure",
  "evidenceMode": "local_sufficient",
  "prompt": "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки.",
  "endToEnd": true,
  "labels": [
    "ru_only",
    "local_first",
    "sources_required",
    "analysis_required",
    "conclusion_required",
    "opinion_required"
  ],
  "sliceIds": [
    "multilingual_local_first",
    "local_only_strength"
  ],
  "localFirst": "required",
  "web": "forbid_unless_local_insufficient",
  "sources": "must_display_all_used_sources",
  "analysis": "must_compare_local_and_web_evidence_when_web_is_used",
  "conclusion": "must_provide_clear_conclusion",
  "opinion": "must_provide_separate_labeled_opinion",
  "minWebSources": 0,
  "responseSections": [
    "local_findings",
    "web_findings",
    "sources",
    "analysis",
    "conclusion",
    "opinion"
  ]
}
```

## Domain Matrix

| Code | Domain | Cases | Prompt IDs | Coverage |
| --- | --- | ---: | --- | --- |
| `D01` | Health and medicine | 20 | `LFWR-001..020` | `4 task types x 5 evidence modes` |
| `D02` | Psychology, relationships, personal development | 20 | `LFWR-021..040` | `4 task types x 5 evidence modes` |
| `D03` | Education and learning | 20 | `LFWR-041..060` | `4 task types x 5 evidence modes` |
| `D04` | Science and research | 20 | `LFWR-061..080` | `4 task types x 5 evidence modes` |
| `D05` | Programming and IT | 20 | `LFWR-081..100` | `4 task types x 5 evidence modes` |
| `D06` | Engineering and manufacturing | 20 | `LFWR-101..120` | `4 task types x 5 evidence modes` |
| `D07` | Business and management | 20 | `LFWR-121..140` | `4 task types x 5 evidence modes` |
| `D08` | Finance, accounting, and taxes | 20 | `LFWR-141..160` | `4 task types x 5 evidence modes` |
| `D09` | Law, civic, and compliance | 20 | `LFWR-161..180` | `4 task types x 5 evidence modes` |
| `D10` | Marketing, media, and creative work | 20 | `LFWR-181..200` | `4 task types x 5 evidence modes` |
| `D11` | Logistics, transport, and travel | 20 | `LFWR-201..220` | `4 task types x 5 evidence modes` |
| `D12` | Housing, construction, and real estate | 20 | `LFWR-221..240` | `4 task types x 5 evidence modes` |
| `D13` | Environment, energy, and agriculture | 20 | `LFWR-241..260` | `4 task types x 5 evidence modes` |
| `D14` | Family, household, and consumer life | 20 | `LFWR-261..280` | `4 task types x 5 evidence modes` |
| `D15` | Public safety, security, and emergency | 20 | `LFWR-281..300` | `4 task types x 5 evidence modes` |

## Prompt List

### D01. Health And Medicine

- `LFWR-001 | explain_and_structure | local_sufficient | Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки и в каких случаях обычному человеку нужны оба типа тренировок.`
- `LFWR-002 | explain_and_structure | local_plus_web | Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.`
- `LFWR-003 | explain_and_structure | web_required_fresh | Объясни, что сейчас известно о текущей вспышке кори в Европе и какие официальные меры профилактики рекомендуют на сегодня.`
- `LFWR-004 | explain_and_structure | conflict_check | Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.`
- `LFWR-005 | explain_and_structure | uncertain_sparse | Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.`
- `LFWR-006 | compare_and_choose | local_sufficient | Сравни ходьбу и велотренажёр для начинающего человека с лишним весом и чувствительными коленями.`
- `LFWR-007 | compare_and_choose | local_plus_web | Сравни креатин моногидрат и креатин HCL, а затем проверь, что предпочитают актуальные спортивные и клинические источники.`
- `LFWR-008 | compare_and_choose | web_required_fresh | Сравни доступные сегодня в Ташкенте варианты онлайн-консультации дерматолога по цене, скорости записи и репутации.`
- `LFWR-009 | compare_and_choose | conflict_check | Сравни холодные ванны и сауну для восстановления после силовых тренировок, если исследования и популярные обзоры дают разные выводы.`
- `LFWR-010 | compare_and_choose | uncertain_sparse | Сравни коллагеновые добавки и обычный пищевой белок для поддержки суставов и кожи, если доказательная база ограничена.`
- `LFWR-011 | plan_actions | local_sufficient | Составь 8-недельный план улучшения сна для офисного сотрудника, который поздно ложится и просыпается уставшим.`
- `LFWR-012 | plan_actions | local_plus_web | Составь план безопасного возвращения к бегу после шестимесячного перерыва и проверь его по актуальным рекомендациям спортивной медицины.`
- `LFWR-013 | plan_actions | web_required_fresh | Составь актуальный на сегодня план догоняющей вакцинации для взрослого, который переезжает в Германию в 2026 году.`
- `LFWR-014 | plan_actions | conflict_check | Составь план питания при погранично высоком холестерине, если американские и европейские рекомендации различаются в деталях.`
- `LFWR-015 | plan_actions | uncertain_sparse | Составь осторожный план тестирования standing desk для снижения боли в спине, если исследования по эффективности противоречивы.`
- `LFWR-016 | review_diagnose_or_critique | local_sufficient | Оцени мой режим тренировок: 5 силовых дней подряд без кардио и один день полного отдыха; где здесь главные риски для новичка?`
- `LFWR-017 | review_diagnose_or_critique | local_plus_web | Оцени мой дневной рацион при преддиабете и проверь его по свежим официальным рекомендациям: сладкий йогурт утром, рис на обед, фрукты вечером, сок перед сном.`
- `LFWR-018 | review_diagnose_or_critique | web_required_fresh | Проверь, насколько актуален мой список лекарств и прививок для поездки в Таиланд на апрель 2026 года.`
- `LFWR-019 | review_diagnose_or_critique | conflict_check | Разбери, почему разные источники дают разные нормы белка для людей старше 60 лет, и какой вывод выглядит самым обоснованным.`
- `LFWR-020 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что магнитные браслеты уменьшают хроническое воспаление.`

### D02. Psychology, Relationships, And Personal Development

- `LFWR-021 | explain_and_structure | local_sufficient | Объясни простыми словами, чем стресс отличается от тревоги и когда одно начинает переходить в другое.`
- `LFWR-022 | explain_and_structure | local_plus_web | Объясни, как обычно работают техники когнитивно-поведенческой терапии против навязчивого пережёвывания мыслей, а затем проверь это по свежим рекомендациям.`
- `LFWR-023 | explain_and_structure | web_required_fresh | Объясни, какие сервисы психологической поддержки и кризисные горячие линии реально доступны сегодня в Ташкенте.`
- `LFWR-024 | explain_and_structure | conflict_check | Объясни, помогает ли регулярный journaling снижать выгорание, если self-help-источники и исследования расходятся.`
- `LFWR-025 | explain_and_structure | uncertain_sparse | Объясни, насколько серьёзно стоит воспринимать идеи про dopamine detox как рабочий психологический инструмент.`
- `LFWR-026 | compare_and_choose | local_sufficient | Сравни метод Pomodoro и длинные блоки deep work для студента, который быстро отвлекается.`
- `LFWR-027 | compare_and_choose | local_plus_web | Сравни дневник мыслей и mindfulness-практику для снижения тревожной руминации, а затем дополни это актуальными evidence-based источниками.`
- `LFWR-028 | compare_and_choose | web_required_fresh | Сравни актуальные онлайн-платформы для парной терапии на русском языке по цене, доступности и прозрачности условий.`
- `LFWR-029 | compare_and_choose | conflict_check | Сравни позиции источников по влиянию социальных сетей на самооценку подростков, если выводы заметно расходятся.`
- `LFWR-030 | compare_and_choose | uncertain_sparse | Сравни EMDR и tapping для повседневного стресса, если качество доказательной базы неровное.`
- `LFWR-031 | plan_actions | local_sufficient | Составь 4-недельный план снижения прокрастинации для человека, который откладывает сложные задачи до ночи.`
- `LFWR-032 | plan_actions | local_plus_web | Составь план улучшения концентрации для студента в экзаменационный период и проверь его по текущим доказательным рекомендациям.`
- `LFWR-033 | plan_actions | web_required_fresh | Составь актуальный список шагов, куда можно обратиться сегодня ночью за кризисной психологической помощью в Ташкенте.`
- `LFWR-034 | plan_actions | conflict_check | Составь план снижения выгорания для remote team lead, если экспертные источники спорят о минимуме встреч и норме отдыха.`
- `LFWR-035 | plan_actions | uncertain_sparse | Составь осторожный self-help-план для человека с возможными признаками adult ADHD, не выдавая его за замену диагностики.`
- `LFWR-036 | review_diagnose_or_critique | local_sufficient | Оцени мой anti-stress routine: кофе в 18:00, doomscrolling перед сном, рабочие сообщения в кровати и никаких прогулок.`
- `LFWR-037 | review_diagnose_or_critique | local_plus_web | Оцени мой план улучшения внимания ребёнка через reward charts и проверь его по свежим рекомендациям детской психологии.`
- `LFWR-038 | review_diagnose_or_critique | web_required_fresh | Проверь, насколько актуальны на сегодня условия, ограничения и репутационные сигналы у крупных сервисов онлайн-психотерапии.`
- `LFWR-039 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по вопросу, нужно ли всегда валидировать каждую эмоцию в конфликтном разговоре.`
- `LFWR-040 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что subliminal audio может надёжно повысить уверенность в себе за 30 дней.`

### D03. Education And Learning

- `LFWR-041 | explain_and_structure | local_sufficient | Объясни разницу между active recall и пассивным перечитыванием материала.`
- `LFWR-042 | explain_and_structure | local_plus_web | Объясни, как работает project-based learning, а затем проверь это по более свежим исследованиям и практикам.`
- `LFWR-043 | explain_and_structure | web_required_fresh | Объясни, какие текущие изменения в 2026 году появились в правилах поступления на магистратуру по computer science в ведущих университетах Германии.`
- `LFWR-044 | explain_and_structure | conflict_check | Объясни, стоит ли вообще использовать теорию learning styles, если исследования и популярные образовательные источники противоречат друг другу.`
- `LFWR-045 | explain_and_structure | uncertain_sparse | Объясни, насколько убедительны данные о реальной эффективности курсов speed reading.`
- `LFWR-046 | compare_and_choose | local_sufficient | Сравни карточки для запоминания и конспекты-саммари для изучения анатомии.`
- `LFWR-047 | compare_and_choose | local_plus_web | Сравни стратегии подготовки к IELTS и TOEFL, а затем проверь актуальные изменения формата экзаменов.`
- `LFWR-048 | compare_and_choose | web_required_fresh | Сравни актуальные онлайн-платформы английского языка для детей в Узбекистане по цене, формату и прозрачности программы.`
- `LFWR-049 | compare_and_choose | conflict_check | Сравни школьные подходы к домашним заданиям, если исследования по их эффективности расходятся.`
- `LFWR-050 | compare_and_choose | uncertain_sparse | Сравни gamified math apps и традиционные упражнения для ранней математики, если доказательства неоднородны.`
- `LFWR-051 | plan_actions | local_sufficient | Составь 12-недельный план изучения алгебры с нуля для взрослого.`
- `LFWR-052 | plan_actions | local_plus_web | Составь план подготовки к IELTS Writing и проверь его по текущим официальным band descriptors и материалам.`
- `LFWR-053 | plan_actions | web_required_fresh | Составь актуальный план подачи на стипендию Erasmus Mundus по data science в 2026 году.`
- `LFWR-054 | plan_actions | conflict_check | Составь план домашнего обучения чтению, если источники спорят между phonics и balanced literacy.`
- `LFWR-055 | plan_actions | uncertain_sparse | Составь осторожный self-study-путь к роли AI product manager, если сама роль и требования пока нестабильны.`
- `LFWR-056 | review_diagnose_or_critique | local_sufficient | Оцени мой учебный режим: 6 часов перечитывания, ноль practice tests и никакого spaced repetition.`
- `LFWR-057 | review_diagnose_or_critique | local_plus_web | Оцени мой план bilingual education для ребёнка и проверь его по актуальным developmental guidance.`
- `LFWR-058 | review_diagnose_or_critique | web_required_fresh | Проверь, какие из онлайн-курсов по machine learning из моего списка всё ещё активны, обновляются и реально стоят оплаты сегодня.`
- `LFWR-059 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по вопросу, вредят ли ноутбуки на лекциях обучению или помогают ему.`
- `LFWR-060 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что brain-training games заметно повышают IQ у здоровых взрослых.`

### D04. Science And Research

- `LFWR-061 | explain_and_structure | local_sufficient | Объясни разницу между корреляцией и причинностью на простых примерах.`
- `LFWR-062 | explain_and_structure | local_plus_web | Объясни, как устроен peer review, а затем проверь, как в последние годы меняются open-review практики.`
- `LFWR-063 | explain_and_structure | web_required_fresh | Объясни, что в последнем обновлении IPCC или Copernicus говорится о текущей глобальной температурной аномалии.`
- `LFWR-064 | explain_and_structure | conflict_check | Объясни, насколько сегодня правдоподобны claims о room-temperature superconductivity, если источники резко расходятся.`
- `LFWR-065 | explain_and_structure | uncertain_sparse | Объясни, насколько убедительны текущие данные о прямом вреде микропластика для здоровья человека.`
- `LFWR-066 | compare_and_choose | local_sufficient | Сравни observational studies и randomized controlled trials.`
- `LFWR-067 | compare_and_choose | local_plus_web | Сравни arXiv preprints и peer-reviewed journal papers, а затем проверь текущие практики издателей и репозиториев.`
- `LFWR-068 | compare_and_choose | web_required_fresh | Сравни текущие расходы на open-access публикацию в крупных ML-конференциях и журналах, если подаваться сегодня.`
- `LFWR-069 | compare_and_choose | conflict_check | Сравни конкурирующие объяснения replication crisis в психологии и биомедицине.`
- `LFWR-070 | compare_and_choose | uncertain_sparse | Сравни доказательства в пользу quantum biology в навигации птиц и более консервативных моделей.`
- `LFWR-071 | plan_actions | local_sufficient | Составь план критического чтения научной статьи для начинающего исследователя.`
- `LFWR-072 | plan_actions | local_plus_web | Составь план небольшого literature review по тепловым насосам и дополни его свежими исследованиями об эффективности.`
- `LFWR-073 | plan_actions | web_required_fresh | Составь актуальный на 2026 год план подачи NLP-статьи на подходящую конференцию с учётом дедлайнов и профиля площадок.`
- `LFWR-074 | plan_actions | conflict_check | Составь план оценки конкурирующих исследований по интервальному голоданию и долголетию.`
- `LFWR-075 | plan_actions | uncertain_sparse | Составь осторожный исследовательский план по теме низких доз лития в питьевой воде и психического здоровья, если evidence sparse.`
- `LFWR-076 | review_diagnose_or_critique | local_sufficient | Разбери вывод: sample size 18 доказывает, что лечение работает для всех.`
- `LFWR-077 | review_diagnose_or_critique | local_plus_web | Оцени мой метод literature review и проверь его по актуальным guidance для systematic reviews.`
- `LFWR-078 | review_diagnose_or_critique | web_required_fresh | Проверь, была ли статья, на которую я ссылаюсь, отозвана, исправлена или сильно оспорена по состоянию на сегодня.`
- `LFWR-079 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по оценкам climate sensitivity и какой вывод при этом остаётся безопасным.`
- `LFWR-080 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что научно уже доказано существование сознания у современных ИИ.`

### D05. Programming And IT

- `LFWR-081 | explain_and_structure | local_sufficient | Объясни разницу между threading, async I/O и multiprocessing.`
- `LFWR-082 | explain_and_structure | local_plus_web | Объясни, как работают passkeys, а затем проверь текущую поддержку на основных браузерах и платформах.`
- `LFWR-083 | explain_and_structure | web_required_fresh | Объясни, что изменилось в последнем стабильном релизе React и какие изменения реально важны сегодня.`
- `LFWR-084 | explain_and_structure | conflict_check | Объясни, действительно ли microservices лучше modular monolith, если инженерные источники спорят.`
- `LFWR-085 | explain_and_structure | uncertain_sparse | Объясни, насколько надёжны текущие claims о fully autonomous AI software engineers.`
- `LFWR-086 | compare_and_choose | local_sufficient | Сравни REST и GraphQL для внутреннего бизнес-приложения среднего размера.`
- `LFWR-087 | compare_and_choose | local_plus_web | Сравни PostgreSQL и MySQL для SaaS backend, а затем дополни ответ по текущему вектору развития релизов и экосистемы.`
- `LFWR-088 | compare_and_choose | web_required_fresh | Сравни текущие варианты аренды cloud GPU для fine-tuning модели на 7B параметров в этом месяце.`
- `LFWR-089 | compare_and_choose | conflict_check | Сравни позиции источников по поводу TypeScript на backend, если мнения расходятся по сложности и безопасности.`
- `LFWR-090 | compare_and_choose | uncertain_sparse | Сравни vector databases и классический search для маленькой команды, если большая часть benchmark-ов vendor-shaped.`
- `LFWR-091 | plan_actions | local_sufficient | Составь 10-недельный план перехода в C#, если я уже знаю Python.`
- `LFWR-092 | plan_actions | local_plus_web | Составь план миграции от password login к passkeys и проверь его по текущей платформенной документации.`
- `LFWR-093 | plan_actions | web_required_fresh | Составь актуальный план деплоя небольшого production app в AWS с самыми дешёвыми и ещё поддерживаемыми компонентами на сегодня.`
- `LFWR-094 | plan_actions | conflict_check | Составь план рефакторинга из монолита в модульную архитектуру, если советы команды и блоги конфликтуют.`
- `LFWR-095 | plan_actions | uncertain_sparse | Составь осторожный план внедрения AI coding assistants в регулируемой компании, где claims о продуктивности пока спорные.`
- `LFWR-096 | review_diagnose_or_critique | local_sufficient | Оцени эту идею архитектуры: одна общая база, один общий репозиторий, но 14 отдельных "микросервисов" в деплое.`
- `LFWR-097 | review_diagnose_or_critique | local_plus_web | Оцени мою backup strategy для SaaS-приложения и проверь её по актуальным cloud reliability guidance.`
- `LFWR-098 | review_diagnose_or_critique | web_required_fresh | Проверь, есть ли сегодня у выбранных мной зависимостей нерешённые критические уязвимости.`
- `LFWR-099 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по вопросу, не является ли Kubernetes overkill для стартапа из 10 человек.`
- `LFWR-100 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что переход на Rust автоматически снимает большую часть security risk.`

### D06. Engineering And Manufacturing

- `LFWR-101 | explain_and_structure | local_sufficient | Объясни разницу между tensile strength, yield strength и hardness.`
- `LFWR-102 | explain_and_structure | local_plus_web | Объясни основы lean manufacturing и проверь, поддерживаются ли те же ключевые метрики в текущих отраслевых источниках.`
- `LFWR-103 | explain_and_structure | web_required_fresh | Объясни, что говорят последние отзывы и recalls по батарейной безопасности в потребительской электронике в этом году.`
- `LFWR-104 | explain_and_structure | conflict_check | Объясни, действительно ли 3D printing экономически выгоден для малосерийного производства, если отраслевые источники спорят.`
- `LFWR-105 | explain_and_structure | uncertain_sparse | Объясни, насколько правдоподобны текущие обещания по массовой доступности solid-state batteries в ближайшие годы.`
- `LFWR-106 | compare_and_choose | local_sufficient | Сравни CNC machining и injection molding для небольших пластиковых деталей.`
- `LFWR-107 | compare_and_choose | local_plus_web | Сравни brushed и brushless motors, а затем дополни ответ актуальными supplier и maintenance guidance.`
- `LFWR-108 | compare_and_choose | web_required_fresh | Сравни доступные сейчас недорогие industrial IoT sensors для контроля температуры и вибрации на складе.`
- `LFWR-109 | compare_and_choose | conflict_check | Сравни steel frame и reinforced concrete для mid-rise building, если инженерные и стоимостные оценки расходятся по регионам.`
- `LFWR-110 | compare_and_choose | uncertain_sparse | Сравни hydrogen fuel cells и advanced batteries для тяжёлой строительной техники, если коммерческих данных пока мало.`
- `LFWR-111 | plan_actions | local_sufficient | Составь пошаговый план улучшения quality control на маленькой мебельной производственной линии.`
- `LFWR-112 | plan_actions | local_plus_web | Составь preventive maintenance plan для CNC-оборудования и дополни его актуальной vendor guidance.`
- `LFWR-113 | plan_actions | web_required_fresh | Составь текущий procurement plan для небольшой solar-powered pump system из компонентов, которые реально доступны сейчас.`
- `LFWR-114 | plan_actions | conflict_check | Составь план выбора между local fabrication и импортными компонентами, если cost и reliability sources конфликтуют.`
- `LFWR-115 | plan_actions | uncertain_sparse | Составь осторожный pilot plan по внедрению collaborative robots в маленькой мастерской, если evidence по ROI ограничен.`
- `LFWR-116 | review_diagnose_or_critique | local_sufficient | Оцени эту производственную схему: нет входного контроля, нет журнала калибровки и есть только один финальный визуальный осмотр.`
- `LFWR-117 | review_diagnose_or_critique | local_plus_web | Оцени мой warehouse layout plan и проверь его по актуальным safety и ergonomics guidance.`
- `LFWR-118 | review_diagnose_or_critique | web_required_fresh | Проверь, есть ли у выбранных мной sensor modules текущая доступность поставок и живая vendor support.`
- `LFWR-119 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по ожидаемому сроку службы residential solar inverters.`
- `LFWR-120 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что additive manufacturing заменит большую часть conventional spare-parts production в течение пяти лет.`

### D07. Business And Management

- `LFWR-121 | explain_and_structure | local_sufficient | Объясни разницу между gross margin, operating margin и cash flow.`
- `LFWR-122 | explain_and_structure | local_plus_web | Объясни, что такое product-market fit, а затем дополни это актуальными мыслями операторов и практиков.`
- `LFWR-123 | explain_and_structure | web_required_fresh | Объясни, что текущие данные по потребительским настроениям и инфляции означают для малого ритейла в этом месяце.`
- `LFWR-124 | explain_and_structure | conflict_check | Объясни, действительно ли remote-first компании продуктивнее, если management sources противоречат друг другу.`
- `LFWR-125 | explain_and_structure | uncertain_sparse | Объясни, насколько надёжны claims о том, что четырёхдневная рабочая неделя улучшает output практически в любой отрасли.`
- `LFWR-126 | compare_and_choose | local_sufficient | Сравни bootstrapping и venture financing для основателя SaaS-компании.`
- `LFWR-127 | compare_and_choose | local_plus_web | Сравни OKRs и balanced scorecard, а затем дополни ответ актуальными management practice sources.`
- `LFWR-128 | compare_and_choose | web_required_fresh | Сравни текущие website builders и commerce platforms для запуска небольшого fashion brand сегодня.`
- `LFWR-129 | compare_and_choose | conflict_check | Сравни centralized и decentralized decision-making, если leadership sources спорят об их эффективности.`
- `LFWR-130 | compare_and_choose | uncertain_sparse | Сравни community-led growth и paid acquisition для B2B tools, если evidence сильно зависит от контекста.`
- `LFWR-131 | plan_actions | local_sufficient | Составь 90-дневный план для нового operations manager в небольшой логистической компании.`
- `LFWR-132 | plan_actions | local_plus_web | Составь процесс ревизии pricing plan для SaaS-бизнеса и дополни его текущими рыночными примерами.`
- `LFWR-133 | plan_actions | web_required_fresh | Составь актуальный go-to-market plan для открытия specialty coffee shop в Ташкенте в 2026 году.`
- `LFWR-134 | plan_actions | conflict_check | Составь hiring plan для первой sales-команды, если эксперты спорят между founder-led sales и SDR-first подходом.`
- `LFWR-135 | plan_actions | uncertain_sparse | Составь осторожный experimentation plan по добавлению subscription membership в офлайн-ритейл.`
- `LFWR-136 | review_diagnose_or_critique | local_sufficient | Оцени мой набор KPI: только revenue, без retention, margin и customer support metrics.`
- `LFWR-137 | review_diagnose_or_critique | local_plus_web | Оцени мой onboarding process для новых сотрудников и проверь его по текущим HR и management best practices.`
- `LFWR-138 | review_diagnose_or_critique | web_required_fresh | Проверь, остаются ли мой список конкурентов и assumptions по pricing актуальными сегодня.`
- `LFWR-139 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по вопросу, работает ли unlimited PTO на практике.`
- `LFWR-140 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что AI agents уже могут заменить большую часть middle management.`

### D08. Finance, Accounting, And Taxes

- `LFWR-141 | explain_and_structure | local_sufficient | Объясни разницу между ETF, index fund и mutual fund.`
- `LFWR-142 | explain_and_structure | local_plus_web | Объясни, как работает dollar-cost averaging, а затем дополни ответ актуальными guidance о том, где этот подход полезен, а где нет.`
- `LFWR-143 | explain_and_structure | web_required_fresh | Объясни, что текущий тренд по ставкам центральных банков означает для заёмщиков по ипотеке в этом месяце.`
- `LFWR-144 | explain_and_structure | conflict_check | Объясни, действительно ли золото является надёжным inflation hedge, если источники расходятся по периодам и методикам.`
- `LFWR-145 | explain_and_structure | uncertain_sparse | Объясни, насколько убедительны текущие claims о криптовалюте как долгосрочном средстве сохранения стоимости.`
- `LFWR-146 | compare_and_choose | local_sufficient | Сравни досрочное погашение долга и инвестирование свободных денег для семьи с умеренной склонностью к риску.`
- `LFWR-147 | compare_and_choose | local_plus_web | Сравни cash-basis и accrual accounting для маленького сервисного бизнеса и проверь это по актуальной tax-practice guidance.`
- `LFWR-148 | compare_and_choose | web_required_fresh | Сравни текущие brokerage fees и условия счетов для покупки global ETF из Узбекистана сегодня.`
- `LFWR-149 | compare_and_choose | conflict_check | Сравни dividend investing и broad index investing, если источники спорят между income-подходом и total return.`
- `LFWR-150 | compare_and_choose | uncertain_sparse | Сравни robo-advisors и самостоятельное инвестирование для новичка, если evidence по долгосрочному результату смешанный.`
- `LFWR-151 | plan_actions | local_sufficient | Составь годовой план стабилизации бюджета семьи с нерегулярным фриланс-доходом.`
- `LFWR-152 | plan_actions | local_plus_web | Составь план bookkeeping для маленького агентства и проверь его по текущим tax-compliance guidance.`
- `LFWR-153 | plan_actions | web_required_fresh | Составь актуальный filing checklist для remote worker, который выставляет инвойсы иностранным клиентам из Узбекистана в 2026 году.`
- `LFWR-154 | plan_actions | conflict_check | Составь план погашения долгов, если источники спорят между debt avalanche и snowball method.`
- `LFWR-155 | plan_actions | uncertain_sparse | Составь осторожный план выделения 5% портфеля на высоковолатильные активы без завышенных ожиданий доходности.`
- `LFWR-156 | review_diagnose_or_critique | local_sufficient | Оцени мой месячный бюджет: нет emergency fund, только minimum payments по долгам, три BNPL-покупки и никаких sinking funds.`
- `LFWR-157 | review_diagnose_or_critique | local_plus_web | Оцени мой процесс учета инвойсов и расходов и проверь его по актуальной guidance для малого бизнеса.`
- `LFWR-158 | review_diagnose_or_critique | web_required_fresh | Проверь, актуальны ли налоговые thresholds и reporting deadlines, которыми я пользуюсь сегодня.`
- `LFWR-159 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по safe withdrawal rate для пенсии.`
- `LFWR-160 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что одна rental apartment всегда лучше диверсифицированного ETF-портфеля.`

### D09. Law, Civic, And Compliance

- `LFWR-161 | explain_and_structure | local_sufficient | Объясни простыми словами разницу между breach of contract, negligence и fraud.`
- `LFWR-162 | explain_and_structure | local_plus_web | Объясни, как работает GDPR consent, а затем проверь, уточнилось ли что-то важное в недавнем enforcement guidance.`
- `LFWR-163 | explain_and_structure | web_required_fresh | Объясни, что последние изменения в регулировании ИИ в ЕС означают сегодня для маленького software vendor.`
- `LFWR-164 | explain_and_structure | conflict_check | Объясни, остаются ли non-compete clauses исполнимыми, если юридические источники расходятся по юрисдикциям.`
- `LFWR-165 | explain_and_structure | uncertain_sparse | Объясни, насколько надёжны текущие юридические комментарии о copyright ownership для AI-generated works.`
- `LFWR-166 | compare_and_choose | local_sufficient | Сравни sole proprietorship и LLC для маленькой freelance design studio.`
- `LFWR-167 | compare_and_choose | local_plus_web | Сравни NDA и non-compete agreement, а затем дополни это текущими источниками о юридической практике.`
- `LFWR-168 | compare_and_choose | web_required_fresh | Сравни актуальные визовые пути для software engineer, который хочет переехать в Германию в 2026 году.`
- `LFWR-169 | compare_and_choose | conflict_check | Сравни open-source licenses для коммерческого SaaS-использования, если юридические блоги дают конфликтующие трактовки.`
- `LFWR-170 | compare_and_choose | uncertain_sparse | Сравни legal risk web scraping по разным юрисдикциям, если guidance всё ещё нестабилен.`
- `LFWR-171 | plan_actions | local_sufficient | Составь checklist документов для подписания простого service contract с freelance developer.`
- `LFWR-172 | plan_actions | local_plus_web | Составь compliance plan для сбора email-подписок на newsletter и проверь его по текущему privacy guidance.`
- `LFWR-173 | plan_actions | web_required_fresh | Составь актуальный relocation paperwork plan для регистрации small business в UAE в 2026 году.`
- `LFWR-174 | plan_actions | conflict_check | Составь response plan для employee monitoring policy, если privacy и security sources конфликтуют.`
- `LFWR-175 | plan_actions | uncertain_sparse | Составь осторожный plan для использования generative AI в marketing content при неустойчивых copyright rules.`
- `LFWR-176 | review_diagnose_or_critique | local_sufficient | Оцени это условие договора: поставщик может менять цену, срок и scope в любое время без письменного согласования.`
- `LFWR-177 | review_diagnose_or_critique | local_plus_web | Оцени моё website privacy notice и проверь его по актуальной публичной guidance.`
- `LFWR-178 | review_diagnose_or_critique | web_required_fresh | Проверь, актуальны ли import restrictions и customs rules для ввоза дрона в ЕС на сегодня.`
- `LFWR-179 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по вопросу, считается ли обучение на публичных веб-данных fair use.`
- `LFWR-180 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что один disclaimer всегда снимает юридическую ответственность.`

### D10. Marketing, Media, And Creative Work

- `LFWR-181 | explain_and_structure | local_sufficient | Объясни разницу между brand awareness, consideration и conversion.`
- `LFWR-182 | explain_and_structure | local_plus_web | Объясни, как работает editorial calendar, а затем дополни ответ актуальными platform best practices.`
- `LFWR-183 | explain_and_structure | web_required_fresh | Объясни, что текущие сдвиги в алгоритмах TikTok и Instagram означают для organic reach в этом месяце.`
- `LFWR-184 | explain_and_structure | conflict_check | Объясни, действительно ли long-form content по-прежнему лучше short-form, если маркетинговые источники спорят.`
- `LFWR-185 | explain_and_structure | uncertain_sparse | Объясни, насколько убедительны claims о том, что AI-generated ad copy обгоняет human copy в долгую.`
- `LFWR-186 | compare_and_choose | local_sufficient | Сравни direct response marketing и brand marketing для нового потребительского продукта.`
- `LFWR-187 | compare_and_choose | local_plus_web | Сравни growth через newsletter и growth через short video, а затем дополни это текущими creator-platform realities.`
- `LFWR-188 | compare_and_choose | web_required_fresh | Сравни текущие email marketing tools для небольшого ecommerce brand по цене, automation и deliverability reputation.`
- `LFWR-189 | compare_and_choose | conflict_check | Сравни SEO и paid search, если практики спорят по поводу durability и CAC.`
- `LFWR-190 | compare_and_choose | uncertain_sparse | Сравни influencer marketing и UGC creators для нишевого B2B software, если evidence ограничен.`
- `LFWR-191 | plan_actions | local_sufficient | Составь 60-дневный content plan для B2B cybersecurity newsletter.`
- `LFWR-192 | plan_actions | local_plus_web | Составь launch plan для подкаста и проверь его по текущим рекомендациям по distribution и discoverability.`
- `LFWR-193 | plan_actions | web_required_fresh | Составь актуальный cross-platform launch plan для indie game, которая выходит в следующем квартале.`
- `LFWR-194 | plan_actions | conflict_check | Составь marketing mix plan для нового skincare brand, если источники спорят, остаётся ли paid social эффективным.`
- `LFWR-195 | plan_actions | uncertain_sparse | Составь осторожный plan по использованию generative AI в visual branding без потери distinctiveness.`
- `LFWR-196 | review_diagnose_or_critique | local_sufficient | Оцени идею этой landing page: семь CTA, никакого proof, никаких намёков на цену и один гигантский абзац текста.`
- `LFWR-197 | review_diagnose_or_critique | local_plus_web | Оцени мой newsletter funnel и проверь его по актуальной email deliverability guidance.`
- `LFWR-198 | review_diagnose_or_critique | web_required_fresh | Проверь, остаются ли актуальными и репутационно безопасными создатели контента из моего списка партнёрств на сегодня.`
- `LFWR-199 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по вопросу, стоит ли малому локальному бизнесу до сих пор вкладываться в SEO.`
- `LFWR-200 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что один viral Reel гарантирует устойчивый рост бренда.`

### D11. Logistics, Transport, And Travel

- `LFWR-201 | explain_and_structure | local_sufficient | Объясни разницу между direct shipping, cross-docking и warehousing.`
- `LFWR-202 | explain_and_structure | local_plus_web | Объясни, как работает dynamic pricing у авиакомпаний, а затем дополни ответ текущими наблюдениями travel industry.`
- `LFWR-203 | explain_and_structure | web_required_fresh | Объясни, какие visa-free и transit rules для граждан Узбекистана действуют сегодня при поездке во Вьетнам.`
- `LFWR-204 | explain_and_structure | conflict_check | Объясни, действительно ли поезд всегда климатически лучше самолёта, если источники считают emissions по-разному.`
- `LFWR-205 | explain_and_structure | uncertain_sparse | Объясни, насколько надёжны текущие claims о delivery robots в плотной городской среде.`
- `LFWR-206 | compare_and_choose | local_sufficient | Сравни sea freight и air freight для небольших партий электроники.`
- `LFWR-207 | compare_and_choose | local_plus_web | Сравни carry-on-only и checked-baggage travel strategies, а затем дополни это текущими трендами airline baggage policies.`
- `LFWR-208 | compare_and_choose | web_required_fresh | Сравни текущие маршруты из Ташкента в Токио в этом месяце по времени в пути, багажу и гибкости возврата.`
- `LFWR-209 | compare_and_choose | conflict_check | Сравни road trip на EV и на гибриде, если источники по доступности зарядки расходятся по регионам.`
- `LFWR-210 | compare_and_choose | uncertain_sparse | Сравни travel insurance и self-insurance для частого путешественника, если данные по реальным claims неполные.`
- `LFWR-211 | plan_actions | local_sufficient | Составь packing plan и plan временных буферов для двухнедельной multi-city business trip.`
- `LFWR-212 | plan_actions | local_plus_web | Составь inventory replenishment plan для маленького ecommerce shop и проверь его по актуальным logistics guidance.`
- `LFWR-213 | plan_actions | web_required_fresh | Составь актуальный relocation travel plan для переезда семьи из Ташкента в Берлин летом 2026 года.`
- `LFWR-214 | plan_actions | conflict_check | Составь план выбора между local 3PL и in-house fulfillment, если отзывы и benchmark-источники конфликтуют.`
- `LFWR-215 | plan_actions | uncertain_sparse | Составь осторожный pilot plan для запуска same-day delivery в одном городе до национального масштабирования.`
- `LFWR-216 | review_diagnose_or_critique | local_sufficient | Оцени этот warehouse process: нет barcode scanning, нет slotting logic и ручной stock count только раз в квартал.`
- `LFWR-217 | review_diagnose_or_critique | local_plus_web | Оцени мою business travel policy и проверь её по текущим airline flexibility norms.`
- `LFWR-218 | review_diagnose_or_critique | web_required_fresh | Проверь, остаётся ли мой planned itinerary рабочим сегодня с учётом strikes, route suspensions и entry-rule changes.`
- `LFWR-219 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по поводу лучшего дня и времени для покупки авиабилетов.`
- `LFWR-220 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что hyperloop-like systems скоро изменят mainstream passenger logistics.`

### D12. Housing, Construction, And Real Estate

- `LFWR-221 | explain_and_structure | local_sufficient | Объясни разницу между load-bearing wall, partition wall и shear wall.`
- `LFWR-222 | explain_and_structure | local_plus_web | Объясни, как работает mortgage amortization, а затем дополни это актуальными примерами lending practice.`
- `LFWR-223 | explain_and_structure | web_required_fresh | Объясни, что текущие условия рынка жилья в Ташкенте означают сегодня для арендаторов и first-time buyers.`
- `LFWR-224 | explain_and_structure | conflict_check | Объясни, повышает ли open-plan home office продуктивность, если architectural и psychological sources расходятся.`
- `LFWR-225 | explain_and_structure | uncertain_sparse | Объясни, насколько правдоподобны текущие claims о том, что 3D-printed houses решат проблему массовой доступности жилья.`
- `LFWR-226 | compare_and_choose | local_sufficient | Сравни laminate, engineered wood и tile flooring для семейной квартиры.`
- `LFWR-227 | compare_and_choose | local_plus_web | Сравни heat pumps и gas boilers, а затем дополни ответ текущими efficiency и maintenance guidance.`
- `LFWR-228 | compare_and_choose | web_required_fresh | Сравни текущие property listing platforms в Ташкенте по качеству inventory, прозрачности и репутации агентов.`
- `LFWR-229 | compare_and_choose | conflict_check | Сравни покупку и аренду жилья, если market commentaries конфликтуют по ближайшей ценовой динамике.`
- `LFWR-230 | compare_and_choose | uncertain_sparse | Сравни smart-home systems для экономии энергии, если vendor evidence слабый и неоднородный.`
- `LFWR-231 | plan_actions | local_sufficient | Составь renovation plan для небольшой двухкомнатной квартиры с ограниченным бюджетом и без structural changes.`
- `LFWR-232 | plan_actions | local_plus_web | Составь home energy-efficiency upgrade plan и проверь его по актуальным public guidance и incentives, где они есть.`
- `LFWR-233 | plan_actions | web_required_fresh | Составь актуальный due-diligence plan для покупки квартиры в Дубае в 2026 году.`
- `LFWR-234 | plan_actions | conflict_check | Составь план выбора insulation materials, если cost, fire safety и sustainability sources тянут в разные стороны.`
- `LFWR-235 | plan_actions | uncertain_sparse | Составь осторожный pilot plan по превращению части жилья в short-term rental space.`
- `LFWR-236 | review_diagnose_or_critique | local_sufficient | Оцени эту идею ремонта: объединить кухню и гостиную, вынести стиральную машину на балкон и повесить тяжёлые полки на тонкую внутреннюю стену.`
- `LFWR-237 | review_diagnose_or_critique | local_plus_web | Оцени мой landlord-tenant checklist и проверь его по актуальной practical guidance.`
- `LFWR-238 | review_diagnose_or_critique | web_required_fresh | Проверь, актуальны ли developer, permits и обещания по utilities у этого нового жилого проекта.`
- `LFWR-239 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по вопросу, хорошее ли сейчас время для mortgage refinance.`
- `LFWR-240 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что все smart thermostats быстро окупаются независимо от климата и типа здания.`

### D13. Environment, Energy, And Agriculture

- `LFWR-241 | explain_and_structure | local_sufficient | Объясни разницу между weather, climate и climate variability.`
- `LFWR-242 | explain_and_structure | local_plus_web | Объясни основы regenerative agriculture и затем дополни ответ текущими field evidence и caveats.`
- `LFWR-243 | explain_and_structure | web_required_fresh | Объясни, что текущие drought conditions и reservoir levels означают сегодня для фермеров в Центральной Азии.`
- `LFWR-244 | explain_and_structure | conflict_check | Объясни, действительно ли nuclear power менее рискованна, чем fossil fuels, если источники используют разные risk frames.`
- `LFWR-245 | explain_and_structure | uncertain_sparse | Объясни, насколько убедительны текущие данные об enhanced rock weathering как масштабируемом climate solution.`
- `LFWR-246 | compare_and_choose | local_sufficient | Сравни drip irrigation и sprinkler irrigation по эффективности использования воды.`
- `LFWR-247 | compare_and_choose | local_plus_web | Сравни rooftop solar и solar water heating, а затем дополни ответ текущими cost и maintenance guidance.`
- `LFWR-248 | compare_and_choose | web_required_fresh | Сравни current home battery options, доступные в моём регионе, по capacity, warranty и local support.`
- `LFWR-249 | compare_and_choose | conflict_check | Сравни organic и conventional farming по yield и soil health, если источники спорят в зависимости от crop и region.`
- `LFWR-250 | compare_and_choose | uncertain_sparse | Сравни carbon capture и direct air capture как climate investments, если коммерческих данных всё ещё мало.`
- `LFWR-251 | plan_actions | local_sufficient | Составь сезонный план kitchen garden для томатов, зелени и огурцов в жарком климате.`
- `LFWR-252 | plan_actions | local_plus_web | Составь household electricity reduction plan и проверь его по актуальным efficiency guidance.`
- `LFWR-253 | plan_actions | web_required_fresh | Составь актуальный procurement plan для rooftop solar на небольшом складе в Ташкенте в 2026 году.`
- `LFWR-254 | plan_actions | conflict_check | Составь farm water-management plan, если agronomy и weather sources расходятся по ожидаемым осадкам.`
- `LFWR-255 | plan_actions | uncertain_sparse | Составь осторожный pilot plan по внедрению biochar на небольшом хозяйстве.`
- `LFWR-256 | review_diagnose_or_critique | local_sufficient | Оцени такой irrigation approach: ежедневный полив в полдень, без мульчи и с одинаковым графиком для всех культур.`
- `LFWR-257 | review_diagnose_or_critique | local_plus_web | Оцени мой household energy-saving plan и проверь его по актуальной appliance-efficiency guidance.`
- `LFWR-258 | review_diagnose_or_critique | web_required_fresh | Проверь, делают ли текущие subsidy, tariff или net-metering rules rooftop solar всё ещё финансово разумным решением здесь.`
- `LFWR-259 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по вопросу, всегда ли electric vehicles экологичнее гибридов.`
- `LFWR-260 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что vertical farming заменит полевое земледелие для staple crops.`

### D14. Family, Household, And Consumer Life

- `LFWR-261 | explain_and_structure | local_sufficient | Объясни разницу между fixed household costs, variable costs и irregular annual costs.`
- `LFWR-262 | explain_and_structure | local_plus_web | Объясни, как оценивать крупную покупку бытовой техники, а затем дополни ответ текущими источниками о надёжности и ремонте.`
- `LFWR-263 | explain_and_structure | web_required_fresh | Объясни, какие текущие consumer alerts и recalls важны, если я выбираю детское автокресло сегодня.`
- `LFWR-264 | explain_and_structure | conflict_check | Объясни, должны ли screen-time limits быть жёсткими или гибкими, если parenting sources расходятся.`
- `LFWR-265 | explain_and_structure | uncertain_sparse | Объясни, насколько надёжны claims о том, что air fryer всегда здоровее обычной духовки.`
- `LFWR-266 | compare_and_choose | local_sufficient | Сравни bulk shopping и weekly shopping для бюджета семьи из четырёх человек.`
- `LFWR-267 | compare_and_choose | local_plus_web | Сравни robot vacuum и upright vacuum, а затем дополни ответ текущими repairability и reliability signals.`
- `LFWR-268 | compare_and_choose | web_required_fresh | Сравни текущие family travel cards или loyalty programs, которые всё ещё дают хорошую ценность сегодня.`
- `LFWR-269 | compare_and_choose | conflict_check | Сравни homeschooling и traditional schooling для ребёнка, который тяжело социализируется, если источники расходятся.`
- `LFWR-270 | compare_and_choose | uncertain_sparse | Сравни meal-kit subscriptions и самостоятельное планирование готовки для снижения семейного стресса, если evidence ограничен.`
- `LFWR-271 | plan_actions | local_sufficient | Составь household budget plan на 3 месяца для семьи, которая хочет сократить бессмысленные траты.`
- `LFWR-272 | plan_actions | local_plus_web | Составь buying plan для первого ребёнка и проверь его по актуальным safety и consumer guidance.`
- `LFWR-273 | plan_actions | web_required_fresh | Составь актуальный relocation plan для семьи с двумя детьми, которая переезжает в другую страну в 2026 году.`
- `LFWR-274 | plan_actions | conflict_check | Составь план screen-time rules для 10-летнего ребёнка, если pediatric, school и parenting sources различаются.`
- `LFWR-275 | plan_actions | uncertain_sparse | Составь осторожный plan по тестированию smart-home child monitoring tools без избыточного сбора семейных данных.`
- `LFWR-276 | review_diagnose_or_critique | local_sufficient | Оцени мой weekly household routine: продукты покупаются каждый день, нет meal prep, нет общего календаря и нет ревизии recurring bills.`
- `LFWR-277 | review_diagnose_or_critique | local_plus_web | Оцени мой child sleep routine и проверь его по актуальным pediatric sleep guidance.`
- `LFWR-278 | review_diagnose_or_critique | web_required_fresh | Проверь, есть ли у товаров в моём baby registry текущие recalls, safety flags или discontinued models.`
- `LFWR-279 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по вопросу, улучшает ли allowance финансовую ответственность детей.`
- `LFWR-280 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что blue-light glasses заметно улучшают сон практически у всех.`

### D15. Public Safety, Security, And Emergency

- `LFWR-281 | explain_and_structure | local_sufficient | Объясни разницу между hazard, risk, vulnerability и mitigation.`
- `LFWR-282 | explain_and_structure | local_plus_web | Объясни, как обычно работает phishing, а затем дополни ответ текущими примерами attacker tactics.`
- `LFWR-283 | explain_and_structure | web_required_fresh | Объясни, какие актуальные предупреждения по кибербезопасности для пользователей Android появились в этом месяце.`
- `LFWR-284 | explain_and_structure | conflict_check | Объясни, действительно ли public CCTV заметно снижает преступность, если criminology sources расходятся.`
- `LFWR-285 | explain_and_structure | uncertain_sparse | Объясни, насколько убедительны claims о том, что consumer face-recognition tools реально предотвращают identity fraud.`
- `LFWR-286 | compare_and_choose | local_sufficient | Сравни роли fire extinguisher, fire blanket и smoke detector в домашней безопасности.`
- `LFWR-287 | compare_and_choose | local_plus_web | Сравни password manager и hardware security key для личной защиты аккаунтов, а затем дополни ответ текущими guidance.`
- `LFWR-288 | compare_and_choose | web_required_fresh | Сравни текущие personal VPN providers по transparency, audits и jurisdiction.`
- `LFWR-289 | compare_and_choose | conflict_check | Сравни armed и unarmed school security models, если public-safety sources спорят об их эффективности.`
- `LFWR-290 | compare_and_choose | uncertain_sparse | Сравни smart-home cameras и neighborhood watch как способы снижения burglary risk, если evidence ограничен.`
- `LFWR-291 | plan_actions | local_sufficient | Составь домашний emergency kit и evacuation checklist для семьи из четырёх человек.`
- `LFWR-292 | plan_actions | local_plus_web | Составь small-business phishing defense plan и проверь его по актуальным security guidance.`
- `LFWR-293 | plan_actions | web_required_fresh | Составь актуальный cyber hygiene plan для журналиста, который в 2026 году перевозит устройства через границы.`
- `LFWR-294 | plan_actions | conflict_check | Составь preparedness plan для extreme heat, если municipal и health sources отличаются по порогам и действиям.`
- `LFWR-295 | plan_actions | uncertain_sparse | Составь осторожный plan по тестированию AI-based fraud detection в маленьком онлайн-магазине.`
- `LFWR-296 | review_diagnose_or_critique | local_sufficient | Оцени мою домашнюю safety setup: нет проверки батареек smoke alarm, нет meeting point и лекарства лежат рядом с бытовой химией.`
- `LFWR-297 | review_diagnose_or_critique | local_plus_web | Оцени мою company password policy и проверь её по актуальным security best practices.`
- `LFWR-298 | review_diagnose_or_critique | web_required_fresh | Проверь, есть ли сегодня у приложений и smart cameras, которыми я пользуюсь, известные security issues или privacy controversies.`
- `LFWR-299 | review_diagnose_or_critique | conflict_check | Разбери, почему источники расходятся по вопросу, повышает ли безопасность подростков запрет на social media.`
- `LFWR-300 | review_diagnose_or_critique | uncertain_sparse | Оцени утверждение, что одного annual cybersecurity awareness video достаточно для заметного снижения employee risk.`

## Recommended Next Step

If you want this turned from a prompt catalog into a fully runnable benchmark package, the next practical step is:

1. materialize all `300` cases into a real JSONL corpus;
2. add per-case `minWebSources`, `freshnessSensitivity`, and `expectedFailureMode`;
3. attach a rubric that scores:
   - local-first behavior,
   - honest web escalation,
   - source display quality,
   - analytical quality,
   - conclusion quality,
   - opinion quality.
