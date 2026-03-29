param(
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = "doc/HELPER_TEMPLATE_CERTIFICATION_GATE_" + (Get-Date -Format "yyyy-MM-dd_HH-mm-ss") + ".md"
}

Write-Host "[TemplateCertificationGate] Evaluating active template versions..."
& (Join-Path $PSScriptRoot "invoke_helper_runtime_cli.ps1") template-certification-gate $ReportPath
if ($LASTEXITCODE -ne 0) {
    throw "[TemplateCertificationGate] Gate failed."
}

Write-Host "[TemplateCertificationGate] Passed. Report: $ReportPath" -ForegroundColor Green
