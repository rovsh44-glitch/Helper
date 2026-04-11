# ADR: Provider Runtime Switching

Date: 2026-04-11
Status: Accepted

## Context

`provider profile` activation exposed an API/UI surface for `Ollama` and `OpenAI-Compatible`, but the runtime client stayed bound to Ollama-style endpoints. The old flow mutated process-wide environment variables and gave a false impression that transport switching had happened.

This created an unsafe contract:

- activation looked successful in settings;
- `AILink` still reused the old transport shape;
- `OpenAI-Compatible` profiles did not have a real `/models`, `/chat/completions`, or `/embeddings` execution path.

## Decision

The runtime now treats active provider state as an in-memory runtime contract, not as process-wide env routing.

1. `ProviderProfileResolver` converts the active profile into `AiProviderRuntimeSettings`.
2. `AILink` owns an applyable runtime snapshot with explicit `TransportKind`, `BaseUrl`, `DefaultModel`, `ApiKey`, `EmbeddingModel`, and model bindings.
3. `AILink` keeps one public facade, but transport behavior is explicit:
   - `Ollama` uses `/api/tags`, `/api/generate`, `/api/embeddings`
   - `OpenAI-Compatible` uses `/models`, `/chat/completions`, `/embeddings`
4. `HELPER_ACTIVE_PROVIDER_PROFILE_ID` remains only as a marker of the selected profile. It is no longer the functional routing mechanism.
5. Activation always refreshes the model catalog through `IModelGateway.DiscoverAsync`, so the first post-activation runtime call uses the new transport contract.

## Consequences

- Provider switching changes actual runtime behavior without process restart.
- `OpenAI-Compatible` routing has first-class request, streaming, discovery, and embeddings support.
- Model selection can prefer active-profile bindings before falling back to static env defaults.
- Runtime and API tests now cover:
  - activation applying new runtime settings
  - OpenAI-compatible transport endpoints and bearer auth
  - endpoint-level activation causing a real catalog refresh through the switched transport

## Boundary

Future provider work must extend the runtime contract and tests together. Do not reintroduce env-only transport routing semantics.
