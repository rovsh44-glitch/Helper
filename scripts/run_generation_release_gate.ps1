param(
    [string[]]$TemplateIds = @(),
    [switch]$AllowIncompleteWindow
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Script
    )

    Write-Host "[ReleaseGate] $Name..."
    & $Script
    if ($LASTEXITCODE -ne 0) {
        throw "[ReleaseGate] Step failed: $Name"
    }
}

try {
    Invoke-Step "Parity KPI gate" {
        powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_gate.ps1
    }

    Invoke-Step "Promotion gate" {
        powershell -ExecutionPolicy Bypass -File scripts/run_generation_promotion_gate.ps1
    }

    if ($AllowIncompleteWindow.IsPresent) {
        $env:HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE = "true"
    }
    else {
        $env:HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE = "false"
    }

    Invoke-Step "Parity rolling window gate" {
        powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_window_gate.ps1 -WindowDays 7
    }

    Invoke-Step "Template certification gate" {
        powershell -ExecutionPolicy Bypass -File scripts/run_template_certification_gate.ps1
    }

    Write-Host "[ReleaseGate] Passed." -ForegroundColor Green
}
catch {
    Write-Warning $_.Exception.Message
    if ($TemplateIds.Count -gt 0) {
        foreach ($templateId in $TemplateIds) {
            if ([string]::IsNullOrWhiteSpace($templateId)) {
                continue
            }

            Write-Host "[ReleaseGate] Rolling back template: $templateId"
            & (Join-Path $PSScriptRoot "invoke_helper_runtime_cli.ps1") template-rollback $templateId
        }
    }

    throw
}
