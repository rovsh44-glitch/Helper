# HELPER Environment Reference

Generated: `2026-04-15 15:28:43 UTC`
Source of truth: `src/Helper.Api/Backend/Configuration/BackendEnvironmentInventory.cs`

This reference covers the governed configuration surface for:

1. backend bootstrap and runtime options
2. local frontend bootstrap variables
3. active operator / CI script variables that are intentionally governed

Unknown names in `.env.local.example` are treated as repo drift. Deprecated names remain documented here until consumers are migrated off them.

## Runtime Paths And Bootstrap

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_API_KEY` | `secret` | none | `backend_runtime` | Primary backend bootstrap key.; secret |
| `HELPER_ROOT` | `path` | none | `backend_runtime` | Optional explicit helper root. Usually auto-discovered.; Keep unset unless auto-discovery is wrong. |
| `HELPER_DATA_ROOT` | `path` | none | `backend_runtime` | Runtime data root. Must live outside the repository source tree. |
| `HELPER_PROJECTS_ROOT` | `path` | none | `backend_runtime` | Workspace/project output root. Defaults under HELPER_DATA_ROOT. |
| `HELPER_LIBRARY_ROOT` | `path` | none | `backend_runtime` | Library root. Defaults under HELPER_DATA_ROOT. |
| `HELPER_LOGS_ROOT` | `path` | none | `backend_runtime` | Logs root. Defaults under HELPER_DATA_ROOT. |
| `HELPER_TEMPLATES_ROOT` | `path` | none | `backend_runtime` | Template library root. Defaults under HELPER_LIBRARY_ROOT\forge_templates. |
| `HELPER_CONVERSATION_STORE_PATH` | `path` | none | `backend_runtime` | Optional explicit conversation store file path. |

## Auth And Bootstrap

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_SESSION_SIGNING_KEY` | `secret` | none | `backend_runtime` | Dedicated session signing secret for issued browser tokens.; secret |
| `HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP` | `bool` | `environment-driven` | `backend_runtime` | Allow local `/api/auth/session` bootstrap outside the default Development/Local inference.; Leave unset for normal local development. |
| `HELPER_ALLOW_LOCAL_BOOTSTRAP` | `bool` | none | `compatibility` | Legacy alias for HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP.; deprecated -> `HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP` |
| `HELPER_SESSION_TTL_MIN_MINUTES` | `int` | `2` | `backend_runtime` | Lower bound for issued session token TTL.; range: `1`..`60` |
| `HELPER_SESSION_TTL_MAX_MINUTES` | `int` | `480` | `backend_runtime` | Upper bound for issued session token TTL.; range: `5`..`1440` |
| `HELPER_SESSION_TOKEN_TTL_MINUTES` | `int` | none | `compatibility` | Legacy default session TTL override.; deprecated -> `HELPER_SESSION_TTL_MIN_MINUTES/HELPER_SESSION_TTL_MAX_MINUTES` |
| `HELPER_LOCAL_BOOTSTRAP_SCOPES` | `csv` | none | `backend_runtime` | Optional local bootstrap scope override. Values are intersected with the allowed local scope bundle. |
| `HELPER_AUTH_KEYS_PATH` | `path` | none | `backend_runtime` | Optional explicit auth key store path.; Must not point inside src/. |
| `HELPER_AUTH_KEYS_JSON` | `json` | none | `backend_runtime` | Inline auth key bootstrap payload. Prefer HELPER_AUTH_KEYS_PATH for persisted local setups.; secret |

