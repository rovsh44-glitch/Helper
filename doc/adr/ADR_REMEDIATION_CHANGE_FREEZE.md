# ADR — remediation change freeze

- Статус: accepted
- Дата: 2026-03-06

## Решение

- Во время remediation включается change freeze через `HELPER_REMEDIATION_LOCK=1`.
- CI gate `scripts/check_remediation_freeze.ps1` ограничивает изменения remediation allowlist-ом.

## Причина

- Performance/security/startup remediation не должна смешиваться с feature work.

## Последствия

- При активном remediation lock feature work должен идти отдельным потоком.
- После завершения remediation lock может быть снят, но gate остаётся как guardrail.
