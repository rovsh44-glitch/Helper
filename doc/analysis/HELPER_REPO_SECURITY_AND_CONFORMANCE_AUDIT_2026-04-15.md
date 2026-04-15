# Helper Repo Security And Conformance Audit - 2026-04-15

## Scope

This audit compares:

1. local workspace state under the checked-out Helper repository;
2. GitHub repository state for `rovsh44-glitch/Helper`;
3. Helper remediation/proof-bundle expectations after PRs `#38` through `#42`;
4. current dependency-security signals from NuGet and npm.

## Repository Alignment

Observed state:

- Local branch: `main`.
- Local commit: `c2f38df`.
- Remote default branch: `main`.
- Remote `origin/main`: `c2f38df`.
- Local divergence from `origin/main`: `0 0`.
- Working tree before this remediation branch: clean.
- Repository visibility after the operator change: `Private`.

Conclusion:

- Local and remote code identity is aligned.
- The earlier repository-alignment residual described in `HELPER_CURRENT_STATUS_AND_REPO_ALIGNMENT_REPORT_2026-04-15.md` is now obsolete.
- Historical failed Actions runs still exist as audit trail, but the latest `main` runs on `c2f38df` were green before the dependency drift described below.

## CI And Gate Status

Local deterministic gate:

- `npm run ci:gate` passed end-to-end.
- Covered: secret scan, governance, docs, frontend architecture, required status contract, solution coverage, build, fast runtime lane, eval, tool benchmark, OpenAPI, generated client diff, monitoring, frontend build, and bundle budget.

GitHub Actions observed for `c2f38df`:

- `repo-gate`: success.
- `nuget-security-audit`: success.
- push workflow: success.
- dynamic dependency workflow: success.

Important caveat:

- Local `ci:gate` runs NuGet audit in `best-effort-local` mode when the runner has offline/dead-proxy constraints.
- The authoritative NuGet security signal is the connected strict-online lane, not the local degraded/offline lane.

## Dependency Security Findings

### npm

Fresh `npm audit --json` before lockfile remediation found:

- `vite`: high severity advisory in the dev/build chain.
- `rollup`: high severity advisory in the dev/build chain.
- `picomatch`: high/moderate advisories in the dev/build chain.
- `yaml`: moderate advisory in the dev/build chain.

`npm audit --omit=dev --json` returned zero production dependency vulnerabilities.

Interpretation:

- Runtime/prod npm exposure is clean.
- Build/dev dependency exposure is not clean until the lockfile updates from Dependabot PRs `#34` through `#37` are applied.

Remediation path selected:

- Apply the same dependency floor represented by PRs `#34` through `#37` in a consolidated lockfile update from current `main`.
- Re-run `npm audit` after the lockfile update.
- Treat stale Dependabot PRs as superseded if their branches cannot be safely rebased and merged one-by-one from the old base.

Remediation result on this branch:

- `npm audit fix --package-lock-only` updated the lockfile to secure versions.
- Effective resolved versions include `vite 6.4.2`, `rollup 4.60.1`, `picomatch 4.0.4` / `2.3.2`, and `yaml 2.8.3`.
- `npm install` completed with `found 0 vulnerabilities`.
- `npm audit --json` returned `total: 0`.

### NuGet

Fresh strict-online NuGet audit after the previous green GitHub run found:

- `Magick.NET-Q8-AnyCPU 14.12.0` still reports low/moderate advisories.
- `dotnet list package --outdated` did not show a newer `Magick.NET-Q8-AnyCPU` version available from NuGet.

Interpretation:

- PR `#41` correctly fixed the previous `14.11.1` finding at that time.
- The advisory database has moved again or widened, so `14.12.0` is no longer a sufficient long-term security closure.
- Because no newer fixed package is available, the correct fix is not another blind bump. The correct fix is to remove or isolate the dependency.

Remediation path selected:

- Remove `Magick.NET-Q8-AnyCPU` from `Helper.Runtime.Knowledge`.
- Keep PDF text extraction on `PdfPig`.
- Keep PDF vision fallback on Ghostscript-rendered JPEG bytes.
- Degrade DJVU OCR explicitly until a non-vulnerable rasterization path is selected.

Remediation result on this branch:

