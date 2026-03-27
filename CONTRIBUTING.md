# Contributing

This repository is currently maintainer-curated. Contributions are welcome, but alignment matters more than volume.

## Good First Contribution Types

- documentation fixes
- issue reproduction notes
- test-gap reports
- small safety or hygiene improvements
- evaluation corpus and reviewer-process suggestions

## Before You Start

- open an issue or discussion for large changes first
- do not start a large feature branch without alignment
- keep claims honest and source-backed
- avoid adding machine-local paths, secrets, or runtime data

## Pull Request Expectations

- scope the change tightly
- explain the user-visible or operational impact
- add tests when behavior changes
- do not mix unrelated cleanup into the same change
- do not introduce undocumented public claims

## Protected Main

- `main` is a protected branch and should be treated as a PR-only merge target
- do not plan work around direct pushes to `main`
- expect `Public Proof Paths` to run on every PR and on the merge commit
- `main` currently requires green hosted checks plus one approving review before merge
- stale approvals are dismissed when the PR changes, so keep rebases and force-pushes intentional

## What To Avoid

- committing secrets or local env files
- publishing sensitive reviewer or blind-eval material
- adding generated noise or transient artifacts
- treating dated analysis docs as canonical truth without linking the active source of truth

## Review Model

- review is maintainer-driven
- merge timing is not guaranteed
- large strategic changes may be declined even if technically sound
