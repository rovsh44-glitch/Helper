$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-CiHeavyStep {
    param(
        [string]$Name,
        [scriptblock]$Command
    )

    Write-Host "[CI Heavy] $Name..."
    $global:LASTEXITCODE = 0
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "[CI Heavy] Step failed: $Name (exit code: $LASTEXITCODE)."
    }
}

Invoke-CiHeavyStep "Load/Chaos smoke" {
    powershell -ExecutionPolicy Bypass -File scripts/run_load_chaos_smoke.ps1 -Configuration Debug
}

Invoke-CiHeavyStep "Generation parity gate" {
    powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_gate.ps1
}

Invoke-CiHeavyStep "Generation parity benchmark" {
    powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_benchmark.ps1
}

Invoke-CiHeavyStep "Generation parity window gate" {
    powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_window_gate.ps1 -WindowDays 7
}

Invoke-CiHeavyStep "Control-plane thresholds" {
    powershell -ExecutionPolicy Bypass -File scripts/check_backend_control_plane.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE
}

Invoke-CiHeavyStep "Latency budget" {
    powershell -ExecutionPolicy Bypass -File scripts/check_latency_budget.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE
}

Invoke-CiHeavyStep "UI workflow smoke" {
    $uiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
    powershell -ExecutionPolicy Bypass -File scripts/run_ui_workflow_smoke.ps1 -RequireConfiguredRuntime -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE -UiUrl $uiUrl
}

Invoke-CiHeavyStep "UI perf regression" {
    $uiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
    powershell -ExecutionPolicy Bypass -File scripts/ui_perf_regression.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE -UiUrl $uiUrl
}

Invoke-CiHeavyStep "Release baseline capture" {
    $uiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
    powershell -ExecutionPolicy Bypass -File scripts/baseline_capture.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE -UiUrl $uiUrl -SkipUiSmoke -RefreshActiveGateSnapshot
}

Write-Host "[CI Heavy] All heavy checks passed."
