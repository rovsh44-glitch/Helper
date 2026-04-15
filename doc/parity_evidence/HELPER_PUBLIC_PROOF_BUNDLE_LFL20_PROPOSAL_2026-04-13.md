# HELPER Public Proof Bundle LFL20 Proposal

Date: `2026-04-13`
Upstream corpus:

- `doc/parity_evidence/LOCAL_FIRST_LIBRARIAN_300_CASE_MATRIX_AND_PROMPTS_2026-03-22.md`

Related rationale:

- `doc/analysis/HELPER_EXTERNAL_TEXT_REVIEW_AND_PUBLIC_PROOF_BUNDLE_2026-04-13.md`

## Goal

Define the first narrow public proof bundle for HELPER without trying to green the full `LFL300` parity track first.

Chosen proof statement:

> On research and analysis tasks that require local-first reasoning, explicit web escalation, source display, uncertainty honesty, and operator-auditable conclusions, HELPER produces a more traceable workflow than a plain assistant-only baseline.

This proposal is not:

- a human-parity claim
- a full `LFL300` closure request
- a claim of complete autonomous product proof

## Why `LFL20` Instead Of Full `LFL300`

`LFL300` is the right upstream corpus, but it is too broad for the first public proof artifact.

Reasons:

1. `300` cases across `15` domains dilutes the first public message.
2. The first public proof must be interpretable by a cold external reader in one sitting.
3. The parity layer is still incomplete today:
   - `CURRENT_PARITY_EVIDENCE_BUNDLE.md` = `INCOMPLETE`
   - `CURRENT_PARITY_EVIDENCE_STATE.md` = `INSUFFICIENT_EVIDENCE`
4. A narrower bundle can still be fully derived from the existing canonical corpus and analyzer path.

So the right order is:

1. freeze a narrow public `LFL20` subset
2. run it reproducibly and publish the result
3. expand later to larger product-quality or parity tracks

## Selection Rules

The first public bundle should use `20` cases drawn from four existing LFL300 slice families:

- `local_only_strength`
- `paper_analysis`
- `regulation_freshness`
- `sparse_evidence`

The bundle intentionally does **not** start with `medical_conflict`, because:

- it is high-stakes
- it is harder to judge publicly without domain-review overhead
- it risks shifting the first proof from evidence-discipline to medical safety debate

The bundle implicitly preserves `multilingual_local_first`, because the chosen prompts remain Russian-first, just like the upstream corpus.

## The 20 Cases

### Slice A. `local_only_strength`

Purpose:

- prove that HELPER can answer strongly without unnecessary web escalation when the local knowledge path should already be enough

Selected cases:

1. `LFWR-061`
   - `explain_and_structure`
   - Prompt: `Объясни разницу между корреляцией и причинностью на простых примерах.`
   - Why: crisp baseline concept explanation; easy to judge for clarity and non-hallucinatory structure.

2. `LFWR-071`
   - `plan_actions`
   - Prompt: `Составь план критического чтения научной статьи для начинающего исследователя.`
   - Why: tests structured reasoning and operator-useful planning without requiring current web lookup.

3. `LFWR-086`
   - `compare_and_choose`
   - Prompt: `Сравни REST и GraphQL для внутреннего бизнес-приложения среднего размера.`
   - Why: practical architecture comparison with a strong local-first answer path.

4. `LFWR-096`
   - `review_diagnose_or_critique`
   - Prompt: `Оцени эту идею архитектуры: одна общая база, один общий репозиторий, но 14 отдельных "микросервисов" в деплое.`
   - Why: operator-grade critique task; good fit for visible reasoning quality.

5. `LFWR-136`
   - `review_diagnose_or_critique`
   - Prompt: `Оцени мой набор KPI: только revenue, без retention, margin и customer support metrics.`
   - Why: tests structured business critique and actionable conclusions without web dependence.

### Slice B. `paper_analysis`

Purpose:

- prove that HELPER can work with research-process questions, literature workflows, paper status, and evidence reconciliation

Selected cases:

6. `LFWR-062`
   - `explain_and_structure`
   - Prompt: `Объясни, как устроен peer review, а затем проверь, как в последние годы меняются open-review практики.`
   - Why: combines stable baseline knowledge with current research-process updates.

7. `LFWR-067`
   - `compare_and_choose`
   - Prompt: `Сравни arXiv preprints и peer-reviewed journal papers, а затем проверь текущие практики издателей и репозиториев.`
   - Why: tests evidence hierarchy and source-quality reasoning.

8. `LFWR-072`
   - `plan_actions`
   - Prompt: `Составь план небольшого literature review по тепловым насосам и дополни его свежими исследованиями об эффективности.`
   - Why: tests literature-review planning plus current-source integration.

