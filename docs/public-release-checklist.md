# Public Release Checklist

Use this checklist before merging changes into the public showcase default branch.

## Surface Contract

- the branch stays showcase-only: no `src/`, `test/`, `scripts/`, `eval/`, or internal `doc/`
- no code-bearing build files are reintroduced at repo root
- `README.md` still describes the repository as public showcase only
- `docs/repository-scope.md` still explains the split between private core, public showcase, and public proof repos

## Public Claims

- top-level docs do not claim human-level parity
- top-level docs do not imply that the private core is published here
- proof-related claims point to a sanitized public proof repo, not to private paths

## Proof And Links

- the current public proof repo link is still valid
- public docs do not contain broken links to removed `doc/` or runtime paths
- no absolute local filesystem paths appear in public docs

## GitHub Surface

- `.github/ISSUE_TEMPLATE/` remains present and public-safe
- `.github/workflows/repo-gate.yml` still validates the showcase surface contract
- `.github/branch-protection.required-status-checks.json` matches the actual public required checks

## Merge Discipline

- changes land through a PR, not by direct push to `main`
- the public branch stays readable as narrative and proof-entry surface, not as a mirrored engineering repo
