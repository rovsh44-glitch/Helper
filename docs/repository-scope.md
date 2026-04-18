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

## Public Proof Repos

Proof bundles are published separately when they are:

1. frozen on a specific run
2. sanitized
3. checksummed
4. accompanied by a manifest and release notes

Current public proof repo:

- `https://github.com/rovsh44-glitch/helper-web-reliability-50-proof`

## Boundary Rule

If a change makes this repository look like a runnable mirror of the private core again, that change is out of scope for the public showcase default branch.