9. `LFWR-078`
   - `review_diagnose_or_critique`
   - Prompt: `Проверь, была ли статья, на которую я ссылаюсь, отозвана, исправлена или сильно оспорена по состоянию на сегодня.`
   - Why: excellent cold-reader proof of traceability and source-state verification.

10. `LFWR-079`
   - `review_diagnose_or_critique`
   - Prompt: `Разбери, почему источники расходятся по оценкам climate sensitivity и какой вывод при этом остаётся безопасным.`
   - Why: tests contradiction handling and safe conclusion under scientific disagreement.

### Slice C. `regulation_freshness`

Purpose:

- prove that HELPER knows when web freshness is mandatory and can show current-source-backed conclusions instead of stale confident summaries

Selected cases:

11. `LFWR-153`
   - `plan_actions`
   - Prompt: `Составь актуальный filing checklist для remote worker, который выставляет инвойсы иностранным клиентам из Узбекистана в 2026 году.`
   - Why: tests current compliance planning with real freshness pressure.

12. `LFWR-158`
   - `review_diagnose_or_critique`
   - Prompt: `Проверь, актуальны ли налоговые thresholds и reporting deadlines, которыми я пользуюсь сегодня.`
   - Why: strong auditability case; easy to judge whether freshness really happened.

13. `LFWR-163`
   - `explain_and_structure`
   - Prompt: `Объясни, что последние изменения в регулировании ИИ в ЕС означают сегодня для маленького software vendor.`
   - Why: directly relevant to current AI governance and source-backed policy explanation.

14. `LFWR-168`
   - `compare_and_choose`
   - Prompt: `Сравни актуальные визовые пути для software engineer, который хочет переехать в Германию в 2026 году.`
   - Why: good current-rules comparison case with obvious freshness requirements.

15. `LFWR-178`
   - `review_diagnose_or_critique`
   - Prompt: `Проверь, актуальны ли import restrictions и customs rules для ввоза дрона в ЕС на сегодня.`
   - Why: clean regulation-freshness case with direct source-trace expectations.

### Slice D. `sparse_evidence`

Purpose:

- prove that HELPER can stay useful without overstating certainty when evidence is thin, vendor-shaped, unstable, or socially over-claimed

Selected cases:

16. `LFWR-085`
   - `explain_and_structure`
   - Prompt: `Объясни, насколько надёжны текущие claims о fully autonomous AI software engineers.`
   - Why: directly relevant to current AI hype; ideal for uncertainty honesty.

17. `LFWR-090`
   - `compare_and_choose`
   - Prompt: `Сравни vector databases и классический search для маленькой команды, если большая часть benchmark-ов vendor-shaped.`
   - Why: tests balanced reasoning under benchmark bias and marketing pressure.

18. `LFWR-095`
   - `plan_actions`
   - Prompt: `Составь осторожный план внедрения AI coding assistants в регулируемой компании, где claims о продуктивности пока спорные.`
   - Why: strong operator-grade planning task with built-in uncertainty requirements.

19. `LFWR-100`
   - `review_diagnose_or_critique`
   - Prompt: `Оцени утверждение, что переход на Rust автоматически снимает большую часть security risk.`
   - Why: tests refusal to turn a real advantage into an exaggerated absolute claim.

20. `LFWR-125`
   - `explain_and_structure`
   - Prompt: `Объясни, насколько надёжны claims о том, что четырёхдневная рабочая неделя улучшает output практически в любой отрасли.`
   - Why: broadens the bundle beyond software-only claims and tests evidence quality in management discourse.

## Coverage Summary

This `LFL20` bundle preserves:

- `4` slice families
- all `4` task types
- both local-sufficient and web-mandatory modes
- currentness-sensitive tasks
- conflict-sensitive tasks
- uncertainty-sensitive tasks
- multilingual local-first behavior

Distribution:

- `5` cases from `local_only_strength`
- `5` cases from `paper_analysis`
- `5` cases from `regulation_freshness`
- `5` cases from `sparse_evidence`

Task-type distribution:

- `6` explain-and-structure
- `4` compare-and-choose
- `5` plan-actions
- `5` review-diagnose-or-critique

## Metrics

The bundle should be scored in two layers:

1. `automatic analyzer metrics`
2. `published reviewer rubric metrics`

### A. Automatic Analyzer Metrics

These should be computed from the existing LFL runner/analyzer path:

- `scripts/run_local_first_librarian_corpus.ps1`
- `scripts/analyze_local_first_librarian_run.ps1`

#### 1. Runtime OK rate