## Model Warmup

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_MODEL_WARMUP_MODE` | `enum` | `minimal` | `backend_runtime` | Warmup intensity at startup.; allowed: `disabled`, `minimal`, `full` |
| `HELPER_MODEL_WARMUP_CATEGORIES` | `csv` | `fast,reasoning,coder` | `backend_runtime` | Model categories to warm when warmup is enabled. |
| `HELPER_MODEL_PREFLIGHT_ENABLED` | `bool` | `false` | `backend_runtime` | Enable model preflight probes during startup. |
| `HELPER_MODEL_PREFLIGHT_TIMEOUT_SEC` | `int` | `20` | `backend_runtime` | Per-probe preflight timeout.; range: `3`..`120` |
| `HELPER_MODEL_PREFLIGHT_WARN_MS` | `int` | `12000` | `backend_runtime` | Warn threshold for preflight duration.; range: `500`..`180000` |
| `HELPER_WARMUP_IDLE_WINDOW_MS` | `int` | `1200` | `backend_runtime` | Idle spacing between warmup calls.; range: `250`..`5000` |
| `HELPER_WARMUP_BUDGET_MS` | `int` | `30000` | `backend_runtime` | Startup warmup budget.; range: `1000`..`300000` |

## Model Gateway

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_MODEL_POOL_INTERACTIVE` | `int` | `2` | `backend_runtime` | Interactive pool concurrency.; range: `1`..`32` |
| `HELPER_MODEL_POOL_BACKGROUND` | `int` | `1` | `backend_runtime` | Background pool concurrency.; range: `1`..`32` |
| `HELPER_MODEL_POOL_MAINTENANCE` | `int` | `1` | `backend_runtime` | Maintenance pool concurrency.; range: `1`..`16` |
| `HELPER_MODEL_TIMEOUT_INTERACTIVE_SEC` | `int` | `25` | `backend_runtime` | Interactive call timeout.; range: `3`..`300` |
| `HELPER_MODEL_TIMEOUT_BACKGROUND_SEC` | `int` | `45` | `backend_runtime` | Background call timeout.; range: `3`..`600` |
| `HELPER_MODEL_TIMEOUT_MAINTENANCE_SEC` | `int` | `60` | `backend_runtime` | Maintenance call timeout.; range: `3`..`600` |
| `HELPER_MODEL_FAST` | `string` | none | `backend_runtime` | Optional fast-model override / fallback route. |
| `HELPER_MODEL_REASONING` | `string` | none | `backend_runtime` | Optional reasoning-model override / fallback route. |
| `HELPER_MODEL_LONG_CONTEXT` | `string` | none | `backend_runtime` | Optional long-context reasoning model override. |
| `HELPER_MODEL_DEEP_REASONING` | `string` | none | `backend_runtime` | Optional deep-reasoning model override. |
| `HELPER_MODEL_VERIFIER` | `string` | none | `backend_runtime` | Optional verifier / critic model override for reasoning checks. |
| `HELPER_MODEL_CRITIC` | `string` | none | `backend_runtime` | Optional critic-model override / fallback route. |
| `HELPER_MODEL_SAFE_FALLBACK` | `string` | none | `backend_runtime` | Optional safe fallback model name. |

## Persistence

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_CONVERSATION_PERSIST_FLUSH_MS` | `int` | `1500` | `backend_runtime` | Write-behind flush delay.; range: `0`..`30000` |
| `HELPER_CONVERSATION_JOURNAL_COMPACTION_THRESHOLD` | `int` | `25` | `backend_runtime` | Journal compaction threshold.; range: `5`..`500` |
| `HELPER_PERSISTENCE_QUEUE_CAPACITY` | `int` | `1024` | `backend_runtime` | Write-behind queue capacity.; range: `64`..`16384` |
| `HELPER_PERSISTENCE_QUEUE_BATCH_SIZE` | `int` | `32` | `backend_runtime` | Write-behind batch size.; range: `1`..`1024` |
| `HELPER_PERSISTENCE_QUEUE_BACKLOG_ALERT` | `int` | `128` | `backend_runtime` | Persistence backlog alert threshold.; range: `8`..`16384` |
| `HELPER_PERSISTENCE_LAG_ALERT_MS` | `int` | `10000` | `backend_runtime` | Persistence lag alert threshold.; range: `250`..`300000` |

## Post-Turn Audit

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_POST_TURN_AUDIT_QUEUE_CAPACITY` | `int` | `512` | `backend_runtime` | Audit queue capacity.; range: `64`..`8192` |
| `HELPER_POST_TURN_AUDIT_TIMEOUT_SEC` | `int` | `8` | `backend_runtime` | Audit worker timeout.; range: `2`..`120` |
| `HELPER_POST_TURN_AUDIT_MAX_ATTEMPTS` | `int` | `2` | `backend_runtime` | Maximum audit retry attempts.; range: `1`..`10` |
| `HELPER_POST_TURN_AUDIT_BACKLOG_ALERT` | `int` | `96` | `backend_runtime` | Audit backlog alert threshold.; range: `8`..`8192` |
| `HELPER_POST_TURN_AUDIT_FAILURE_ALERT` | `double` | `0.20` | `backend_runtime` | Audit failure-rate alert threshold.; range: `0.01`..`1.0` |

