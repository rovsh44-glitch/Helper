param(
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = "doc/HELPER_PROMOTION_GATE_" + (Get-Date -Format "yyyy-MM-dd_HH-mm-ss") + ".md"
}

Write-Host "[PromotionGate] Evaluating promotion benchmark gate..."
powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_benchmark.ps1 -ReportPath $ReportPath
if ($LASTEXITCODE -ne 0) {
    throw "[PromotionGate] Gate failed."
}

Write-Host "[PromotionGate] Passed. Report: $ReportPath" -ForegroundColor Green
