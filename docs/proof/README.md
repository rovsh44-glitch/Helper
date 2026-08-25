# Behavior-proof bundles

Reproducible evidence packages for HELPER's key runtime chains — the behavioral counterpart to the structural
[topology page](../topology/index.html). Each bundle was captured live against a production build via
`POST /api/chat` and carries:

| File | Contents |
|---|---|
| `scenario.json` | the chain under proof, machine-checkable invariants and their observed verdicts |
| `input.json` | the exact request body |
| `observed-output.json` | the full answer, grounding status, citation coverage, source list |
| `trace.json` | the runtime searchTrace events of the turn |
| `checksums.txt` | SHA256 of every file above |

Machine paths are sanitized to `<workspace>` / `<data-root>`; nothing else is altered — answers and traces are verbatim.

## Bundles

| Chain | What it proves |
|---|---|
| [`web-local-fusion-research`](web-local-fusion-research/README.md) | analytical research fuses the local library with live web sources in one citation space, and states an explicit opinion when asked |
| [`local-extraction-source-fidelity`](local-extraction-source-fidelity/README.md) | extraction pinned to a NAMED local tome delivers the article itself (lead + full excerpt) — fabricated attribution is gate-blocked |
| [`web-extraction-explicit-url`](web-extraction-explicit-url/README.md) | an explicit external URL routes to a bounded target read, not an analysis-template answer |
| [`comparative-research-deterministic-keep`](comparative-research-deterministic-keep/README.md) | when the LLM draft fails quality gates, the deterministic source narration is delivered instead of a content-free refusal |
| [`honest-degradation`](honest-degradation/README.md) | when retrieval cannot support a request the turn reports degraded grounding instead of fabricating |

To reproduce: run the bundle's `input.json` against a HELPER instance and re-check the invariants in `scenario.json`
against your response and trace. Failure cases are part of the point — `honest-degradation` documents a deliberate
refusal-over-fabrication contract.
