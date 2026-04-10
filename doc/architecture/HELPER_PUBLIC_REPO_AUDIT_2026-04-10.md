# HELPER_PUBLIC_REPO_AUDIT_2026-04-10

Status: completed
Date: 2026-04-10
Scope: public `main` of `rovsh44-glitch/Helper`

## Findings

1. `public main` did not build because `ConversationFollowThroughBranchAffinityTests` referenced `ChatRequestDto` and `ChatMessageDto` without importing `Helper.Api.Hosting`.
2. `POST /api/chat/{conversationId}/preferences` had no route-level regression test. Existing coverage only exercised the payload reader helper and a source-level architecture assertion.
3. `ConversationPreferencePayloadReader` accepted non-object JSON payloads such as top-level `null`, which let the endpoint treat invalid input as a successful no-op update instead of returning `400 Bad Request`.

## Remediation

1. Restored the missing DTO namespace import in `ConversationFollowThroughBranchAffinityTests`.
2. Hardened `ConversationPreferencePayloadReader` to reject any payload whose root JSON kind is not `Object`.
3. Added a route-level regression suite for `POST /api/chat/{conversationId}/preferences` covering:
   - camelCase binding
   - explicit `null` clear semantics
   - rejection of top-level `null`
4. Added the new route-level test file to the API lane manifest so the guardrail runs in the fast API lane.

## Verification

- `dotnet build Helper.sln -c Debug -m:1`
- `dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj -c Debug --no-build`
- `dotnet test test/Helper.Runtime.Api.Tests/Helper.Runtime.Api.Tests.csproj -c Debug --no-build`
- `./scripts/run_fast_tests.ps1 -Configuration Debug -NoRestore`

## Outcome

The three defects found in the 2026-04-10 audit were fixed in the local workspace and covered by regression tests.
