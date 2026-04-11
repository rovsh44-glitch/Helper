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
    powershell -ExecutionPolicy Bypass -File scripts/secret_scan.ps1 -ScanMode repo
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
    $nugetMode = if (-not [string]::IsNullOrWhiteSpace($env:HELPER_NUGET_SECURITY_GATE_MODE)) {
        $env:HELPER_NUGET_SECURITY_GATE_MODE
    }
    elseif ($env:CI -eq "true") {
        "strict-online"
    }
    else {
        "best-effort-local"
    }

    powershell -ExecutionPolicy Bypass -File scripts/nuget_security_gate.ps1 -ExecutionMode $nugetMode
}

Invoke-CiStep "Solution build coverage" {
    powershell -ExecutionPolicy Bypass -File scripts/check_solution_build_coverage.ps1
}

Invoke-CiStep "Build" {
    dotnet build Helper.sln -m:1
}

Invoke-CiStep "Fast runtime lane" {
    powershell -ExecutionPolicy Bypass -File scripts/run_fast_tests.ps1 -Configuration Debug -NoBuild -NoRestore
}

Invoke-CiStep "Eval" {
    powershell -ExecutionPolicy Bypass -File scripts/run_eval_gate.ps1 -NoBuild -NoRestore
}

Invoke-CiStep "Tool benchmark" {
    powershell -ExecutionPolicy Bypass -File scripts/run_tool_benchmark.ps1 -Configuration Debug -NoBuild -NoRestore
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

Invoke-CiStep "Frontend build" {
    & (Join-Path $PSScriptRoot "build_frontend_verification.ps1") -RequireRebuild
}

Invoke-CiStep "Bundle budget" {
    powershell -ExecutionPolicy Bypass -File scripts/check_bundle_budget.ps1
}

Write-Host "[CI Gate] All checks passed."

