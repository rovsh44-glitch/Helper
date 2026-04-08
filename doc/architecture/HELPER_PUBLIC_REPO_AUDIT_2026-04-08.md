# HELPER Public Repo Audit

Status: `audit snapshot`
Updated: `2026-04-08`
Target repo: `https://github.com/rovsh44-glitch/Helper`
Public baseline: `main @ ba8a9a3`
Private reference commits intentionally kept out of public: `0e95b9b`, `ed0ad59`

## Audit Basis

This audit compares the current public `main` state against the local Helper workspace and the two intentionally withheld private slices.

The repository is operational at the build and test level:

1. `dotnet build Helper.sln -c Debug -m:1` passed.
2. `dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj -c Debug --no-build` passed.
3. `scripts/run_dotnet_test_with_monitor.ps1` for `Helper.RuntimeSlice.Api.Tests` passed.
4. `scripts/run_test_project_by_file.ps1` for `Helper.Runtime.Certification.Compile.Tests` passed.
5. `npm run build` passed.

The main conclusion is therefore not "public repo is broken to build", but "public repo contains product and contract drift relative to canonical Helper behavior."

## Findings

### 1. Public `main` exposes continuity and live-voice surfaces that the public backend does not implement

Severity: `critical`

The public TypeScript client and Settings UI expect:

1. `backgroundTasks`
2. `proactiveTopics`
3. `liveVoiceSession`
4. `/api/chat/{conversationId}/background/{taskId}/cancel`
5. `/api/chat/{conversationId}/topics/{topicId}`
6. `/api/chat/{conversationId}/voice/session`
7. `/api/chat/{conversationId}/voice/session/{sessionId}/chunks`
8. `/api/chat/{conversationId}/voice/session/{sessionId}`

Evidence in the public tree:

1. `services/generatedApiClient.ts` declares those snapshot fields and mutators.
2. `hooks/useSettingsViewState.ts` hydrates, stores, and mutates those fields.
3. `components/settings/SettingsProjectContextPanel.tsx` renders background task, proactive topic, and live-voice sections as user-facing controls.

The public backend does not match that contract:

1. `src/Helper.Api/Hosting/EndpointRegistrationExtensions.Conversation.StateAndReplay.cs` returns only the basic conversation snapshot and memory view.
2. `src/Helper.Api/Hosting/OpenApiDocumentFactory.cs` does not publish `/background`, `/topics`, or `/voice/session` routes.

Impact:

1. public Settings exposes controls that cannot be satisfied by the public backend;
2. the public client contract is wider than the public server contract;
3. build and unit-test green status currently hides a real user-visible surface mismatch.

### 2. Advanced personalization and project-context fields are declared publicly but do not work end-to-end

Severity: `critical`

The public DTO surface and Settings flow declare advanced Helper preferences such as:

1. `decisionAssertiveness`
2. `clarificationTolerance`
3. `citationPreference`
4. `repairStyle`
5. `reasoningStyle`
6. `reasoningEffort`
7. `personaBundleId`
8. `projectId`
9. `projectLabel`
10. `projectInstructions`
11. `projectMemoryEnabled`
12. `backgroundResearchEnabled`
13. `proactiveUpdatesEnabled`

Evidence in the public tree:

1. `src/Helper.Api/Hosting/ApiContracts.cs` declares these fields on `ConversationPreferenceDto`.
2. `services/generatedApiClient.ts` exposes them in both snapshot and save-preferences shapes.
3. `hooks/useSettingsViewState.ts` saves and hydrates them.
4. `components/settings/SettingsPersonalizationPanel.tsx` and `components/settings/SettingsProjectContextPanel.tsx` render them as active settings.

But the public backend state and persistence layer are incomplete:

1. `src/Helper.Api/Conversation/ConversationState.cs` does not carry the full advanced personalization and continuity state that private Helper uses.
2. `src/Helper.Api/Conversation/ConversationUserProfile.cs` still models only the basic profile surface.
3. `src/Helper.Api/Conversation/UserProfileService.cs` resolves and applies only the basic profile fields.
4. `src/Helper.Api/Conversation/ConversationPersistenceModels.cs` does not serialize the advanced personalization, project-context, continuity, or live-state fields that the public UI implies.
5. `src/Helper.Api/Conversation/ConversationPersonalizationCompatibility.cs` is a compatibility shim, not the full private implementation.

Impact:

1. the public UI can send "saved" advanced preferences that are ignored or partially dropped;
2. reload and journal replay do not preserve the implied advanced state;
3. the public repo currently overstates its true Helper personalization and project-context support.

### 3. Follow-through infrastructure is only partially exported and currently stubbed

Severity: `high`

The public repo already contains follow-through interfaces and registrations, but key behavior is still placeholder logic:

1. `src/Helper.Api/Conversation/ConversationFollowThroughProcessor.cs` returns `0` or `false` for processing, cancel, and topic-toggle operations.
2. `src/Helper.Api/Conversation/FollowThroughScheduler.cs` is effectively a no-op.

In private Helper, the withheld slice contains the missing behavior:

1. `BackgroundTaskContracts.cs`
2. `ProactiveTopicPolicy.cs`
3. the non-stub `FollowThroughScheduler.cs`
4. the non-stub `ConversationFollowThroughProcessor.cs`

Impact:

1. even if background-topic routes were added back, public behavior would still not match canonical Helper;
2. current public code suggests support for async follow-through, but the actual execution path is incomplete.

### 4. Contract guardrails validate the manual OpenAPI snapshot, not the shipped TypeScript client

Severity: `high`

The public repo uses `src/Helper.Api/Hosting/OpenApiDocumentFactory.cs` as the public contract authority, and tests validate that snapshot in:

1. `test/Helper.Runtime.Tests/ApiSchemaTests.cs`
2. `test/Helper.Runtime.Tests/ApiContractTests.cs`

But the shipped frontend client in `services/generatedApiClient.ts` is not parity-checked against that public contract.

Impact:

1. the repository can stay green while the public client has already drifted past the server contract;
2. the continuity/live-voice mismatch exists specifically because this parity gap is not guarded in CI.

## Public Repo Assessment

The current public export is operational, but it is not a faithful parity snapshot of canonical Helper.

The central problem is partial export drift:

1. public contains Settings and client traces of withheld continuity and live-voice work;
2. public declares advanced personalization and project-context knobs that are not backed by full state, profile, persistence, and endpoint wiring;
3. public keeps placeholder follow-through behavior where private Helper already has working implementations.

## Bottom Line

The public repo is usable as a narrower Helper build, but it should not currently be treated as a truthful public contract for continuity, advanced personalization, project context, or live voice.

Until remediation lands, the public tree should be interpreted as:

1. stable for core chat/runtime flows;
2. incomplete for advanced collaboration continuity;
3. intentionally behind private Helper in the withheld `0e95b9b` and `ed0ad59` slices.
