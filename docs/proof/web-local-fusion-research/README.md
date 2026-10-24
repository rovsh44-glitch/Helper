# web-local-fusion-research

Chain: analytical research over fused local library + live web, with an explicit opinion section

Overall: **PASS**

| Invariant | Verdict |
|---|---|
| sources_total>=8 | PASS |
| web_and_local_layers | PASS |
| grounding_not_degraded | PASS |
| opinion_section_present | PASS |
| no_cjk_leak | PASS |

Files: scenario.json (invariants), input.json (exact prompt), observed-output.json (answer + metrics), trace.json (runtime events), checksums.txt.

## Provenance note (2026-10-24)

This bundle is a frozen August 2026 capture. Its `trace.json` is `{"events": null}` because the August
harness did not persist runtime events for this scenario; traces are never reconstructed after the fact.
The capture also predates the render-stub-sweep release, so rendering defects visible in the observed
output (template placeholder fragments) are historical: the current production build strips that class.
A regenerated capture will replace this bundle only when the scenario reproduces on the current core with
full event traces; until then it documents the August behaviour, including its defects.
