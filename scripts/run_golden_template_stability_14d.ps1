param(
    [string]$ReportPrefix = "doc/GOLDEN_TEMPLATE_STABILITY_14D",
    [switch]$AllowIncompleteWindow
)

$ErrorActionPreference = "Stop"

$stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$windowReport = "$ReportPrefix" + "_PARITY_WINDOW_" + $stamp + ".md"
$certReport = "$ReportPrefix" + "_CERTIFICATION_" + $stamp + ".md"

if ($AllowIncompleteWindow.IsPresent) {
    $env:HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE = "true"
}
else {
    $env:HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE = "false"
}

Write-Host "[GoldenStability14d] Running parity window gate (14 days)..."
powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_window_gate.ps1 -ReportPath $windowReport -WindowDays 14
if ($LASTEXITCODE -ne 0) {
    throw "[GoldenStability14d] Parity window gate failed."
}

Write-Host "[GoldenStability14d] Running template certification gate..."
powershell -ExecutionPolicy Bypass -File scripts/run_template_certification_gate.ps1 -ReportPath $certReport
if ($LASTEXITCODE -ne 0) {
    throw "[GoldenStability14d] Template certification gate failed."
}

Write-Host "[GoldenStability14d] Passed. Reports:" -ForegroundColor Green
Write-Host " - $windowReport"
Write-Host " - $certReport"
