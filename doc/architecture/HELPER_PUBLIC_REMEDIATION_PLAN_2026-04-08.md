# HELPER Public Remediation Plan

Status: `completed`
Updated: `2026-04-08`
Target repo: `https://github.com/rovsh44-glitch/Helper`
Decision boundary: keep `0e95b9b` and `ed0ad59` private until an explicit paired-export decision is approved

## Goal

Bring the public repository back to an honest, supportable state with one of two outcomes:

1. a narrower public Helper surface that exposes only what public backend and persistence actually implement; or
2. a fuller public Helper surface that exports the missing non-voice collaboration state first, and only later exports live voice as a complete paired slice.

The recommended path is:

1. remove unsupported public surfaces first;
2. export the non-voice personalization and project-context slice from private;
3. add parity guardrails;
4. decide separately whether live voice should stay private or ship as a full paired export.

## What To Remove From Public Immediately

### Slice A. Remove unsupported continuity and live-voice client surface

Files to change:

1. `services/generatedApiClient.ts`
2. `services/conversationApi.ts`
3. `services/settingsContinuityContracts.ts`
4. `hooks/useSettingsViewState.ts`
5. `components/settings/SettingsProjectContextPanel.tsx`
6. `components/views/SettingsView.tsx`

Required changes:

1. remove `backgroundTasks`, `proactiveTopics`, and `liveVoiceSession` from the public `getConversation` response shape;
2. remove `cancelBackgroundTask`, `setProactiveTopicEnabled`, `syncLiveVoiceSession`, `appendLiveVoiceChunk`, and `clearLiveVoiceSession`;
3. remove the `conversationApi.ts` wrappers that call those unsupported endpoints;
4. delete `services/settingsContinuityContracts.ts` after its imports are removed;
5. remove the Background Tasks, Proactive Topics, and Live Voice Session sections from `SettingsProjectContextPanel`;
6. remove state hydration, action handlers, and governance snapshot fields for those unsupported continuity entities from `useSettingsViewState`.

Reason:

The current public backend and public OpenAPI contract do not implement these routes or snapshot fields, so public UI must stop pretending they exist.

### Slice B. Remove or hide no-op advanced settings until backend support lands

Files to change:

1. `components/settings/SettingsPersonalizationPanel.tsx`
2. `components/settings/SettingsProjectContextPanel.tsx`
3. `components/views/SettingsView.tsx`
4. `hooks/useSettingsViewState.ts`
5. `src/Helper.Api/Hosting/ApiContracts.cs`

Required changes if the private export does not land in the same PR:

1. hide or remove the public Settings controls for `decisionAssertiveness`, `clarificationTolerance`, `citationPreference`, `repairStyle`, `reasoningStyle`, `reasoningEffort`, and `personaBundleId`;
2. hide or remove the public Settings controls for `projectId`, `projectLabel`, `projectInstructions`, `projectMemoryEnabled`, `backgroundResearchEnabled`, and `proactiveUpdatesEnabled`;
3. stop persisting these fields from `useSettingsViewState` until the backend state, profile, and persistence paths are real;
4. trim `ConversationPreferenceDto` in `ApiContracts.cs` if the public product decision is to keep the narrower surface for now;
5. trim `BackgroundTaskActionRequestDto`, `ProactiveTopicActionRequestDto`, `LiveVoiceSessionSyncRequestDto`, and `LiveVoiceChunkSyncRequestDto` if their routes remain private.

Reason:

Today these controls are either ignored, partially applied, or dropped on persistence. Public should not expose settings that do not round-trip.

## What To Export From Private Next

### Slice C. Export the non-voice personalization and project-context core first

Recommended private-to-public export set:

1. `src/Helper.Api/Conversation/ConversationState.cs`
2. `src/Helper.Api/Conversation/ConversationUserProfile.cs`
3. `src/Helper.Api/Conversation/UserProfileService.cs`
4. `src/Helper.Api/Conversation/ConversationPersistenceModels.cs`
5. `src/Helper.Api/Conversation/PersonalizationProfile.cs`
6. `src/Helper.Api/Conversation/ProjectContextState.cs`
7. `src/Helper.Api/Conversation/UserUnderstandingState.cs`
8. `src/Helper.Api/Conversation/ProjectUnderstandingState.cs`
9. `src/Helper.Api/Conversation/PersonalizationMergePolicy.cs`
10. `src/Helper.Api/Conversation/ConversationModelCapabilityCatalog.cs`
11. `src/Helper.Api/Conversation/ConversationModelSelectionPolicy.cs`
12. `src/Helper.Api/Conversation/ConversationPromptPolicy.cs`
13. `src/Helper.Api/Conversation/ProjectInstructionPolicy.cs`
14. `src/Helper.Api/Conversation/ProjectMemoryBoundaryPolicy.cs`
15. `src/Helper.Api/Hosting/EndpointRegistrationExtensions.Conversation.StateAndReplay.cs`
16. `src/Helper.Api/Hosting/ServiceRegistrationExtensions.Conversation.cs`

Public cleanup required at the same time:

1. delete or replace `src/Helper.Api/Conversation/ConversationPersonalizationCompatibility.cs`;
2. ensure the public `GET /api/chat/{conversationId}` snapshot returns only fields that now genuinely exist in state;
3. ensure the public `POST /api/chat/{conversationId}/preferences` path actually writes and returns those advanced fields.

Expected effect:

