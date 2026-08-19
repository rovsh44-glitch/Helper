# HELPER Public Status - 2026-08-19

Publication classification: PUBLIC-SAFE STATUS NOTE

This note is the public-safe status text for the `rovsh44-glitch/Helper` showcase. It is intentionally narrower than the private engineering state and supersedes the 2026-05-29 note.

## Current Position

Since the May note the private HELPER core has moved in three directions:

1. **Local library as the primary evidence surface.** The local knowledge base now spans ~33 subject domains (science, medicine, humanities, engineering, Russian and English reference works) with on the order of 900k indexed passages. A dedicated Russian reference layer was added (general and subject encyclopedias and dictionaries for biology, medicine, physics, chemistry, mathematics, plus a quality-filtered topical subset of Russian Wikipedia), indexed with an encyclopedia-aware chunker and routed additively into definitional and analytical queries.
2. **Grounded analysis instead of free-form answers.** Analytical requests that name the local library are now routed through local retrieval first (budgeted synthesis, causal chains / competing explanations / calibrated confidence, quote gate, verifier), with observable synthesis outcomes in the turn trace. Local + web evidence fusion, semantic contradiction detection, log-odds confidence calibration and an analytical report composer shipped behind flags with isolated A/B measurement.
3. **Operational observability.** A repository topology page (interactive isometric map of modules, files, dependency edges and flows) ships with the private core and overlays live runtime metrics when opened inside a running instance; the public showcase hosts a sanitized build of the same page (see README).

## What Was Measured (public-safe summary)

All numbers below come from isolated A/B probes on the private core (one flag flipped per run, same queries, same data); they are signals, not certification:

- Russian definitional probes on the new reference layers: grounded answers 5/8 -> 8/8 (chemistry), 6/8 -> 8/8 (mathematics), 12/12 with richer sources (medicine), 7/8 -> 8/8 (physics), with the content of the definitions moving from off-target fragments to the headword articles.
- Analytical report from the local library (4 Russian analytical queries, strict criterion "routed through local search, >=2 sources, >=4 analytical markers, grounded, no template"): 0/4 -> 1/4, with the analytical synthesis reaching the user in 5/8 runs instead of 0/8 and no fabricated citations in the treated arm. The binding limiter after this change is retrieval recall/precision, not synthesis.
- Curated-reference-over-Wikipedia ordering restored curated sources in the two probed cases where a curated headword article exists, while keeping Wikipedia where no curated article exists.

## What Is Not Claimed

- Human-level parity is not proven.
- The 14-day counted parity window is not complete.
- No blind human evaluation result is published.
- Isolated A/B probes are small (8-12 queries per set) and LLM variance between runs is material; they guide engineering, they do not certify quality.

## Public Wording To Use

- "The private HELPER core now treats the local library as the primary evidence surface, including a Russian reference layer."
- "Analytical answers are grounded in local retrieval first and their synthesis outcomes are traceable."
- "Measurements are isolated A/B probes; parity is not claimed."

## Public Wording To Avoid

- "HELPER has proven human-level parity."
- "HELPER's answers are certified correct."
- "The indexed reference texts are published" (they are copyrighted works indexed locally; only aggregate counts are public).

## Disclosure Boundary

Do not publish private paths, raw runtime logs, local-library source text, secrets, private artifacts, prompt-specific smoke logs, or unsanitized internal audit records. The showcase topology page is a sanitized build: module names, file counts, sizes and project-reference edges only; file names are hidden.
