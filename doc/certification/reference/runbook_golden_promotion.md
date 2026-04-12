# Runbook: Golden Template Promotion

## Цель
Оперативно диагностировать и восстановить pipeline `routing -> forge -> promotion -> certification -> activation`.

## Быстрая проверка
1. Проверить профиль promotion:
`powershell -ExecutionPolicy Bypass -File scripts/invoke_helper_cli.ps1 template-promotion-profile`
2. Проверить parity gates:
`powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_gate.ps1`
3. Проверить certification gate:
`powershell -ExecutionPolicy Bypass -File scripts/run_template_certification_gate.ps1`

## Частые причины падения
1. `compile_gate`:
   - ошибки компиляции candidate.
   - действия: открыть `template certification report`, исправить source template/candidate, повторить.
2. `certification`:
   - не пройдены metadata/smoke/artifact checks.
   - действия: исправить `template.json` и smoke-условия.
3. `activation`:
   - версия не активируется в lifecycle.
   - действия: выполнить ручной rollback.
4. `post_activation_certification`:
   - активированная версия не проходит повторную full certification при явно включённом `HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY=true`.
   - действия: rollback на предыдущую active и повторная сертификация.
5. `post_activation_verification`:
   - activation прошёл, но опубликованное дерево/статус/report/lifecycle не совпали с сертифицированным candidate.
   - действия: rollback на предыдущую active, проверить `certification_status.json`, report path и integrity mismatch.

## Точки данных
1. `/api/metrics`
2. `/api/metrics/prometheus`
3. `/api/templates/promotion-profile`
4. `/api/templates/certification-gate`

## Ручной rollback
`powershell -ExecutionPolicy Bypass -File scripts/invoke_helper_cli.ps1 template-rollback <TemplateId>`

## Примечание по compile-hang диагностике
- для обычной проверки promotion/certification использовать wrapper без blame:
  `powershell -ExecutionPolicy Bypass -File scripts\run_certification_compile_tests.ps1 -Configuration Debug`
- forensic режим включать отдельно:
  `powershell -ExecutionPolicy Bypass -File scripts\run_certification_compile_tests.ps1 -Configuration Debug -EnableBlameHang -BlameHangTimeoutSec 180`
- raw `--blame-hang-timeout 60s` больше не считается валидным baseline для compile lane, потому что давал ложный hang-signal на console promotion path.

Подробный протокол отката:
`doc/certification/reference/runbook_template_rollback.md`
