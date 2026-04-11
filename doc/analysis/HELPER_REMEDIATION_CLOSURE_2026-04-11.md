# HELPER Remediation Closure

Дата: `2026-04-11`
Статус: `implemented`
Основание:

1. `doc/analysis/HELPER_CRITICAL_ANALYSIS_2026-04-10.md`
2. `doc/analysis/HELPER_REMEDIATION_PLAN_2026-04-10.md`
3. `doc/analysis/HELPER_REMEDIATION_BACKLOG_2026-04-10.md`

## Что закрыто

### 1. Config governance и generated artifacts

Исправлено:

1. `doc/config/ENV_REFERENCE.md` и `doc/config/ENV_INVENTORY.json` синхронизированы с `BackendEnvironmentInventory.cs`.
2. Inventory расширен для `HELPER_NUGET_SECURITY_GATE_MODE`, чтобы `scripts/ci_gate.ps1` больше не ссылался на unknown governed variable.
3. `check_env_governance.ps1` снова даёт честный зелёный сигнал.

### 2. Canonical solution build

Исправлено:

1. `Helper.sln` больше не скрывает test projects из canonical build matrix.
2. Исправлены broken test-project includes в certification lane.
3. Для script-level solution build зафиксирован deterministic path `dotnet build Helper.sln -m:1` в `scripts/ci_gate.ps1` и `scripts/run_generation_parity_nightly.ps1`.

### 3. Secret scan

Исправлено:

1. `scripts/secret_scan.ps1` поддерживает `workspace` и `repo` режимы.
2. Scanner ловит unquoted `.env` secrets для `HELPER_API_KEY` и `HELPER_SESSION_SIGNING_KEY`.
3. Policy и wrappers синхронизированы между `ci_gate`, runtime promotion scripts, `package.json`, `README.md` и security docs.
4. Добавлен regression harness: `SecretScanScriptTests`.

### 4. Provider runtime

Исправлено:

1. Введён runtime contract `AiProviderRuntimeSettings` и явный `AiTransportKind`.
2. `AILink` теперь применяет runtime snapshot и действительно переключает transport path.
3. `OpenAI-Compatible` получил отдельные пути для `/models`, `/chat/completions`, `/embeddings` и SSE parsing.
4. `ProviderProfileResolver` больше не использует process-wide env mutation как functional routing mechanism.
5. `HelperModelGateway` учитывает active provider bindings.
6. DI wiring возвращено в composition root.
7. Добавлены:
   - `AILinkProviderRuntimeTests`
   - `ProviderProfileActivationServiceTests`
   - `ProviderProfileEndpointRuntimeIntegrationTests`
   - provider/service registration assertions
8. Архитектурное решение зафиксировано в `doc/adr/ADR_PROVIDER_RUNTIME_SWITCHING.md`.

### 5. NuGet security gate

Исправлено:

1. `scripts/nuget_security_gate.ps1` различает:
   - `audit_passed`
   - `audit_failed_vulnerabilities_found`
   - `audit_failed_infrastructure_unavailable`
   - `audit_degraded_infrastructure_unavailable`
   - `audit_failed_command_error`
2. Появились execution modes `strict-online` и `best-effort-local`.
3. Добавлены proxy/source diagnostics и JSON report output.
4. Добавлен regression harness: `NugetSecurityGateScriptTests`.

### 6. Governance artifacts, требуемые `ci:gate`

Добавлено и восстановлено:

1. `doc/archive/comparative/HELPER_EXECUTION_ORDER_TABLE_LFL300_2026-03-22.md`
2. `doc/archive/comparative/HELPER_EXECUTION_DASHBOARD_LFL300_2026-03-22.md`
3. `doc/archive/comparative/HELPER_EXECUTION_STEP_CLOSURE_LFL300_2026-03-23.json`
4. `doc/archive/comparative/HELPER_EXECUTION_STEP_CLOSURE_LFL300_2026-03-23.md`
5. Явная ссылка на `doc/research/README.md` в `doc/README.md`

Это вернуло зелёный:

1. `scripts/check_rd_governance.ps1`
2. `scripts/check_execution_step_closure.ps1`
3. `scripts/check_docs_entrypoints.ps1`

## Верификация

Пройдено:

1. `dotnet build Helper.sln -m:1`
2. `dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj --no-build --blame-hang --blame-hang-timeout 2m`
3. `dotnet test test/Helper.Runtime.Api.Tests/Helper.Runtime.Api.Tests.csproj --no-build`
4. `dotnet test test/Helper.RuntimeSlice.Api.Tests/Helper.RuntimeSlice.Api.Tests.csproj --no-build`
5. `npm run build`
6. `powershell -ExecutionPolicy Bypass -File scripts/check_env_governance.ps1`
7. `powershell -ExecutionPolicy Bypass -File scripts/check_rd_governance.ps1`
8. `powershell -ExecutionPolicy Bypass -File scripts/check_execution_step_closure.ps1`
9. `powershell -ExecutionPolicy Bypass -File scripts/secret_scan.ps1 -ScanMode repo`
10. `powershell -ExecutionPolicy Bypass -File scripts/nuget_security_gate.ps1`

Observed results:

1. `Helper.Runtime.Tests`: `202/202 PASS`
2. `Helper.Runtime.Api.Tests`: `171/171 PASS`
3. `Helper.RuntimeSlice.Api.Tests`: `4/4 PASS`
4. `nuget_security_gate.ps1` в локальном offline/proxy окружении возвращает ожидаемый `audit_degraded_infrastructure_unavailable`, а не ложный security failure.

## Статус полного `ci:gate`

`npm run ci:gate` после remediation проходит:

1. secret/config/docs/governance stages
2. deterministic solution build
3. batched runtime tests
4. eval/openapi/generated-client/monitoring stages
5. load-chaos и tool benchmark stages

Текущая точка остановки:

1. `Generation parity gate`

Причина остановки:

1. Canonical parity truth-state в репозитории остаётся недостаточным для KPI gate:
   - `GoldenSampleInsufficient 0 < 20`
   - `GenerationSuccessRate 0.00 % < 95.00 %`
2. Это подтверждено артефактом `doc/HELPER_PARITY_GATE_2026-04-11_01-34-39.md`.
3. Это не defect remediation layer, а честный product-evidence blocker, уже отражённый в `doc/CURRENT_STATE.md`.

## Итог

Remediation backlog реализован полностью на уровне кода, тестов, runtime architecture и governance surface.

Единственный remaining red path в полном `ci:gate` относится к parity evidence readiness и не должен “чиниться” ослаблением gate или подменой canonical truth.
