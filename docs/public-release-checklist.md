# Public Release Checklist

Use this checklist before and after a public showcase sync.

## Pre-Push

- the change is prepared on a topic branch, not as a planned direct push to protected `main`
- `FAQ.md` does not point to private-only `doc/` paths
- `FAQ.md` does not describe already-published issue forms as future work
- `docs/ip-and-ownership.md` matches the public repository identity
- `README.md`, `one-pager.md`, and `executive-summary.md` agree on honest-status wording
- top-level public docs do not contain `Gemini.Api` or `Gemini.Genesis`
- any status jargon used in top-level docs is either defined in `docs/status-definitions.md` or removed
- `runtime-review-slice/` exists and is linked from `README.md`
- `runtime-review-slice/package-lock.json` exists and matches the current public `package.json`
- `docs/runtime-review-slice-architecture.md` exists and is linked from `README.md`, `docs/README.md`, and `runtime-review-slice/README.md`
- `docs/runtime-review-slice-verification.md` exists and is linked from `README.md`, `docs/README.md`, and `runtime-review-slice/README.md`
- `docs/runtime-review-slice-redaction-workflow.md` exists and is linked from `README.md`, `docs/README.md`, and `runtime-review-slice/README.md`
- `generated-artifact-validation-slice/` exists and is linked from `README.md`
- `docs/generated-artifact-validation-slice-architecture.md` exists and is linked from `README.md`, `docs/README.md`, and `generated-artifact-validation-slice/README.md`
- `docs/generated-artifact-validation-slice-verification.md` exists and is linked from `README.md`, `docs/README.md`, and `generated-artifact-validation-slice/README.md`
- `docs/generated-artifact-validation-slice-comparison.md` exists and is linked from `README.md`, `docs/README.md`, and `generated-artifact-validation-slice/README.md`
- `helper-generation-contracts/` exists and is linked from `README.md` and `docs/README.md`
- `docs/helper-generation-contracts-dependency-map.md` exists and is linked from `README.md`, `docs/README.md`, and `helper-generation-contracts/README.md`
- `docs/helper-generation-contracts-compatibility.md` exists and is linked from `README.md`, `docs/README.md`, and `helper-generation-contracts/README.md`
- `README.md` and `docs/README.md` still expose the canonical Stage 2 test path, Stage 2 sample-validation path, and Stage 3 test path
- `runtime-review-slice/sample_data/` and `sample_data/logs/` remain sanitized: no non-redacted Windows paths, no token-like material, no non-local URLs
- `runtime-review-slice/scripts/validate-sample-data.ps1` passes against the checked-in `sample_data/` tree
- `runtime-review-slice/scripts/*.ps1` use slice-root-relative paths and local loopback hosts only
- `runtime-review-slice/scripts/test.ps1` restores locked frontend dependencies, checks fixture presence, runs the sample-data validation gate, and can run on a clean machine without private context
- `generated-artifact-validation-slice/sample_fixtures/` remains public-safe: no private paths, no operator identity, no token-like material, no provider URLs
- `generated-artifact-validation-slice/scripts/*.ps1` use slice-root-relative paths and public .NET toolchain commands only
- `generated-artifact-validation-slice/scripts/test.ps1` restores public dependencies, runs the xUnit suite, and runs the checked-in sample-validation sweep
- `helper-generation-contracts/scripts/test.ps1` restores public dependencies and runs the shared contract test suite
- `.github/workflows/public-proof-paths.yml` exists
- `.github/workflows/public-proof-paths.yml` still runs the canonical Stage 1, Stage 2, and Stage 3 commands documented in `README.md` and `docs/README.md`
- `.github/workflows/public-proof-paths.yml` still uses `windows-latest`, `.NET 9`, and `Node.js 22` only where the public proof paths need them
- `.github/workflows/public-proof-paths.yml` does not introduce secrets, private registries, private submodules, or private-core-only steps
- `main` branch protection still enforces admins, requires one approving pull-request review, and keeps the three `Public Proof Paths` jobs as required checks
- any screenshot or artifact claim stays within the boundary described by `docs/public-proof-boundary.md`

## Post-Push

- the live GitHub repo root shows the expected files and directories
- the live `README.md` reflects the latest intended links and wording
- changed raw GitHub docs render the new content
- the live GitHub repo shows `.github/workflows/public-proof-paths.yml`
- the merge happened through a PR rather than a direct push to protected `main`
- if the push changed public proof-path code or workflow wiring, the GitHub Actions tab shows a `Public Proof Paths` run for the pushed commit
- the public repo still communicates an honest boundary between showcase and private core

## GitHub Render Caveat

Do not close a public sync only because local `main` matches `origin/main`.

GitHub repo-page rendering can lag behind pushed raw content for a short period. If the repo main page still looks stale:

- verify the raw `README.md`
- verify the raw changed docs
- verify the repo root again after refresh

If raw content is updated but the main page is temporarily stale, treat that as a render/cache lag and keep the sync open until the live page catches up.