## Research

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_RESEARCH_ENABLED` | `bool` | `true` | `backend_runtime` | Enable research routing. |
| `HELPER_RESEARCH_CACHE_TTL_MINUTES` | `int` | `20` | `backend_runtime` | Research cache TTL in minutes.; range: `1`..`720` |
| `HELPER_GROUNDING_CASUAL_CHAT_ENABLED` | `bool` | `false` | `backend_runtime` | Allow grounding for casual chat. |
| `HELPER_RESEARCH_MAX_SOURCES` | `int` | `8` | `backend_runtime` | Maximum grounded sources per response.; range: `1`..`64` |
| `HELPER_RESPONSE_SHOW_LOCAL_PATHS` | `bool` | `false` | `backend_runtime` | Expose absolute local library paths in local source labels. Defaults to false so public proof artifacts remain path-redacted. |
| `HELPER_LOCAL_LIBRARY_PUBLIC_SOURCE_MODE` | `enum` | `redacted` | `backend_runtime` | Local-library source rendering mode for public artifacts.; allowed: `redacted`, `developer` |
| `HELPER_LOCAL_LIBRARY_ALLOW_CURRENT_FACTS` | `bool` | `false` | `backend_runtime` | Allow explicitly trusted local-library documents to support current external facts. Defaults to false; web remains required for fresh/regulatory claims. |
| `HELPER_LOCAL_LIBRARY_CURRENT_FACT_ALLOWLIST` | `csv` | none | `backend_runtime` | Optional stable source IDs allowed by operator policy to support current-fact claims. |
| `HELPER_MIXED_EVIDENCE_REQUIRE_WEB_FOR_FRESH` | `bool` | `true` | `backend_runtime` | Require web evidence for freshness-sensitive claims even when local library evidence is available. |
| `HELPER_RESEARCH_BACKGROUND_BUDGET` | `int` | `1` | `backend_runtime` | Background research parallelism budget.; range: `0`..`16` |
| `HELPER_WEB_SEARCH_LOCAL_URL` | `url` | `http://localhost:8080` | `backend_runtime` | Primary local web-search endpoint used by the local provider adapter. |
| `HELPER_WEB_SEARCH_SEARX_URL` | `url` | none | `backend_runtime` | Optional secondary Searx-compatible endpoint used for graceful failover. |
| `HELPER_WEB_SEARCH_PROVIDER_ORDER` | `csv` | `local,searx` | `backend_runtime` | Ordered provider IDs for web-search failover. Supported values currently include `local,searx`. |
| `HELPER_WEB_SEARCH_PROVIDER_TIMEOUT_SEC` | `int` | `4` | `backend_runtime` | Per-provider web-search timeout before mux failover.; range: `1`..`30` |
| `HELPER_WEB_SEARCH_COST_BUDGET_UNITS` | `int` | `3` | `backend_runtime` | Base cost budget units for provider selection per search turn before policy adjustments by search mode.; range: `0`..`8` |
| `HELPER_WEB_SEARCH_LATENCY_BUDGET_MS` | `int` | `3500` | `backend_runtime` | Base total latency budget for provider selection per search turn before policy adjustments by search mode.; range: `250`..`30000` |
| `HELPER_WEB_SEARCH_PROVIDER_COOLDOWN_SEC` | `int` | `45` | `backend_runtime` | Cooldown window applied after consecutive provider timeouts or errors trip health thresholds.; range: `5`..`600` |
| `HELPER_WEB_SEARCH_PROVIDER_MAX_CONSECUTIVE_TIMEOUTS` | `int` | `2` | `backend_runtime` | Consecutive timeout threshold before a provider enters cooldown.; range: `1`..`10` |
| `HELPER_WEB_SEARCH_PROVIDER_MAX_CONSECUTIVE_ERRORS` | `int` | `2` | `backend_runtime` | Consecutive error threshold before a provider enters cooldown.; range: `1`..`10` |
| `HELPER_WEB_SEARCH_PROVIDER_SLOW_LATENCY_MS` | `int` | `1200` | `backend_runtime` | Rolling latency threshold after which a provider is considered degraded for selection scoring.; range: `100`..`30000` |
| `HELPER_WEB_SEARCH_MAX_ITERATIONS` | `int` | `3` | `backend_runtime` | Maximum iterative web-search passes per request.; range: `1`..`3` |
| `HELPER_WEB_FETCH_MAX_REDIRECTS` | `int` | `3` | `backend_runtime` | Maximum redirect hops allowed during provider/page fetch before the request is blocked.; range: `0`..`5` |
| `HELPER_WEB_FETCH_USE_PROXY` | `bool` | `false` | `backend_runtime` | Whether outbound page/document fetches should honor the system proxy configuration. Defaults to false to avoid broken local proxy interception in offline/dev runtimes. |
| `HELPER_WEB_PAGE_FETCH_TIMEOUT_SEC` | `int` | `6` | `backend_runtime` | Per-page fetch timeout for full-page evidence retrieval.; range: `1`..`20` |
| `HELPER_WEB_PAGE_MAX_BYTES` | `int` | `400000` | `backend_runtime` | Maximum bytes admitted for a single fetched page before extraction is aborted.; range: `16384`..`2000000` |
| `HELPER_WEB_PAGE_MAX_FETCHES_PER_SEARCH` | `int` | `3` | `backend_runtime` | Maximum fetched pages per search session after search hits are selected.; range: `1`..`6` |
| `HELPER_WEB_RENDER_ENABLED` | `bool` | `true` | `backend_runtime` | Enables isolated browser-render fallback for JS-heavy pages after normal HTTP extraction is insufficient. |
| `HELPER_WEB_RENDER_MAX_PAGES_PER_SEARCH` | `int` | `1` | `backend_runtime` | Maximum pages per search session that may use browser-render fallback.; range: `0`..`3` |
| `HELPER_WEB_RENDER_TIMEOUT_SEC` | `int` | `8` | `backend_runtime` | Timeout for a single browser-render fallback page load.; range: `2`..`20` |
| `HELPER_WEB_RENDER_MAX_HTML_CHARS` | `int` | `300000` | `backend_runtime` | Maximum rendered HTML characters retained from browser-render fallback before extraction.; range: `16384`..`1000000` |
| `HELPER_WEB_EVIDENCE_GENERAL_STALE_MINUTES` | `int` | `20` | `backend_runtime` | Maximum age before general web evidence is treated as stale and refreshed or disclosed.; range: `1`..`1440` |
| `HELPER_WEB_EVIDENCE_VOLATILE_STALE_MINUTES` | `int` | `30` | `backend_runtime` | Maximum age before volatile categories such as finance/news/weather are treated as stale.; range: `1`..`240` |
| `HELPER_WEB_EVIDENCE_SOFTWARE_STALE_MINUTES` | `int` | `360` | `backend_runtime` | Maximum age before software-version or release evidence is treated as stale.; range: `5`..`10080` |