1. advanced personalization stops being declarative-only;
2. persona bundle, reasoning effort, project context, and related state survive persistence;
3. public Settings can re-enable these controls honestly.

### Slice D. Export non-voice follow-through and richer memory inspection

Recommended private-to-public export set:

1. `src/Helper.Api/Conversation/BackgroundTaskContracts.cs`
2. `src/Helper.Api/Conversation/FollowThroughScheduler.cs`
3. `src/Helper.Api/Conversation/ConversationFollowThroughProcessor.cs`
4. `src/Helper.Api/Conversation/ProactiveTopicPolicy.cs`
5. `src/Helper.Api/Conversation/MemoryInspectionService.cs`
6. `src/Helper.Api/Conversation/MemoryPriorityPolicy.cs`
7. `src/Helper.Api/Hosting/EndpointRegistrationExtensions.Conversation.Memory.cs`
8. `src/Helper.Api/Hosting/ServiceRegistrationExtensions.Conversation.cs`

Expected effect:

1. background follow-through becomes a real behavior instead of a stub;
2. proactive topic toggles stop being cosmetic;
3. memory inspection exposes the richer scope and priority information already present in private Helper.

### Slice E. Export guardrail and round-trip tests

Recommended test and validation work:

1. add a client/server parity test so `services/generatedApiClient.ts` cannot silently drift past `OpenApiDocumentFactory`;
2. add settings round-trip coverage for advanced personalization, project context, and persistence reload;
3. add or export follow-through tests from private where relevant;
4. extend the existing contract tests so unsupported public endpoints cannot reappear in the client without server support.

Representative candidate files from private:

1. `test/Helper.Runtime.Tests/ConversationPromptPolicyTests.cs`
2. `test/Helper.Runtime.Tests/ConversationFollowThroughTests.cs`
3. `test/Helper.Runtime.Tests/CommunicationQualityPolicyTests.cs`
4. `test/Helper.Runtime.Tests/ArchitectureFitnessTests.RepoAndContracts.cs`

## What Must Stay Private For Now

The following work should remain private until a full paired export is approved:

1. `0e95b9b` app-shell and live-voice runtime client surface;
2. the live-voice server files from `ed0ad59`, including `ConversationLiveVoiceSessionService.cs`, `LiveVoiceTurnCoordinator.cs`, `LiveVoiceSessionContracts.cs`, and `EndpointRegistrationExtensions.Conversation.LiveVoice.cs`;
3. any live-voice route, client runtime hook, or app-shell routing surface that would expose live voice without the complete server, state, persistence, and UI package.

Rule:

Do not export only one side of the live-voice pair. Either keep both commits private, or shape a dedicated branch that exports the matching client and server slices together.

## Repair Order

### PR 1. Public contract honesty

1. remove unsupported continuity and live-voice client surface;
2. remove or hide no-op advanced settings if backend export is not landing in the same PR;
3. trim `ApiContracts.cs` to the actual public scope if needed;
4. regenerate or hand-trim `services/generatedApiClient.ts` so it matches the real public contract.

Exit criteria:

1. Settings no longer exposes unsupported routes or snapshot sections;
2. public TypeScript client no longer claims live-voice and continuity routes that do not exist.

### PR 2. Non-voice personalization and project-context export

1. export the state, profile, persistence, prompt, and project-context files listed in Slice C;
2. replace the compatibility shim with the canonical private implementation;
3. re-enable only the advanced Settings fields that now round-trip cleanly.

Exit criteria:

1. save -> reload -> persisted snapshot works for advanced personalization and project context;
2. public repo now has honest support for these fields.

### PR 3. Non-voice follow-through export

1. export the contracts, scheduler, processor, topic policy, and memory inspection files from Slice D;
2. re-enable background follow-through and proactive topic controls only after these behaviors are live;
3. add tests for cancel, toggle, and queued follow-through behavior.

Exit criteria:

1. follow-through is no longer a stub;
2. public settings controls match working backend behavior.

### PR 4. Contract guardrails

1. add TS client vs OpenAPI parity enforcement;
2. add settings round-trip tests;
3. fail CI on client/server contract drift.

Exit criteria:

1. the current continuity/live-voice drift class becomes impossible to merge silently again.

### PR 5. Optional full live-voice and app-shell parity

1. only if explicitly approved, create a dedicated export branch for the live-voice pair;
2. export the client runtime, server endpoints, conversation state, persistence, and tests together;
3. validate build, runtime slice tests, frontend build, and the full route contract before merge.

Exit criteria:

1. public live voice exists as a complete feature, not as a partial trace.

## Definition Of Done

1. public UI exposes only features that public backend and persistence actually implement;
2. advanced settings either round-trip correctly or do not exist in the public product surface;
3. follow-through is either fully implemented or absent from public settings;
4. the generated TS client cannot drift beyond public OpenAPI without failing CI;
5. live voice stays private until the full paired export is intentionally approved.

## Completion Note

Completed on `2026-04-08`.

Closed state:

1. `PR 1` removed unsupported live-voice public traces and narrowed the public contract;
2. `PR 2` exported non-voice personalization and project-context state with real round-trip persistence;
3. `PR 3` exported non-voice follow-through, proactive topic controls, and richer memory inspection;
4. `PR 4` added parity and lane guardrails, and follow-up lane hygiene work restored green `fast` and `integration` public test runs;
5. `PR 5` was intentionally not executed, which matches the decision boundary to keep live voice private until an explicit paired export is approved.
