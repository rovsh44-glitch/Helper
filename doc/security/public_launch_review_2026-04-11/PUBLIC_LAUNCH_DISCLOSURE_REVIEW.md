# Public Launch Disclosure Review

Generated: 2026-04-11T05:50:55.6392730+00:00
Policy version: 2026-03-24
Publication model: private_core_plus_public_showcase

## Verdict

- Verdict: BLOCK_DIRECT_PUBLISH
- Direct publish allowed: NO
- Candidate files reviewed: 1419
- Public-safe files: 14
- Private-only files: 1387
- Review-required files: 18

## Hygiene Gates

- Secret scan exit code: 0
- Secret hits: 0
- Root layout exit code: 0
- Root violations: 0
- Source fatal violations: 0
- Source warnings: 0

## Review Notes

- Candidate files contain private-core or operator-only surfaces that are outside the public showcase allowlist.
- Candidate files remain under default-deny review and are not approved for public publication.

## Public-Safe Sample

- .github/ISSUE_TEMPLATE/apply-as-blind-reviewer.yml
- .github/ISSUE_TEMPLATE/config.yml
- .github/ISSUE_TEMPLATE/request-demo-or-contact.yml
- .github/ISSUE_TEMPLATE/submit-web-research-prompt.yml
- CONTACT.md
- CONTRIBUTING.md
- docs/architecture-overview.md
- docs/one-pager.md
- docs/product-roadmap.md
- docs/use-cases.md
- FAQ.md
- media/README.md
- README.md
- SECURITY.md

## Private-Only Sample

- .env.local.example
- .gitignore
- .ignore
- App.tsx
- components/capabilityCoveragePanel.module.css
- components/CapabilityCoveragePanel.tsx
- components/DiffViewer.tsx
- components/FileTree.tsx
- components/GoalPanel.tsx
- components/HumanLikeConversationDashboardPanel.tsx
- components/IndexingPanel.tsx
- components/layout/PanelResizeHandle.tsx
- components/ResearchCard.tsx
- components/runtime-console/runtimeConsole.module.css
- components/runtime-console/RuntimeConsoleLogsPanel.tsx
- components/runtime-console/RuntimeConsolePresentation.tsx
- components/runtime-console/RuntimeConsoleSidebar.tsx
- components/runtime-console/RuntimeConsoleTelemetryDeck.tsx
- components/settings/SettingsAlertsPanel.tsx
- components/settings/SettingsConversationStylePanel.tsx
- components/settings/SettingsInfrastructurePanel.tsx
- components/settings/SettingsMemoryItemsPanel.tsx
- components/settings/SettingsMemoryPolicyPanel.tsx
- components/settings/SettingsPersonalizationPanel.tsx
- components/settings/SettingsProjectContextPanel.tsx
- components/settings/SettingsProviderProfilesPanel.tsx
- components/settings/SettingsRuntimeDoctorPanel.tsx
- components/settings/SettingsSecurityPanel.tsx
- components/settings/SettingsViewHeader.tsx
- components/Sidebar.tsx
- components/StrategicMindMap.tsx
- components/ThoughtStream.tsx
- components/ThoughtTree.tsx
- components/views/ActiveStreamingMessage.tsx
- components/views/AssistantDiagnosticsDeck.tsx
- components/views/BuilderNodeSheet.tsx
- components/views/BuilderProjectLauncher.tsx
- components/views/BuilderView.tsx
- components/views/BuilderWorkspaceContent.tsx
- components/views/BuilderWorkspaceSidebar.tsx

## Review-Required Sample

- .github/workflows/repo-gate.yml
- .github/workflows/runtime-test-lanes.yml
- LICENSE
- slice/runtime-review/openapi/.gitkeep
- slice/runtime-review/openapi/runtime-review-openapi.json
- slice/runtime-review/README.md
- slice/runtime-review/sample_data/evolution_status.json
- slice/runtime-review/sample_data/indexing_queue.json
- slice/runtime-review/sample_data/logs/indexing-worker.log
- slice/runtime-review/sample_data/logs/runtime-main.log
- slice/runtime-review/sample_data/readiness.json
- slice/runtime-review/sample_data/README.md
- slice/runtime-review/sample_data/route_telemetry.jsonl
- slice/runtime-review/web/index.html
- slice/runtime-review/web/src/App.tsx
- slice/runtime-review/web/src/main.tsx
- slice/runtime-review/web/src/styles.css
- vite.runtime-slice.config.ts

## Top-Level Summary

