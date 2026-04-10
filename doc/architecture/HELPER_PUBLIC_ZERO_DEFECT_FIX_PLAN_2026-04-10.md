# HELPER Public Zero-Defect Fix Plan

Status: `completed`
Updated: `2026-04-10`
Target repo: `https://github.com/rovsh44-glitch/Helper`
Validated baseline: `ed9ff65`

## Goal

Close the remaining product-level defects in public `Helper` after the earlier remediation work, so the repository is not only green on build/test, but also behaviorally honest and project-affine in the surfaces it exposes.

This plan is narrower than the `2026-04-08` and `2026-04-09` remediation plans. Those plans restored contract honesty, green lanes, and the operational public surface. This plan finishes the remaining correctness gaps that still survive on top of that healthy baseline.

## Current Baseline

At `ed9ff65` the public repository is healthy at build/test level:

1. `dotnet build Helper.sln -c Debug -m:1` passes.
2. `./scripts/run_fast_tests.ps1 -Configuration Debug -NoRestore` passes.
3. The public repo now has working advanced settings, non-voice follow-through, contract guardrails, and hardened `/preferences` parsing.

The remaining issues are behavioral:

1. switching `projectId` can carry stale project metadata into the new project context;
2. follow-through completion is branch-affine but not fully project-affine;
3. Settings `Project Context` renders conversation-wide continuity state while presenting it as project-scoped.

## Findings This Plan Fixes

### 1. Project switch semantics are not clean

Symptoms:

1. switching from project `A` to project `B` without explicitly resending label/instructions can preserve `A` metadata;
2. `ReferenceArtifacts` from the old project can carry forward because the current `with` update does not reset them;
3. the public surface therefore claims a fresh project context while reusing stale project state.

Primary files:

1. `src/Helper.Api/Conversation/UserProfileService.cs`
2. `src/Helper.Api/Conversation/ProjectContextState.cs`
3. `test/Helper.Runtime.Tests/ConversationPreferencesRoundTripTests.cs`

### 2. Follow-through completion is not project-affine

Symptoms:

1. queued background task stores `ProjectId`, but the completion message is rendered from the current `state.ProjectContext`;
2. proactive-topic summary inside the completion message also comes from the current conversation state instead of the originating project;
3. if the active project changes before processing, the completion message can describe the wrong project.

Primary files:

1. `src/Helper.Api/Conversation/BackgroundTaskContracts.cs`
2. `src/Helper.Api/Conversation/FollowThroughScheduler.cs`
3. `src/Helper.Api/Conversation/ConversationFollowThroughProcessor.cs`
4. `test/Helper.Runtime.Tests/ConversationFollowThroughTests.cs`
5. `test/Helper.Runtime.Tests/ConversationFollowThroughBranchAffinityTests.cs`

### 3. Project Context Settings panel is not project-scoped in practice

Symptoms:

1. the panel is labeled as `Project-scoped`;
2. it renders `backgroundTasks` and `proactiveTopics` conversation-wide;
3. after project saves and project switches, the panel can keep showing continuity state from other projects.

Primary files:

1. `components/settings/SettingsProjectContextPanel.tsx`
2. `hooks/useSettingsViewState.ts`
3. `components/views/SettingsView.tsx`
4. `services/conversationApi.ts`
5. `services/generatedApiClient.ts`

## Repair Order

### PR 1. Make project switching create a clean project context

Priority: highest

Reason:

This is the most fundamental correctness defect. If `projectId` switching is not clean, both prompt assembly and settings UI will continue to operate on contaminated state.

Files to change:

1. `src/Helper.Api/Conversation/UserProfileService.cs`
2. `src/Helper.Api/Conversation/ProjectContextState.cs`
3. `test/Helper.Runtime.Tests/ConversationPreferencesRoundTripTests.cs`
4. add a focused regression test file if the existing round-trip test becomes too overloaded

Implementation steps:

1. change the `projectIdProvided && !string.IsNullOrWhiteSpace(dto.ProjectId)` branch in `UserProfileService.ApplyPreferences`;
2. detect whether the incoming `dto.ProjectId` is different from the currently active `state.ProjectContext?.ProjectId`;
3. if the project id is changing, initialize a fresh `ProjectContextState.Empty(newProjectId)` instead of cloning the current object;
4. only then apply optional incoming `ProjectLabel`, `ProjectInstructions`, and `ProjectMemoryEnabled` overrides onto that new empty context;
5. ensure `ReferenceArtifacts` are reset to empty on project switch unless a future explicit import path supplies them;
6. keep current clear semantics:
   if `projectId` is explicitly `null` or blank, clear `state.ProjectContext`;
