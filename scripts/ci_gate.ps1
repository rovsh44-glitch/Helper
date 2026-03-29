$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-CiStep {
    param(
        [string]$Name,
        [scriptblock]$Command
    )

    Write-Host "[CI Gate] $Name..."
    $global:LASTEXITCODE = 0
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "[CI Gate] Step failed: $Name (exit code: $LASTEXITCODE)."
    }
}

Invoke-CiStep "Secret scan" {
    powershell -ExecutionPolicy Bypass -File scripts/secret_scan.ps1
}

Invoke-CiStep "Remediation freeze" {
    powershell -ExecutionPolicy Bypass -File scripts/check_remediation_freeze.ps1
}

Invoke-CiStep "Root layout" {
    powershell -ExecutionPolicy Bypass -File scripts/check_root_layout.ps1
}

Invoke-CiStep "Config governance" {
    powershell -ExecutionPolicy Bypass -File scripts/check_env_governance.ps1
}

Invoke-CiStep "R&D governance" {
    powershell -ExecutionPolicy Bypass -File scripts/check_rd_governance.ps1
}

Invoke-CiStep "Execution step closure" {
    powershell -ExecutionPolicy Bypass -File scripts/check_execution_step_closure.ps1
}

Invoke-CiStep "Docs entrypoints" {
    powershell -ExecutionPolicy Bypass -File scripts/check_docs_entrypoints.ps1
}

Invoke-CiStep "Trailing-space directories" {
    powershell -ExecutionPolicy Bypass -File scripts/check_trailing_space_dirs.ps1
}

Invoke-CiStep "UI API consistency" {
    powershell -ExecutionPolicy Bypass -File scripts/check_ui_api_usage.ps1
}

Invoke-CiStep "Frontend architecture" {
    powershell -ExecutionPolicy Bypass -File scripts/check_frontend_architecture.ps1 -SkipApiBoundary
}

Invoke-CiStep "NuGet security gate" {
    powershell -ExecutionPolicy Bypass -File scripts/nuget_security_gate.ps1
}

Invoke-CiStep "Build" {
    dotnet build Helper.sln
}

Invoke-CiStep "Tests" {
    & (Join-Path $PSScriptRoot "run_dotnet_test_batched.ps1") `
        -Target "test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj" `
        -BaseArguments @("--no-build", "--blame-hang", "--blame-hang-timeout", "2m") `
        -LogPath "temp/verification/ci_gate/dotnet_test.log" `
        -ErrorLogPath "temp/verification/ci_gate/dotnet_test.stderr.log" `
        -StatusPath "temp/verification/ci_gate/dotnet_test_status.json" `
        -ResultsRoot "temp/verification/ci_gate/batches" `
        -MaxDurationSec 900 `
        -ListTimeoutSec 300
}

Invoke-CiStep "Eval" {
    powershell -ExecutionPolicy Bypass -File scripts/run_eval_gate.ps1
}

Invoke-CiStep "OpenAPI contract" {
    powershell -ExecutionPolicy Bypass -File scripts/openapi_gate.ps1
}

Invoke-CiStep "Generated client diff" {
    powershell -ExecutionPolicy Bypass -File scripts/generated_client_diff_gate.ps1
}

Invoke-CiStep "Monitoring config" {
    powershell -ExecutionPolicy Bypass -File scripts/monitoring_gate.ps1
}

Invoke-CiStep "Load/Chaos smoke" {
    powershell -ExecutionPolicy Bypass -File scripts/run_load_chaos_smoke.ps1
}

Invoke-CiStep "Tool benchmark" {
    powershell -ExecutionPolicy Bypass -File scripts/run_tool_benchmark.ps1
}

Invoke-CiStep "Generation parity gate" {
    powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_gate.ps1
}

Invoke-CiStep "Generation parity benchmark" {
    powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_benchmark.ps1
}

Invoke-CiStep "Generation parity window gate" {
    powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_window_gate.ps1 -WindowDays 7
}

Invoke-CiStep "Frontend build" {
    & (Join-Path $PSScriptRoot "build_frontend_verification.ps1") -RequireRebuild
}

Invoke-CiStep "Bundle budget" {
    powershell -ExecutionPolicy Bypass -File scripts/check_bundle_budget.ps1
}

Invoke-CiStep "Control-plane thresholds" {
    powershell -ExecutionPolicy Bypass -File scripts/check_backend_control_plane.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE
}

Invoke-CiStep "Latency budget" {
    powershell -ExecutionPolicy Bypass -File scripts/check_latency_budget.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE
}

Invoke-CiStep "UI workflow smoke" {
    $uiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
    powershell -ExecutionPolicy Bypass -File scripts/run_ui_workflow_smoke.ps1 -RequireConfiguredRuntime -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE -UiUrl $uiUrl
}

Invoke-CiStep "UI perf regression" {
    $uiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
    powershell -ExecutionPolicy Bypass -File scripts/ui_perf_regression.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE -UiUrl $uiUrl
}

Invoke-CiStep "Release baseline capture" {
    $uiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
    powershell -ExecutionPolicy Bypass -File scripts/baseline_capture.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE -UiUrl $uiUrl -SkipUiSmoke -RefreshActiveGateSnapshot
}

Write-Host "[CI Gate] All checks passed."

