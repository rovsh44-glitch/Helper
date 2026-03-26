# Public Parity Methodology

This note describes the public methodology HELPER intends to use for any future parity-related claim.

## Current Status

No human-parity claim is being made today.

This document is a public method note, not a completed result packet.

## Why This Exists

The public repository already exposes status language such as:

- `human-level parity: not proven`
- `blind human evaluation: implemented, but the current corpus is still non-authoritative`
- `14-day parity window: not started`

This note explains the intended public evaluation shape behind those terms without publishing the entire private evidence bundle.

## Scope

The methodology is intended for bounded HELPER-relevant operator tasks.

It is not a claim about:

- universal human parity
- leaderboard dominance on unrelated public benchmarks
- unrestricted web-scale autonomous behavior
- every private-core capability

## Target Of Comparison

The public target is a skilled human operator working inside the same bounded task frame.

That means parity is evaluated against:

- the same task brief
- the same allowed source material
- the same time-box rules
- the same output format expectations

It does not mean parity with every human, every role, or every possible task family.

## Task Families

The public parity window is intended to cover task families that match HELPER's product shape:

1. research and answer synthesis
2. strategic planning and structured decision support
3. architecture and implementation reasoning
4. runtime review, log interpretation, and operator triage
5. bounded generation or transformation tasks with reviewable outputs

The exact private task corpus may remain unpublished to reduce gaming and preserve future evaluation integrity.

## Window Design

Any claim-eligible parity run should use a fixed evaluation window, not rolling anecdotes.

Public minimum shape:

1. a pre-frozen `14-day` window
2. a pre-registered task pack before the window starts
3. a minimum of `40` scored tasks across multiple task families
4. no single task family contributing less than `15%` of the scored pack
5. no mid-window rewriting of grading rules

If freeze conditions break, the window should be treated as invalid and restarted rather than counted.

## Blind Review Process

The intended public review process is:

1. produce human and HELPER outputs against the same task brief
2. remove source identity before scoring when feasible
3. score outputs against the same rubric
4. use multiple reviewers when feasible
5. log disqualifying process breaches and unresolved ambiguities

Reviewer identities, raw notes, and some anti-gaming controls may remain private.

## Metrics

The public parity method uses a small number of claim-relevant metrics:

1. task pass rate against pre-defined acceptance criteria
2. reviewer rubric score on correctness, usefulness, clarity, and operator value
3. critical-failure count
4. family-level consistency, not only overall average

The public method is intended to judge whether HELPER stays within a controlled performance band, not whether it produces the most impressive single demo.

## Public Minimum Thresholds For A Claim-Eligible Window

No public parity claim should be made unless all of the following are true:

1. the fixed `14-day` window completed without governance breaks
2. at least `40` scored tasks were completed under the frozen rules
3. overall HELPER pass rate is not worse than the human baseline by more than `5` percentage points
4. no task family underperforms the human baseline by more than `10` percentage points
5. median reviewer-score gap is within `0.3` on a `5-point` scale overall
6. no task family has a median reviewer-score gap worse than `0.5` on a `5-point` scale
7. HELPER critical-failure rate is not worse than the human baseline
8. the evidence bundle is complete enough to support audit of the claimed result

These are public minimum bars. Internal decision bars may be stricter.

## What Stays Private

The public method does not require publishing:

- the full frozen task corpus
- reviewer identities
- anti-gaming controls
- raw evidence bundles
- sensitive runtime material
- internal certification workflow detail beyond what is needed for honest public claims

## Public Output If A Future Window Completes

If a future claim-eligible window completes, the public repo should at minimum publish:

1. the window status
2. task-family coverage
3. scoring summary
4. whether claim thresholds were met
5. what remains private and why

## Relationship To Other Public Docs

Read this together with:

- [status-definitions.md](status-definitions.md)
- [public-proof-boundary.md](public-proof-boundary.md)
- [product-roadmap.md](product-roadmap.md)
