# HELPER Public Post-Remediation Fix Plan

Status: `completed`
Updated: `2026-04-09`
Target repo: `https://github.com/rovsh44-glitch/Helper`
Validated public baseline: `e927306`

## Goal

Fix the remaining public-repo defects that survived the earlier remediation work:

1. remove deceptive settings that still do not have real runtime effect;
2. make project context and project memory boundary operational instead of decorative;
3. restore true reversibility for advanced settings;
4. ensure reset/new-conversation flows clear conversation-scoped settings state honestly.

This plan is intentionally narrower than the `2026-04-08` remediation plan. That earlier plan restored public contract honesty and green CI. This plan addresses the remaining product-level behavior gaps inside the now-green public surface.

## Current Baseline

At `e927306` the public repository is healthy at build/test level:

1. `dotnet build Helper.sln -c Debug -m:1` passes;
2. `./scripts/run_fast_tests.ps1 -Configuration Debug -NoRestore` passes;
3. `./scripts/run_integration_tests.ps1 -Configuration Debug -NoRestore` passes;
4. live voice remains private and is still correctly excluded from public contract.

The remaining issues are not compile failures. They are behavior and surface-honesty defects.

## Findings This Plan Fixes

### 1. Project context is persisted and shown, but not actually injected into answer generation

Symptoms:

1. `ProjectContextState` is saved and returned by `GET /api/chat/{conversationId}`;
2. `ProjectInstructionPolicy` exists and is registered;
3. the actual prompt path does not consume that policy, so `projectInstructions` and `referenceArtifacts` are mostly dead settings.

### 2. Project memory boundary toggle is effectively no-op

Symptoms:

1. UI claims the toggle keeps memory project-scoped;
2. `ProjectMemoryBoundaryPolicy` exists and is registered;
3. memory capture and prompt-side legacy memory injection do not use the policy in a way that changes runtime behavior.

### 3. `personaBundleId` is public but behaviorally dead

Symptoms:

1. UI exposes `Persona Bundle`;
2. DTO, persistence, and snapshot all carry the field;
3. style and prompt generation do not use it as a real runtime input.

### 4. Advanced string settings are not fully reversible

Symptoms:

1. client save path serializes cleared text inputs as omitted values;
2. server update logic treats omission as keep-existing;
3. previously saved `personaBundleId`, `projectId`, `projectLabel`, and `projectInstructions` cannot be cleanly cleared from public UI.

### 5. Reset/new-conversation flow can leave stale conversation-scoped settings visible

Symptoms:

1. reset clears active conversation id;
2. settings hook clears memory list/status only;
3. project context, background tasks, and proactive topics can remain visible until a new snapshot arrives.

## Repair Order

### PR 1. Reversibility and reset honesty

Priority: highest

Reason:

This is the lowest-risk fix and removes the most obvious public UX dishonesty first.

Files to change:

1. `hooks/useSettingsViewState.ts`
2. `services/generatedApiClient.ts`
3. `services/conversationApi.ts`
4. `src/Helper.Api/Hosting/ApiContracts.cs`
5. `src/Helper.Api/Conversation/UserProfileService.cs`
6. `test/Helper.Runtime.Tests/ConversationPreferencesRoundTripTests.cs`
7. add a new UI/state regression test if there is already a suitable frontend test harness; otherwise cover through API/state tests

Required changes:

1. define explicit clear semantics for text settings that are meant to be reversible:
   `personaBundleId`, `projectId`, `projectLabel`, `projectInstructions`, and `searchLocalityHint`;
2. allow the TS client to send `null` for clearable fields instead of silently omitting them;
3. update `UserProfileService.ApplyPreferences` so `null` clears the existing value instead of preserving it;
4. add explicit project-context clear behavior:
   if `projectId` is cleared, clear the whole `ProjectContext` object unless a separate replacement context is supplied in the same request;
5. when there is no active conversation id, reset all conversation-scoped settings state in `useSettingsViewState`, not only memory status;
6. ensure governance snapshot/export state reflects the cleared conversation-scoped values.

Exit criteria:

1. save -> clear -> reload works for all clearable advanced string settings;
2. resetting a conversation no longer leaves stale project/follow-through state in Settings.

### PR 2. Make project context operational in answer generation

Priority: high

Reason:

Public should not expose `Project Instructions` and `Reference Artifacts` unless they affect the model turn.

Files to change:

1. `src/Helper.Api/Conversation/ProjectInstructionPolicy.cs`
2. `src/Helper.Api/Conversation/SharedUnderstandingService.cs`
3. `src/Helper.Api/Conversation/ConversationContextAssembler.cs`
4. `src/Helper.Api/Conversation/ChatTurnAnswerService.cs`
5. `src/Helper.Api/Conversation/ChatPromptFormatter.cs`
6. `test/Helper.Runtime.Tests/ConversationPromptPolicyTests.cs`
7. add a focused prompt-assembly regression test if needed

Required changes:

1. inject project-context blocks into the assembled prompt path, not only into persistence/snapshot state;
2. include `ProjectInstructionPolicy.BuildContextBlock(...)` in prompt assembly when `ProjectContext` is active;
3. include `SharedUnderstandingService.BuildContextBlock(...)` in prompt assembly when shared-understanding state exists;
4. preserve current retrieval/procedural/history layering while appending the new blocks as explicit additional context;
5. add tests proving that `projectInstructions`, `referenceArtifacts`, and memory-boundary mode appear in the actual assembled prompt for active project conversations.

