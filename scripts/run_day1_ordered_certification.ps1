param(
    [string]$DayDir = "doc/certification_2026-03-15/day-01",
    [int]$ParityRuns = 24,
    [int]$ParityLookbackHours = 6,
    [switch]$RunSmokeAfterParity
)

$ErrorActionPreference = "Stop"

Write-Host "[Day1-Ordered] Step 1: parity golden batch"
powershell -ExecutionPolicy Bypass -File scripts/run_parity_golden_batch.ps1 `
    -Runs $ParityRuns `
    -ReportPath (Join-Path $DayDir "PARITY_GOLDEN_BATCH_day01.md") `
    -FailOnThresholds

Write-Host "[Day1-Ordered] Step 2: parity gate (must run before smoke pack)"
powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_gate.ps1 `
    -ReportPath (Join-Path $DayDir "HELPER_PARITY_GATE_day01.md") `
    -WorkloadClasses "parity" `
    -LookbackHours $ParityLookbackHours

if ($RunSmokeAfterParity) {
    Write-Host "[Day1-Ordered] Step 3: smoke compile pack"
    powershell -ExecutionPolicy Bypass -File scripts/run_smoke_generation_compile_pass.ps1 -Runs 50 -TimeoutSec 120
    Copy-Item -Path "doc/smoke_compile_baseline_$(Get-Date -Format 'yyyy-MM-dd').md" -Destination (Join-Path $DayDir "SMOKE_COMPILE_day01.md") -Force
}

Write-Host "[Day1-Ordered] Completed."
