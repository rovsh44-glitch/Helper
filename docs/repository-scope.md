# Repository Scope

## Public Showcase

This repository is the public showcase lane for HELPER.

It should contain only:

- public-safe docs
- media
- trust and contact files
- issue intake forms
- links to sanitized proof exports

## Private Core

The implementation source of truth lives outside this public default branch.

That private core includes:

- runtime code
- tests
- operator scripts
- evaluation corpora
- internal analysis and release tooling

## Structural Transparency Tier (2026-08-19)

The public topology page (`docs/topology/`, GitHub Pages) publishes the repository STRUCTURE of the private core so that
third parties can verify architecture claims without any source code leaving the boundary:

- file names, sizes and modules for code, tests, operator scripts, frontend, CI and deploy configuration, root files
- the project-reference graph and the file-level import graph (static references), fan-in/fan-out, layering and cycle checks
- a structural test -> production map (which test files reference which production files; not coverage)
- the third-party package manifest (NuGet / npm names and versions)

What stays anonymous or out: file CONTENTS (always), names under `doc/`, `docs/` (internal analysis and engineering
history), `eval/` (corpora), media and archives, env/key/secret-store file names (denylist), machine paths, runtime
metrics snapshots. The page itself states what it proves and what it does not (runtime behaviour, security boundaries,
the generation/validation/repair loop and parity claims are not provable from structure).

The build is automated from the private core (CI) behind a privacy gate that enforces the list above.

## Public Proof Repos

Proof bundles are published separately when they are:

1. frozen on a specific run
2. sanitized
3. checksummed
4. accompanied by a manifest and release notes

Current public proof repo:

- `https://github.com/rovsh44-glitch/helper-web-reliability-50-proof`

## Boundary Rule

If a change makes this repository look like a runnable mirror of the private core again, that change is out of scope for the public showcase default branch. Structure (names, sizes, dependency graphs, package manifest) is in scope; content is not.