Exit criteria:

1. project-context controls visibly affect the runtime prompt path;
2. prompt-assembly tests fail if project context stops being injected again.

### PR 3. Make project memory boundary real

Priority: high

Reason:

Right now the toggle exists, but the conversation still captures and surfaces memory as if project scoping were always active whenever a project is present.

Files to change:

1. `src/Helper.Api/Conversation/MemoryPolicyService.cs`
2. `src/Helper.Api/Conversation/ProjectMemoryBoundaryPolicy.cs`
3. `src/Helper.Api/Conversation/InMemoryConversationStore.Branches.cs`
4. `src/Helper.Api/Conversation/MemoryInspectionService.cs`
5. `src/Helper.Api/Conversation/ConversationState.cs` if helper methods or derived views are needed
6. `test/Helper.Runtime.Tests/ConversationFollowThroughTests.cs` if affected
7. add new memory-boundary regression tests

Required changes:

1. apply `ProjectMemoryBoundaryPolicy` in the runtime paths that surface memory back into the prompt and memory inspection APIs;
2. when `ProjectContext.MemoryEnabled == false`, capture new memory with conversation-wide semantics instead of project-scoped semantics;
3. derive prompt-side legacy memory summaries (`User preferences`, `Open tasks`) from boundary-filtered visible memory, not from raw unfiltered collections;
4. ensure `/api/chat/{conversationId}/memory` reflects the effective boundary honestly;
5. add tests for both modes:
   `projectMemoryEnabled = true` and `projectMemoryEnabled = false`.

Exit criteria:

1. toggling project memory boundary changes actual capture and recall behavior;
2. prompt and memory inspection paths agree on what is visible under the active boundary.

### PR 4. Resolve `personaBundleId` honesty

Priority: medium

Recommended path: remove from public for now

Reason:

There is no public persona-bundle runtime resolver today. Keeping a dead field is worse than narrowing the surface.

Recommended files to change:

1. `components/settings/SettingsPersonalizationPanel.tsx`
2. `components/views/SettingsView.tsx`
3. `hooks/useSettingsViewState.ts`
4. `services/generatedApiClient.ts`
5. `src/Helper.Api/Hosting/ApiContracts.cs`
6. `src/Helper.Api/Hosting/EndpointRegistrationExtensions.Conversation.StateAndReplay.cs`
7. `src/Helper.Api/Conversation/UserProfileService.cs`
8. `test/Helper.Runtime.Tests/ClientContractParityTests.cs`
9. `test/Helper.Runtime.Tests/ConversationPreferencesRoundTripTests.cs`

Recommended changes:

1. remove `Persona Bundle` from public Settings;
2. remove `personaBundleId` from public preference DTOs and snapshots;
3. remove the field from public parity tests after contract narrowing;
4. keep the concept private until a real bundle catalog and routing effect exist.

Alternative path if you explicitly want to keep it public:

1. export a concrete public bundle catalog;
2. wire bundle selection into style route and prompt policy;
3. add behavior tests proving bundle choice changes runtime output constraints.

Exit criteria:

1. public no longer exposes behaviorless `personaBundleId`; or
2. if retained, it has a tested runtime effect.

## Verification Matrix

Each PR should re-run:

1. `dotnet build Helper.sln -c Debug -m:1`
2. `./scripts/run_fast_tests.ps1 -Configuration Debug -NoRestore`
3. `./scripts/run_integration_tests.ps1 -Configuration Debug -NoRestore`
4. `npm run build`

Additional required targeted tests:

1. round-trip clear/reload tests for advanced settings;
2. prompt-assembly tests for project context injection;
3. memory-boundary mode tests for prompt and memory inspection visibility;
4. contract parity tests after any public DTO narrowing.

## Non-Goals

This plan does not reopen live voice.

Keep private:

1. `0e95b9b`
2. `ed0ad59`
3. any `/voice/session` route or live-voice client runtime

## Exit Criteria

This plan is complete only when all of the following are true:

1. project-context controls have real runtime effect, not just persistence effect;
2. project-memory boundary toggle changes real behavior;
3. advanced settings that remain public are actually reversible;
4. resetting a conversation clears all conversation-scoped settings state in UI;
5. `personaBundleId` is either removed from public or given tested runtime semantics.

## Completion Note

Completed on `2026-04-09`.

Delivered:

1. `personaBundleId` removed from the public settings and contract surface;
2. project context and shared-understanding blocks injected into the real prompt assembly path;
3. project memory boundary applied to capture, prompt-side legacy summaries, and memory inspection;
4. conversation-scoped settings reset honestly when there is no active conversation;
5. clearable advanced string fields now use explicit presence-aware clear semantics in the preferences endpoint;
6. verification matrix passed:
   `dotnet build Helper.sln -c Debug -m:1`,
   `npm run build`,
   `./scripts/run_fast_tests.ps1 -Configuration Debug -NoRestore`,
   `./scripts/run_integration_tests.ps1 -Configuration Debug -NoRestore`.