## Transport

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_STREAM_HEARTBEAT_SECONDS` | `int` | `10` | `backend_runtime` | SSE heartbeat interval in seconds.; range: `2`..`60` |
| `HELPER_STREAM_DEADLINE_SECONDS` | `int` | `90` | `backend_runtime` | SSE request deadline in seconds.; range: `10`..`1800` |
| `HELPER_STREAM_HEARTBEAT_MS` | `int` | none | `compatibility` | Legacy raw heartbeat interval override in milliseconds.; deprecated -> `HELPER_STREAM_HEARTBEAT_SECONDS` |

## Performance Budgets

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_LISTEN_BUDGET_MS` | `int` | `5000` | `backend_runtime` | Startup listen budget.; range: `500`..`60000` |
| `HELPER_READINESS_BUDGET_MS` | `int` | `30000` | `backend_runtime` | Startup readiness budget.; range: `1000`..`300000` |
| `HELPER_FIRST_TOKEN_BUDGET_MS` | `int` | `1200` | `backend_runtime` | First-token latency budget.; range: `100`..`60000` |
| `HELPER_P95_FULL_TURN_BUDGET_MS` | `int` | `4000` | `backend_runtime` | P95 full-turn latency budget.; range: `250`..`300000` |

## Runtime Policies

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_POLICY_RESEARCH_ENABLED` | `bool` | `true` | `backend_runtime` | Policy gate for research routing. |
| `HELPER_POLICY_GROUNDING_ENABLED` | `bool` | `true` | `backend_runtime` | Policy gate for grounding. |
| `HELPER_POLICY_SYNC_CRITIC_ENABLED` | `bool` | `true` | `backend_runtime` | Policy gate for synchronous critic execution. |
| `HELPER_POLICY_ASYNC_AUDIT_ENABLED` | `bool` | `true` | `backend_runtime` | Policy gate for asynchronous post-turn audit. |
| `HELPER_SHADOW_MODE_ENABLED` | `bool` | `false` | `backend_runtime` | Enable shadow-mode execution paths. |
| `HELPER_POLICY_SAFE_FALLBACK_ONLY` | `bool` | `false` | `backend_runtime` | Restrict backend to safe fallback responses only. |

## Knowledge And Indexing

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_MODEL_CODER` | `string` | none | `backend_runtime` | Optional coder-model route. |
| `HELPER_MODEL_VISION` | `string` | none | `backend_runtime` | Optional vision-model route. |
| `HELPER_PDF_VISION_GHOSTSCRIPT_PATH` | `path` | none | `backend_runtime` | Explicit Ghostscript executable for PDF-to-image vision extraction. |
| `HELPER_PDF_VISION_GHOSTSCRIPT_DPI` | `int` | `200` | `backend_runtime` | Ghostscript raster DPI for PDF vision fallback.; range: `96`..`600` |
| `HELPER_PDF_VISION_GHOSTSCRIPT_JPEG_QUALITY` | `int` | `80` | `backend_runtime` | Ghostscript JPEG quality for PDF vision fallback images.; range: `40`..`95` |
| `HELPER_VISION_OCR_MAX_IMAGE_BYTES` | `int` | `12582912` | `backend_runtime` | Maximum encoded image bytes accepted by vision OCR preparation.; range: `1024`..`67108864` |
| `HELPER_VISION_OCR_TIMEOUT_SEC` | `int` | `90` | `backend_runtime` | Vision OCR extraction timeout in seconds.; range: `10`..`600` |
| `HELPER_INDEX_PIPELINE_VERSION` | `string` | `v1` | `script_runtime` | Active library indexing pipeline version. |
| `HELPER_RAG_ALLOW_V1_FALLBACK` | `bool` | `true` | `script_runtime` | Allow retrieval fallback to legacy v1 collections. |
| `HELPER_INDEX_EXCLUDED_EXTENSIONS` | `csv` | none | `script_runtime` | Extensions excluded from ordered/reset indexing scripts and synthetic learning scans. |
| `HELPER_INDEX_EXCLUDED_FILES` | `csv` | none | `script_runtime` | Explicit file paths excluded from ordered/reset indexing scripts and synthetic learning scans. |
| `HELPER_INDEX_MAX_CHUNKS_PER_DOCUMENT` | `int` | `1600` | `backend_runtime` | Default max chunks per document for v2 indexing.; range: `128`..`64000` |
| `HELPER_INDEX_MAX_CHUNKS_PER_DOCUMENT_LARGE_REFERENCE` | `int` | `24000` | `backend_runtime` | Max chunks for large reference documents.; range: `1600`..`120000` |
| `HELPER_INDEX_LARGE_REFERENCE_MIN_PAGES` | `int` | `400` | `backend_runtime` | Minimum page count to treat a document as a large reference.; range: `64`..`10000` |
| `HELPER_INDEX_MAX_CHUNKS_PER_DOCUMENT_LARGE_DOCUMENT` | `int` | `12000` | `backend_runtime` | Max chunks for large-document indexing mode.; range: `1600`..`120000` |
| `HELPER_INDEX_LARGE_DOCUMENT_MIN_PAGES` | `int` | `250` | `backend_runtime` | Minimum pages for large-document mode.; range: `64`..`10000` |
| `HELPER_INDEX_LARGE_DOCUMENT_MIN_BLOCKS` | `int` | `400` | `backend_runtime` | Minimum block count for large-document mode.; range: `64`..`100000` |
| `HELPER_INDEX_LARGE_DOCUMENT_MIN_OBSERVED_CHUNKS` | `int` | `1700` | `backend_runtime` | Minimum observed chunk count before forcing large-document mode.; range: `1601`..`120000` |
| `HELPER_ENABLE_AUTONOMOUS_EVOLUTION_AUTOSTART` | `bool` | `false` | `backend_runtime` | Autostart autonomous evolution on maintenance startup. |

