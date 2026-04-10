# HELPER_PUBLIC_PROJECT_SCOPE_CONTINUITY_FIX_PLAN_2026-04-10

Status: completed
Owner: Helper maintenance
Scope: public `rovsh44-glitch/Helper` main
Date: 2026-04-10

## Goal

Close the remaining project-scope and continuity defects in public `Helper` so that:

- switching `projectId` from the Settings workflow does not carry stale metadata into a new project
- queued follow-through is isolated by project and branch instead of the whole conversation
- proactive topics are isolated by project instead of being globally deduplicated by text
- the Settings `Project Context` panel reflects persisted project scope rather than unsaved draft input

## Current Defects

### 1. UI project switch still carries stale metadata

Current behavior:

- `hooks/useSettingsViewState.ts` `saveProjectContext` always sends `projectLabel`, `projectInstructions`, and `projectMemoryEnabled` together with `projectId`
- `components/settings/SettingsProjectContextPanel.tsx` binds those fields directly to the currently loaded project snapshot
- `src/Helper.Api/Conversation/UserProfileService.cs` correctly resets context only if those metadata fields are not supplied

Impact:

- changing only `projectId` in the UI can still move old label, instructions, and memory boundary into the new project

### 2. Queued follow-through is conversation-global

Current behavior:

- `src/Helper.Api/Conversation/FollowThroughScheduler.cs` blocks any new queued task if any queued task already exists
- tasks already carry `ProjectId` and `BranchId`

Impact:

- one queued task in project `A` blocks a separate queued task for project `B` or another branch

### 3. Proactive topics are deduplicated globally by text

Current behavior:

- `src/Helper.Api/Conversation/FollowThroughScheduler.cs` prevents registration if an enabled topic with the same text already exists anywhere in the conversation
- UI and client filtering already present topics as project-scoped

Impact:

- the same topic text cannot exist independently in two projects in the same conversation

### 4. Settings panel uses draft project id as active scope

Current behavior:

- `hooks/useSettingsViewState.ts` filters continuity collections using the current editable `projectId` input
- backend snapshot remains on the persisted project until save succeeds

Impact:

- the panel can show tasks and topics for an unsaved draft project id, creating a misleading state before save

## PR Plan

### PR 1

Title:

`Make project switching honest in the Settings workflow`

Required changes:

- split persisted project scope from editable draft fields in `hooks/useSettingsViewState.ts`
- introduce an explicit persisted project id state such as `activeProjectScopeId`
- make continuity filtering depend on the persisted scope id, not the draft input
- change `saveProjectContext` so a plain project switch can send only `projectId`
- only send `projectLabel`, `projectInstructions`, and `projectMemoryEnabled` when they are intentionally edited or intentionally cleared
- preserve explicit clear semantics with `null`

Files:

- `hooks/useSettingsViewState.ts`
- `components/settings/SettingsProjectContextPanel.tsx`
- `components/views/SettingsView.tsx` if props need adjustment
- `test/Helper.Runtime.Tests/ConversationPreferencesRoundTripTests.cs`
- `test/Helper.Runtime.Tests/ConversationProjectContextUiArchitectureTests.cs`

Required tests:

- a UI-like save payload with only new `projectId` does not carry old metadata
- editing draft `projectId` before save does not change continuity filtering
- explicit `null` still clears project metadata when intended

Exit criteria:

- switching projects from the UI no longer leaks stale label, instructions, or memory-boundary state

### PR 2

Title:

`Scope queued follow-through tasks by project and branch`

Required changes:

- replace the global queued-task guard in `src/Helper.Api/Conversation/FollowThroughScheduler.cs`
- uniqueness must be evaluated by:
  - `Status == queued`
  - same effective `ProjectId`
  - same effective `BranchId`
- keep sensible fallback behavior for legacy tasks missing one or both scope fields

Files:

- `src/Helper.Api/Conversation/FollowThroughScheduler.cs`
- `src/Helper.Api/Conversation/BackgroundTaskContracts.cs` only if helpers or comments are needed
- `test/Helper.Runtime.Tests/ConversationFollowThroughTests.cs`
- `test/Helper.Runtime.Tests/ConversationFollowThroughBranchAffinityTests.cs`

Required tests:

- queued task in `project-a/main` does not block `project-b/main`
- queued task in `project-a/main` does not block `project-a/branch-alt`
- duplicate queued task in the same project and branch is still blocked

Exit criteria:

- follow-through queueing is isolated by project and branch within one conversation

### PR 3

Title:

`Make proactive topic registration project-aware`

Required changes:

- update proactive topic dedupe in `src/Helper.Api/Conversation/FollowThroughScheduler.cs`
- dedupe by:
  - same normalized topic text
  - same effective `ProjectId`
  - enabled topic
- retain conversation-wide behavior only when both scopes are empty

Files:

- `src/Helper.Api/Conversation/FollowThroughScheduler.cs`
- `test/Helper.Runtime.Tests/ConversationFollowThroughTests.cs`

Required tests:

- same topic text can exist once in `project-a` and once in `project-b`
- duplicate topic in the same project is still prevented
- unscoped conversation-wide duplicate behavior remains stable

Exit criteria:

- proactive topic registration semantics match the project-scoped UI model

### PR 4

Title:

`Add guardrails for project-scoped continuity semantics`

Required changes:

- extend architecture and regression tests so the three defects above cannot silently return
- verify filtering depends on persisted scope rather than draft input
- verify queue uniqueness is scoped to project and branch
- verify topic dedupe is scoped to project

Files:

- `test/Helper.Runtime.Tests/ConversationProjectContextUiArchitectureTests.cs`
- `test/Helper.Runtime.Tests/ConversationFollowThroughTests.cs`
- `test/Helper.Runtime.Tests/ConversationFollowThroughBranchAffinityTests.cs`
- add a dedicated regression test file if that yields a cleaner split

Required tests:

- source-level or behavior-level regression for persisted-vs-draft project scope
- scheduler regression for scoped queued task uniqueness
- scheduler regression for scoped proactive topic uniqueness

Exit criteria:

- the public repo has automated coverage for all newly fixed project-scope semantics

## Execution Order

1. PR 1
2. PR 2
3. PR 3
4. PR 4

## Verification Matrix

After each PR:

- `dotnet build Helper.sln -c Debug -m:1`
- `./scripts/run_fast_tests.ps1 -Configuration Debug -NoRestore`
- `npm run build`

After PR 4:

- `./scripts/run_integration_tests.ps1 -Configuration Debug -NoRestore`

## Definition Of Done

This plan is complete only when all conditions below are true:

- changing `projectId` from the Settings workflow does not leak old project metadata
- unsaved draft project ids do not affect visible continuity scope
- queued background follow-through is isolated by project and branch
- proactive topics are isolated by project
- fast and integration lanes remain green
- this document is updated from `active plan` to `completed`

## Completion Note

Completed on 2026-04-10.

Delivered:

- honest Settings project switching with persisted-vs-draft scope separation
- project-scoped continuity filtering based on saved scope rather than unsaved draft input
- project-and-branch-scoped queued follow-through uniqueness
- project-aware proactive topic deduplication
- regression coverage for endpoint payload switching, UI scope guardrails, and scheduler semantics

Verification:

- `dotnet build Helper.sln -c Debug -m:1`
- `npm run build`
- `./scripts/run_fast_tests.ps1 -Configuration Debug -NoRestore`
- `./scripts/run_integration_tests.ps1 -Configuration Debug -NoRestore`
