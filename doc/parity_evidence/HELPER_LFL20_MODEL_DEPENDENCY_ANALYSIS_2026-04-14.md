# Helper LFL20 Model Dependency Analysis

Date: `2026-04-14`

## Question

Нужно ли перед повторным прогоном `LFL20` менять локальную модель, и зависит ли сам `rerun` от используемой LLM.

## Short Answer

Да, `LFL20 rerun` зависит от используемой локальной LLM.

Но менять модель прямо сейчас нецелесообразно. Сначала нужно добить `LFL20` до зелёного на текущем runtime, затем делать отдельный controlled model-swap experiment.

## Why The Rerun Depends On The Model

- `scripts/run_local_first_librarian_corpus.ps1` отправляет кейсы в `POST /api/chat`.
- Research и factual turns идут через `src/Helper.Api/Conversation/ConversationModelSelectionPolicy.cs`, который для `research` и high-risk route выбирает reasoning path.
- Ответ генерируется через `src/Helper.Api/Backend/ModelGateway/HelperModelGateway.cs` и `src/Helper.Runtime/AILink.Chat.cs`.
- Даже local-baseline stage модельный, а не rule-based: `src/Helper.Runtime/LocalBaselineAnswerService.cs`.

Итого: retrieval, ranking и quality-gates в `Helper` частично кодовые, но итоговый answer shape, abstention behavior, section discipline и часть local-first behavior зависят от модели.

## Current Runtime State

Текущий живой API поднят на:

- `Current=deepseek-r1:1.5b`

Это видно в логе старта:

- `temp/api-ready-lfl20-timeoutfix-2026-04-14/api_ready_stdout.log`

## Gemma 4 26B Review

Источник:

- https://ollama.com/library/gemma4:26b

По официальной странице Ollama:

- `gemma4:26b` позиционируется как сильная workstation reasoning / agentic / coding model.
- Это `MoE`-модель примерно `25.2B` total / `3.8B` active parameters.
- Заявлен `256K` context.
- Поддерживается `system` role.
- У Gemma 4 есть отдельный thinking-mode contract.

## Why Switching To Gemma 4 Now Is Not Optimal

### 1. The Experiment Would Become Confounded

Сейчас нужно проверить эффект свежих кодовых фиксов:

- timeout handling
- browser render recovery
- persistence fix

Если одновременно сменить модель, будет невозможно честно отделить:

- улучшение из-за кода
- улучшение или деградацию из-за новой LLM

### 2. Helper Is Not Yet Tuned For Gemma 4

В Ollama payload `Helper` сейчас задаёт общий контракт:

- обычный `system`
- `temperature=0.6`
- `num_ctx=8192` почти для всех моделей

См. `src/Helper.Runtime/AILink.Chat.cs`.

Это значит:

- реальный `256K` context `gemma4:26b` сейчас не будет использован
- модель не получит отдельной runtime-tuning под свой профиль

### 3. Helper Does Not Yet Handle Gemma 4 Thinking Semantics

В коде `Helper` сейчас нет явной обработки специальных Gemma 4 thinking tokens / thought tags.

То есть при прямом переключении есть риск:

- format pollution
- лишних reasoning tags
- ухудшения strict `LFL20` section compliance

## Practical Conclusion

Правильная последовательность сейчас такая:

1. Довести `LFL20` до зелёного на текущем `deepseek-r1:1.5b`.
2. Зафиксировать claim-ready baseline.
3. Потом сделать отдельный A/B rerun:
   - current baseline model
   - `gemma4:26b`
4. Только после этого решать, стоит ли переводить public-proof baseline на Gemma 4.

## Recommendation

Сейчас `rerun` нужно запускать поверх текущего живого API без смены модели.

К `gemma4:26b` нужно вернуться после стабилизации `LFL20`, как к отдельному model-selection experiment, а не как к части текущего remediation pass.