## Frontend Local

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `VITE_HELPER_API_PROTOCOL` | `string` | `http` | `frontend_local` | Frontend API protocol when VITE_HELPER_API_BASE is not set. |
| `VITE_HELPER_API_HOST` | `string` | `localhost` | `frontend_local` | Frontend API host when VITE_HELPER_API_BASE is not set. |
| `VITE_HELPER_API_PORT` | `string` | `5000` | `frontend_local` | Frontend API port when VITE_HELPER_API_BASE is not set. |
| `VITE_HELPER_API_BASE` | `url` | none | `frontend_local` | Explicit frontend API base URL. |
| `VITE_HELPER_SESSION_SCOPES_CONVERSATION` | `csv` | none | `frontend_local` | Optional conversation surface scope override. |
| `VITE_HELPER_SESSION_SCOPES_RUNTIME_CONSOLE` | `csv` | none | `frontend_local` | Optional runtime-console surface scope override. |
| `VITE_HELPER_SESSION_SCOPES_BUILDER` | `csv` | none | `frontend_local` | Optional builder surface scope override. |
| `VITE_HELPER_SESSION_SCOPES_EVOLUTION` | `csv` | none | `frontend_local` | Optional evolution surface scope override. |
| `VITE_API_BASE` | `url` | none | `compatibility` | Legacy frontend API base variable.; deprecated -> `VITE_HELPER_API_BASE` |
| `VITE_HELPER_SESSION_SCOPES` | `csv` | none | `compatibility` | Legacy single-surface scope override key.; deprecated -> `VITE_HELPER_SESSION_SCOPES_<SURFACE>` |

