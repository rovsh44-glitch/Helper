param(
    [string]$ReportPath = "",
    [string]$WorkloadClasses = "parity",
    [int]$LookbackHours = 24,
    [string]$SnapshotRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = "temp/verification/heavy/HELPER_PARITY_GATE_" + (Get-Date -Format "yyyy-MM-dd_HH-mm-ss") + ".md"
}

if ($LookbackHours -lt 1) {
    $LookbackHours = 1
}

$prevFilter = $env:HELPER_PARITY_WORKLOAD_CLASSES
$prevLookback = $env:HELPER_PARITY_LOOKBACK_HOURS
$prevSnapshotRoot = $env:HELPER_PARITY_SNAPSHOT_ROOT

try {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    if (-not [System.IO.Path]::IsPathRooted($ReportPath)) {
        $ReportPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ReportPath))
    }

    $reportDirectory = Split-Path -Parent $ReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }

    $env:HELPER_PARITY_WORKLOAD_CLASSES = $WorkloadClasses
    $env:HELPER_PARITY_LOOKBACK_HOURS = $LookbackHours.ToString()
    if ([string]::IsNullOrWhiteSpace($SnapshotRoot)) {
        Remove-Item Env:HELPER_PARITY_SNAPSHOT_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:HELPER_PARITY_SNAPSHOT_ROOT = $SnapshotRoot
    }

    Write-Host "[ParityGate] Generating certification report and evaluating KPI gate..."
    Write-Host "[ParityGate] Workload filter: $WorkloadClasses; LookbackHours: $LookbackHours"
    $cliArgs = @("certify-parity-gate", $ReportPath)
    & (Join-Path $PSScriptRoot "invoke_helper_runtime_cli.ps1") -CliArgs $cliArgs
    if ($LASTEXITCODE -ne 0) {
        throw "[ParityGate] Gate failed."
    }
}
finally {
    $env:HELPER_PARITY_WORKLOAD_CLASSES = $prevFilter
    $env:HELPER_PARITY_LOOKBACK_HOURS = $prevLookback
    $env:HELPER_PARITY_SNAPSHOT_ROOT = $prevSnapshotRoot
}

Write-Host "[ParityGate] Passed. Report: $ReportPath" -ForegroundColor Green
