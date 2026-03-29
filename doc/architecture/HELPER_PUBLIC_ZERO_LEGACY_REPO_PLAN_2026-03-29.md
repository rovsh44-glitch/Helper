# Public Tree Policy

Status: `active policy`
Updated: `2026-03-29`

## Goal

Prepare a public-safe HELPER repository tree where blocked historical product labels do not appear in tracked filenames, tracked file content, public docs, public configs, or operator-facing runtime surfaces.

## Public Tree Rules

1. Public API routes use only helper-first paths.
2. Public hubs use only helper-first paths.
3. Public tool and audit identifiers use only helper-first names.
4. Public docs keep stable architecture and operator guidance only.
5. Historical evidence packs, dated remediation notes, and private comparative materials stay outside the public tree.
6. Public configs and templates avoid blocked historical labels, including third-party config aliases.

## Required Checks

1. filename scan over tracked public files returns zero blocked hits;
2. content scan over tracked public files returns zero blocked hits;
3. build and tests pass on the sanitized tree;
4. docs entrypoint checks pass on the sanitized tree;
5. the public branch is created only from the sanitized tree.

## Public Tree Scope

Keep:

1. active source code;
2. active tests;
3. canonical docs and READMEs;
4. helper-first operator scripts;
5. public-safe config and deployment files.

Remove or privatize:

1. dated remediation narratives;
2. historical certification archives;
3. comparative or blinded evaluation packs;
4. machine-local logs, snapshots, and result bundles;
5. any file that exists only to explain prior naming drift.

## Definition Of Done

1. public working tree scans are clean;
2. helper-only runtime routes and identifiers are enforced by tests;
3. zero-token scan passes;
4. the tree is ready to seed a fresh public history.