7. keep current same-project partial-update semantics:
   if `projectId` is unchanged, omission of label/instructions should continue to preserve existing values.

Required tests:

1. switching from project `A` to `B` without resending label/instructions yields:
   - `ProjectId == B`
   - `Label == null`
   - `Instructions == null`
   - `ReferenceArtifacts` empty
   - `MemoryEnabled == true` unless explicitly overridden
2. switching from `A` to `B` with explicit label/instructions applies only the new values.
3. same-project partial update still preserves omitted fields.
4. explicit `projectId: null` still clears the whole project context.

Exit criteria:

1. no stale project metadata survives a real project switch;
2. tests cover both switch and non-switch behavior.

### PR 2. Make follow-through completion project-affine, not just branch-affine

Priority: high

Reason:

Once project switching is clean, queued follow-through must also stay attached to the project snapshot that created it. Otherwise the repo still misreports continuity state across project changes.

Files to change:

1. `src/Helper.Api/Conversation/BackgroundTaskContracts.cs`
2. `src/Helper.Api/Conversation/FollowThroughScheduler.cs`
3. `src/Helper.Api/Conversation/ConversationFollowThroughProcessor.cs`
4. `src/Helper.Api/Conversation/ConversationPersistenceModels.cs`
5. `test/Helper.Runtime.Tests/ConversationFollowThroughTests.cs`
6. `test/Helper.Runtime.Tests/ConversationFollowThroughBranchAffinityTests.cs`
7. any persistence round-trip tests that already cover `BackgroundConversationTask`

Implementation steps:

1. extend `BackgroundConversationTask` with stable project-affinity fields captured at queue time:
   - `ProjectLabelSnapshot`
   - `ReferenceArtifactsSnapshot`
   - optionally `EnabledTopicSnapshot` if completion text should remain stable even when topic toggles later change
2. in `FollowThroughScheduler.QueueResearchFollowThrough`, stamp those snapshot fields from the active `state.ProjectContext` and currently enabled proactive topics for that project;
3. in `ConversationFollowThroughProcessor.BuildCompletionMessage`, prefer task snapshots over current `state.ProjectContext`;
4. keep existing fallback behavior for older tasks that do not yet have the new fields:
   - use current state only when the snapshot is absent
5. if proactive topics stay live rather than snapshotted, at minimum filter them by `task.ProjectId` so completion does not describe topics from another project;
6. update persistence mapping so the new task fields survive snapshot/restore.

Required tests:

1. queue a task under project `A`, switch conversation to project `B`, process pending, confirm completion message still names project `A`.
2. queue with project `A` references, switch to `B`, confirm completion message still shows `A` references.
3. persisted queued task with snapshots survives round-trip restore and still completes correctly.
4. legacy task without snapshots still completes via fallback path.

Exit criteria:

1. completion messages remain stable and truthful even after project switch;
2. branch affinity and project affinity are both preserved.

### PR 3. Make Project Context Settings panel truly project-scoped

Priority: high

Reason:

The current UI text is stronger than the behavior. After the backend semantics are fixed, the Settings surface must stop mixing continuity state from unrelated projects.

Files to change:

1. `hooks/useSettingsViewState.ts`
2. `components/settings/SettingsProjectContextPanel.tsx`
3. `components/views/SettingsView.tsx`
4. optionally `services/settingsContinuityContracts.ts` if stronger local typing helps

Implementation steps:

1. define derived project-scoped lists in `useSettingsViewState`:
   - `projectScopedBackgroundTasks`
   - `projectScopedProactiveTopics`
2. filter `backgroundTasks` and `proactiveTopics` by the currently active `projectId`;
3. define the empty-project behavior explicitly:
   - if there is no active `projectId`, show no project-scoped tasks/topics and a clear empty-state message;
4. after `saveProjectContext`, refresh the conversation snapshot instead of only saving preferences and reloading memory, so the panel is rehydrated from the backend truth;
5. after `saveContinuityControls`, also refresh the snapshot so filtered lists and project context stay aligned;
6. after `cancelBackgroundTask` and `setProactiveTopicEnabled`, keep using fresh snapshot reloads, but apply the same project filter before rendering;
7. update panel copy if needed so it clearly communicates whether it is showing:
   - only the active project, or
   - no project because no active project is set.