- `Magick.NET-Q8-AnyCPU` was removed from `Helper.Runtime.Knowledge`.
- PDF vision fallback now uses Ghostscript JPEG output plus raw image-byte validation.
- Embedded PDF images are passed through only when they are already recognized encoded image bytes.
- DJVU OCR now reports an explicit `djvu_ocr_unavailable` warning instead of loading the vulnerable rasterizer.
- Strict-online NuGet audit returned `audit_passed`.

## Private Repo Security Automation

`doc/security/GITHUB_PRIVATE_REPO_SECURITY_AUTOMATION_STATUS_2026-04-12.md` remains directionally correct for the current Private repository posture:

- Dependabot alerts and automated security fixes are the available GitHub-native dependency controls.
- Secret scanning and code scanning are not available for this user-owned Private repository without a different GitHub security tier.
- Repo-owned security gates remain mandatory.

The current platform message is consistent with that document:

- Advanced Security is only available under the required GitHub tier.
- Code scanning is available for public repositories or eligible organization/enterprise repositories.
- Therefore code scanning cannot be treated as an enabled control for this private-core repo today.

## Public Visibility Decision

`doc/security/PUBLIC_VISIBILITY_DECISION_2026-04-11.md` remains the correct policy:

- The private-core repository should stay Private.
- The direct-publication path for the full source tree remains disallowed.
- Public release should use a separate curated showcase/proof repository.

Analysis of the tempting alternative:

- Making the current full `Helper` repository Public only to unlock Code scanning would contradict the private-core boundary.
- GitHub does not support "public repo but hide most code" inside the same repository.
- The correct split is: private-core repo for source/tooling/evidence operations, public showcase repo for reviewed docs and reproducible public proof bundle artifacts.

## Helper Conformance

Conforming areas:

- LFL20 proof bundle path is implemented and documented.
- Local-library evidence fusion is implemented and analyzer-backed.
- Browser/fetch recovery and regulation freshness workstreams are represented in code and tests.
- Main branch protection context contract is declared locally and attached server-side.
- NuGet audit is separated into a connected strict-online lane.
- Local main and remote main now describe the same code.

Residuals before this branch:

- `doc/CURRENT_STATE.md` is stale relative to the 2026-04-15 remediation state.
- `HELPER_CURRENT_STATUS_AND_REPO_ALIGNMENT_REPORT_2026-04-15.md` is stale because alignment has since been completed.
- npm dev/build-chain dependency advisories must be closed.
- NuGet Magick.NET dependency must be removed or isolated.
- Public/private posture must explicitly say: private-core stays Private; public proof bundle goes to a separate curated public repo.

Closure on this branch:

- `doc/CURRENT_STATE.md` now includes a 2026-04-15 addendum.
- `HELPER_CURRENT_STATUS_AND_REPO_ALIGNMENT_REPORT_2026-04-15.md` now records current local/remote alignment.
- npm and NuGet dependency security checks pass.
- Private-core/public-showcase policy was revalidated.
- Full `npm run ci:gate` passed after the remediation.

## Closure After PR 43

Current private-core baseline:

1. `main` is aligned locally and remotely at `8cc34c2`.
2. Baseline tag `helper-private-core-2026-04-15-green` is attached to the current private-core `main`.
3. PR `#43` closed dependency security and private-core posture.
4. Hosted `repo_gate` on `8cc34c2` completed with `success`.
5. Hosted `connected_nuget_audit` on `8cc34c2` completed with `success`.
6. Dependabot jobs on `8cc34c2` completed with `success`.
7. `npm audit --json` on current `main` returns `0` vulnerabilities.
8. Stale Dependabot PRs `#34` through `#37` are closed as superseded by the consolidated lockfile remediation.

Public/private split:

1. The private-core repository remains `Private`.
2. The public proof repository is published at `https://github.com/rovsh44-glitch/helper-proof-bundle-lfl20`.
3. The public repository contains sanitized LFL20 corpus/results/reports/manifest material only, not private-core source code.

## Next Action Plan

1. Keep private-core controls green: `repo_gate`, `connected_nuget_audit`, Dependabot, repo-owned secret/config/docs gates.
2. Treat `https://github.com/rovsh44-glitch/helper-proof-bundle-lfl20` as the public evidence surface for LFL20.
3. Start the model A/B stage only after this fixed baseline, using `helper-private-core-2026-04-15-green` as the comparison anchor.
4. If code scanning becomes mandatory for private-core source, use an eligible GitHub security tier or an external scanner instead of making the full source tree public.
