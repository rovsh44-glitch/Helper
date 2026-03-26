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
- any screenshot or artifact claim stays within the boundary described by `docs/public-proof-boundary.md`

## Post-Push

- the live GitHub repo root shows the expected files and directories
- the live `README.md` reflects the latest intended links and wording
- changed raw GitHub docs render the new content
- the public repo still communicates an honest boundary between showcase and private core