- src total=721 public_safe=0 private_only=721 review_required=0
- test total=202 public_safe=0 private_only=202 review_required=0
- scripts total=180 public_safe=0 private_only=180 review_required=0
- doc total=124 public_safe=0 private_only=124 review_required=0
- components total=51 public_safe=0 private_only=51 review_required=0
- hooks total=28 public_safe=0 private_only=28 review_required=0
- services total=23 public_safe=0 private_only=23 review_required=0
- slice total=17 public_safe=0 private_only=3 review_required=14
- mcp_config total=6 public_safe=0 private_only=6 review_required=0
- .github total=6 public_safe=4 private_only=0 review_required=2
- contexts total=5 public_safe=0 private_only=5 review_required=0
- docs total=4 public_safe=4 private_only=0 review_required=0
- utils total=4 public_safe=0 private_only=4 review_required=0
- modules total=3 public_safe=0 private_only=3 review_required=0
- postcss.config.cjs total=1 public_safe=0 private_only=1 review_required=0
- metadata.json total=1 public_safe=0 private_only=1 review_required=0
- README.md total=1 public_safe=1 private_only=0 review_required=0
- prune_queue.ps1 total=1 public_safe=0 private_only=1 review_required=0
- monitor_indexing.ps1 total=1 public_safe=0 private_only=1 review_required=0
- package.json total=1 public_safe=0 private_only=1 review_required=0
- personality.json total=1 public_safe=0 private_only=1 review_required=0
- package-lock.json total=1 public_safe=0 private_only=1 review_required=0
- types.ts total=1 public_safe=0 private_only=1 review_required=0
- tsconfig.json total=1 public_safe=0 private_only=1 review_required=0
- vite.config.ts total=1 public_safe=0 private_only=1 review_required=0
- vite.shared.config.mjs total=1 public_safe=0 private_only=1 review_required=0
- vite.runtime-slice.config.ts total=1 public_safe=0 private_only=0 review_required=1
- tailwind.config.cjs total=1 public_safe=0 private_only=1 review_required=0
- Run_Helper_Integrated.bat total=1 public_safe=0 private_only=1 review_required=0
- Run_Helper.bat total=1 public_safe=0 private_only=1 review_required=0
- searxng total=1 public_safe=0 private_only=1 review_required=0
- vite-env.d.ts total=1 public_safe=0 private_only=1 review_required=0
- SECURITY.md total=1 public_safe=1 private_only=0 review_required=0
- Directory.Build.rsp total=1 public_safe=0 private_only=1 review_required=0
- Directory.Build.props total=1 public_safe=0 private_only=1 review_required=0
- delete_format_duplicates.ps1 total=1 public_safe=0 private_only=1 review_required=0
- eval_live_monitor.json total=1 public_safe=0 private_only=1 review_required=0
- docker-compose.yml total=1 public_safe=0 private_only=1 review_required=0
- Directory.Build.targets total=1 public_safe=0 private_only=1 review_required=0
- delete_duplicates.ps1 total=1 public_safe=0 private_only=1 review_required=0
- .ignore total=1 public_safe=0 private_only=1 review_required=0
- .gitignore total=1 public_safe=0 private_only=1 review_required=0
- .env.local.example total=1 public_safe=0 private_only=1 review_required=0
- CONTRIBUTING.md total=1 public_safe=1 private_only=0 review_required=0
- CONTACT.md total=1 public_safe=1 private_only=0 review_required=0
- App.tsx total=1 public_safe=0 private_only=1 review_required=0
- index.html total=1 public_safe=0 private_only=1 review_required=0
- index.css total=1 public_safe=0 private_only=1 review_required=0
- Helper.sln total=1 public_safe=0 private_only=1 review_required=0
- media total=1 public_safe=1 private_only=0 review_required=0
- LICENSE total=1 public_safe=0 private_only=0 review_required=1
- index.tsx total=1 public_safe=0 private_only=1 review_required=0
- find_redundant_folders.ps1 total=1 public_safe=0 private_only=1 review_required=0
- find_duplicates_all.ps1 total=1 public_safe=0 private_only=1 review_required=0
- find_duplicates.ps1 total=1 public_safe=0 private_only=1 review_required=0
- FAQ.md total=1 public_safe=1 private_only=0 review_required=0
- find_duplicates_by_size.ps1 total=1 public_safe=0 private_only=1 review_required=0
- find_duplicates_by_name.ps1 total=1 public_safe=0 private_only=1 review_required=0
- find_duplicates_by_basename.ps1 total=1 public_safe=0 private_only=1 review_required=0
