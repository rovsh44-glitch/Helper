# Public Release Checklist

Use this checklist before and after a public showcase sync.

## Pre-Push

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
- `generated-artifact-validation-slice/` exists and is linked from `README.md`
- `docs/generated-artifact-validation-slice-architecture.md` exists and is linked from `README.md`, `docs/README.md`, and `generated-artifact-validation-slice/README.md`
- `docs/generated-artifact-validation-slice-verification.md` exists and is linked from `README.md`, `docs/README.md`, and `generated-artifact-validation-slice/README.md`
- `docs/generated-artifact-validation-slice-comparison.md` exists and is linked from `README.md`, `docs/README.md`, and `generated-artifact-validation-slice/README.md`
- `README.md` and `docs/README.md` still expose the canonical Stage 2 test path and sample-validation path
- `runtime-review-slice/sample_data/` and `sample_data/logs/` remain sanitized: no non-redacted Windows paths, no token-like material, no non-local URLs
- `runtime-review-slice/scripts/*.ps1` use slice-root-relative paths and local loopback hosts only
- `runtime-review-slice/scripts/test.ps1` restores locked frontend dependencies, checks fixture presence, and can run on a clean machine without private context
- `generated-artifact-validation-slice/sample_fixtures/` remains public-safe: no private paths, no operator identity, no token-like material, no provider URLs
- `generated-artifact-validation-slice/scripts/*.ps1` use slice-root-relative paths and public .NET toolchain commands only
- `generated-artifact-validation-slice/scripts/test.ps1` restores public dependencies, runs the xUnit suite, and runs the checked-in sample-validation sweep
- any screenshot or artifact claim stays within the boundary described by `docs/public-proof-boundary.md`

## Post-Push

- the live GitHub repo root shows the expected files and directories
- the live `README.md` reflects the latest intended links and wording
- changed raw GitHub docs render the new content
- the public repo still communicates an honest boundary between showcase and private core

## GitHub Render Caveat

Do not close a public sync only because local `main` matches `origin/main`.

GitHub repo-page rendering can lag behind pushed raw content for a short period. If the repo main page still looks stale:

- verify the raw `README.md`
- verify the raw changed docs
- verify the repo root again after refresh

If raw content is updated but the main page is temporarily stale, treat that as a render/cache lag and keep the sync open until the live page catches up.