## Operator And CI Scripts

| Name | Type | Default | Scope | Notes |
| --- | --- | --- | --- | --- |
| `HELPER_RUNTIME_SMOKE_API_BASE` | `url` | none | `script_runtime` | API base URL used by runtime smoke and CI gates. |
| `HELPER_RUNTIME_SMOKE_UI_URL` | `url` | none | `script_runtime` | UI URL used by runtime UI perf smoke. |
| `HELPER_NUGET_SECURITY_GATE_MODE` | `string` | `unset` | `script_runtime` | Overrides the NuGet security gate execution mode used by ci_gate.; allowed: `strict-online`, `best-effort-local`; When unset, scripts/ci_gate.ps1 derives strict-online only when CI=true and otherwise uses best-effort-local. |
| `HELPER_REMEDIATION_LOCK` | `bool` | `unset` | `script_runtime` | Explicit remediation freeze guard. CI expects `1` when freeze is active. |

## Local Template Rules

1. `.env.local.example` is generated from the same inventory and must not grow ad hoc keys.
2. Deprecated keys stay out of the local template even if runtime still accepts them for compatibility.
3. Surface-specific browser scopes use `VITE_HELPER_SESSION_SCOPES_<SURFACE>`; the legacy single `VITE_HELPER_SESSION_SCOPES` key is deprecated.

## Governed Script Files

- `scripts/ci_gate.ps1`
- `scripts/check_backend_control_plane.ps1`
- `scripts/check_latency_budget.ps1`
- `scripts/check_remediation_freeze.ps1`
- `scripts/ui_perf_regression.ps1`
- `scripts/run_ui_perf_live.ps1`
- `scripts/cutover_to_v2.ps1`
- `scripts/reset_library_index_safe.ps1`
- `scripts/monitor_library_indexing_supervisor.ps1`
- `scripts/write_chunking_post_cutover_validation.ps1`
- `scripts/run_ordered_library_indexing.ps1`
- `scripts/run_v2_pilot_reindex.ps1`

