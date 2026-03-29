# Parity Evidence Levels

## Allowed Levels

- `sample`
  Небоевой пример артефакта для демонстрации формата.
- `synthetic`
  Данные выглядят сгенерированными или шаблонными и не могут быть финальным proof.
- `dry_run`
  Пайплайн прогнан без живого model/runtime evidence.
- `live_non_authoritative`
  Артефакт получен на живом runtime, но ещё не удовлетворяет требованиям официального proof.
- `authoritative`
  Артефакт годится как часть официального parity proof.

## Hard Rules

1. `sample`, `synthetic` и `dry_run` никогда не считаются финальным parity proof.
2. `live_non_authoritative` полезен как операционный сигнал, но не как финальное доказательство.
3. `authoritative` допустим только если upstream integrity / coverage / runtime gates пройдены.