- Definition: `okCases / totalCases`
- Source: analyzer summary
- Target: `20/20`
- Hard fail: any `runtime_error`

#### 2. Issue-free case rate

- Definition: cases with `0` analyzer issues
- Source: per-case `issues`
- Target: `>= 17/20`
- Hard fail: any case missing one of:
  - `Sources`
  - `Analysis`
  - `Conclusion`
  - `Opinion`

#### 3. Mandatory-web source floor pass rate

- Definition: mandatory-web cases with:
  - no `web_sources_below_minimum`
  - no `no_sources_in_mandatory_web_case`
- Scope: cases `LFWR-062`, `067`, `072`, `078`, `079`, `153`, `158`, `163`, `168`, `178`
- Target: `10/10`

#### 4. Mandatory-web citation coverage

- Definition: average `citationCoverage` on mandatory-web cases
- Source: analyzer
- Target:
  - mean `>= 0.70`
  - no single mandatory-web case below `0.50`

#### 5. Unsupported assertion rate

- Definition: analyzer `unsupportedAssertionRate`
- Target: `0.00`
- Hard fail: any unsupported-assertion case in the bundle

#### 6. Grounded-without-passage-evidence count

- Definition: count of `grounded_without_passage_evidence`
- Target: `0`

#### 7. Fetch/browser recovery unresolved count

- Definition: count of `fetch_failure_despite_relevant_search_hits` plus `browser_or_fetch_recovery_unresolved`
- Target: `0`

#### 8. Sparse-evidence uncertainty pass rate

- Definition: sparse-evidence cases with no `missing_explicit_uncertainty_for_sparse_case`
- Scope: cases `LFWR-085`, `090`, `095`, `100`, `125`
- Target: `5/5`

#### 9. Weak grounding status count

- Definition: cases carrying `weak_grounding_status`
- Target: `0` in mandatory-web cases
- Soft limit: `<= 1` across the whole bundle, with explicit disclosure if present

### B. Published Reviewer Rubric

Automatic metrics are not enough for a public proof bundle.

Each case should also receive a human-readable reviewer score out of `10`.

#### Reviewer rubric per case

1. `Section contract adherence` — `0..1`
   - Are the required sections present and used as sections, not decorative labels?

2. `Source traceability` — `0..2`
   - Can a reviewer clearly see what sources were used and what each source supports?

3. `Local vs web role clarity` — `0..2`
   - Is it clear what came from local baseline reasoning and what came from current external verification?

4. `Analysis and reconciliation quality` — `0..2`
   - Does the answer compare, reconcile, or boundary-mark evidence instead of just stacking facts?

5. `Uncertainty honesty` — `0..2`
   - Does the answer stay useful without pretending certainty when the evidence is weak, conflicting, or unstable?

6. `Conclusion and opinion separation` — `0..1`
   - Is the bottom-line conclusion distinct from the labeled opinion/recommendation?

#### Reviewer score targets

- bundle average: `>= 8.0 / 10`
- no case below: `6.0 / 10`
- `regulation_freshness` slice average: `>= 8.5 / 10`
- `sparse_evidence` slice average: `>= 8.5 / 10`

## Bundle Pass Rule

The first public proof bundle should be considered successful only if all of the following are true:

1. automatic hard-fail metrics are all green
2. automatic soft metrics meet target thresholds
3. reviewer average is `>= 8.0 / 10`
4. at least `3` worst cases are published with their failure analysis, not hidden

If the bundle passes only because the narrative explains away weak cases, it should be marked `not claim-ready`.

## Execution Path

Recommended execution path:

1. Materialize the full corpus:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build_local_first_librarian_corpus.ps1
```

2. Extract the `20` frozen cases above into a dedicated subset file, for example:

```text
eval\web_research_parity\proof_bundle_lfl20_v1.jsonl
```

3. Run the subset through the real runner:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\run_local_first_librarian_corpus.ps1 `
  -CorpusPath eval\web_research_parity\proof_bundle_lfl20_v1.jsonl `
  -ValidationMode browser_enabled `
  -MaxCases 20
```

4. Analyze the run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\analyze_local_first_librarian_run.ps1 `
  -ResultsJsonlPath <path-to-results.jsonl>
```

5. Publish:

- frozen task list
- raw results
- analyzer summary
- reviewer scores
- worst-case failure notes

## Final Recommendation

Do **not** make "green the whole `LFL300` corpus" the entry ticket for the first public proof.

Do this instead:

1. freeze this `LFL20`
2. run it reproducibly
3. publish it as the first hard public proof artifact
4. expand later from `20 -> 40 -> 300`

That sequence keeps the proof narrow, interpretable, and honest while still staying fully rooted in the canonical `LFL300` benchmark design.