Required tests:

1. with mixed tasks/topics for projects `A` and `B`, active project `A` only renders `A` items.
2. switching active project to `B` swaps the visible lists.
3. clearing `projectId` empties the project-scoped cards instead of showing conversation-wide continuity state.
4. after saving a new project context, the panel rehydrates and reflects the backend snapshot instead of stale local arrays.

Exit criteria:

1. Settings no longer overclaims project-scoping while showing conversation-wide state;
2. panel content matches the active project id at all times.

### PR 4. Add guardrails so these defects cannot return

Priority: medium

Reason:

The repo is already green. The last step is to convert the repaired behavior into regression barriers.

Files to change:

1. `test/Helper.Runtime.Tests/ConversationPreferencesRoundTripTests.cs`
2. `test/Helper.Runtime.Tests/ConversationFollowThroughTests.cs`
3. `test/Helper.Runtime.Tests/ConversationFollowThroughBranchAffinityTests.cs`
4. add a small frontend/state test if there is already a viable harness; otherwise cover the filtering logic through extracted pure helpers
5. `test/Helper.Runtime.Tests/Helper.Runtime.Tests.ApiLane.props` only if new tests need lane inclusion
6. optionally `doc/architecture/HELPER_PUBLIC_REPO_AUDIT_2026-04-10.md` and this plan document for completion note

Implementation steps:

1. keep project-switch regression coverage in backend tests, not only in UI;
2. add explicit assertions for `ReferenceArtifacts` reset on project switch;
3. add explicit assertions for project-affine follow-through completion text;
4. if the settings filter is extracted to a helper, add direct unit tests for the filter semantics;
5. once all fixes merge, mark this plan `completed`.

Exit criteria:

1. all three behavioral defects have direct regression coverage;
2. the plan can be closed without relying on manual reasoning.

## Suggested Commit / PR Shape

1. `Fix project context switching semantics`
2. `Make follow-through completion project-affine`
3. `Scope project context settings to the active project`
4. `Add regression guardrails for project-affinity defects`
5. `Mark zero-defect fix plan completed`

If stacked PR are used:

1. `PR 1` base `main`
2. `PR 2` base `PR 1`
3. `PR 3` base `PR 2`
4. `PR 4` base `PR 3`
5. doc-only completion PR last, or fold it into `PR 4` if preferred

## Verification Matrix

After each PR:

1. `dotnet build Helper.sln -c Debug -m:1`
2. `./scripts/run_fast_tests.ps1 -Configuration Debug -NoRestore`

Additional targeted checks:

1. after `PR 1`:
   - run `ConversationPreferencesRoundTripTests`
2. after `PR 2`:
   - run `ConversationFollowThroughTests`
   - run `ConversationFollowThroughBranchAffinityTests`
3. after `PR 3`:
   - run the new project-scoping UI/state regression tests
4. after `PR 4`:
   - rerun the full fast lane
   - optionally rerun `./scripts/run_integration_tests.ps1 -Configuration Debug -NoRestore`

## Definition Of Done

This plan is complete only when all of the following are true:

1. switching `projectId` creates a genuinely fresh project context instead of carrying stale metadata;
2. queued follow-through completion remains truthful after both branch switch and project switch;
3. Settings `Project Context` shows only continuity state for the active project;
4. all new behaviors are covered by regression tests;
5. `dotnet build Helper.sln -c Debug -m:1` passes;
6. `./scripts/run_fast_tests.ps1 -Configuration Debug -NoRestore` passes;
7. this document is updated to `Status: completed`.

## Completion Note

Implemented on `2026-04-10`.

Completed outcomes:

1. `projectId` switching now creates a fresh `ProjectContextState` instead of cloning stale project metadata and reference artifacts.
2. follow-through tasks now capture stable project-affinity snapshots for project label, reference artifacts, and enabled proactive topics, while public `GET /api/chat/{conversationId}` continues to expose the old external task shape.
3. Settings `Project Context` now renders project-scoped continuity lists only, with explicit empty-state behavior when no active project is set.
4. regression coverage was added for:
   - project switch reset semantics
   - follow-through project-affinity
   - task snapshot persistence
   - frontend source-level project-scoping guardrails

Final verification:

1. `dotnet build Helper.sln -c Debug -m:1`
2. `npm run build`
3. `./scripts/run_fast_tests.ps1 -Configuration Debug -NoRestore`
4. `./scripts/run_integration_tests.ps1 -Configuration Debug -NoRestore`
