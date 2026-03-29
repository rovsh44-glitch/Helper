# Real-Model Eval Requirements

`real-model eval` может считаться authoritative parity evidence только если одновременно выполняется всё ниже.

## Mandatory

1. Режим `live`, а не `dry-run`.
2. Effective `evidence level = authoritative`.
3. Dataset validation = `PASS`.
4. API readiness = `PASS`.
5. API health preflight = `PASS`.
6. Knowledge preflight = `PASS` или `WAIVED` по явному operator decision.
7. Runtime errors = `0`.
8. Pass rate `>= 85%`.

## Required Artifacts

1. Main eval report.
2. Runtime error taxonomy sidecar.
3. Audit trace sidecar.
4. Authoritative gate summary:
   - `*.authoritative.json`
   - `*.authoritative.md`

## Hard Rules

1. `dry_run` никогда не может быть parity proof.
2. Если effective evidence level не `authoritative`, отчёт не считается authoritative даже при хорошем pass rate.
3. `SkipKnowledgePreflight` допустим только при явном waiver path.
4. Отсутствие main report или authoritative summary автоматически означает `non-authoritative`.
