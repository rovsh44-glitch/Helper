param(
    [string]$ReportPath = "",
    [string]$WorkloadClasses = "parity",
    [string]$StartDate = "",
    [string]$EndDate = "",
    [string]$SnapshotRoot = "",
    [switch]$OverwriteExisting,
    [switch]$DryRun,
    [switch]$AllowEmpty
)

$ErrorActionPreference = "Stop"

$args = @("backfill-parity-daily", "--workload", $WorkloadClasses)

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $args += @("--report", $ReportPath)
}

if (-not [string]::IsNullOrWhiteSpace($StartDate)) {
    $args += @("--start", $StartDate)
}

if (-not [string]::IsNullOrWhiteSpace($EndDate)) {
    $args += @("--end", $EndDate)
}

if ($OverwriteExisting.IsPresent) {
    $args += "--overwrite"
}

if ($DryRun.IsPresent) {
    $args += "--dry-run"
}

if ($AllowEmpty.IsPresent) {
    $args += "--allow-empty"
}

Write-Host "[ParityBackfill] Running backfill from generation_runs.jsonl..."
$prevSnapshotRoot = $env:HELPER_PARITY_SNAPSHOT_ROOT
if ([string]::IsNullOrWhiteSpace($SnapshotRoot)) {
    Remove-Item Env:HELPER_PARITY_SNAPSHOT_ROOT -ErrorAction SilentlyContinue
}
else {
    $env:HELPER_PARITY_SNAPSHOT_ROOT = $SnapshotRoot
}

try {
    & (Join-Path $PSScriptRoot "invoke_helper_runtime_cli.ps1") @args
    if ($LASTEXITCODE -ne 0) {
        throw "[ParityBackfill] Backfill failed."
    }
}
finally {
    if ($null -eq $prevSnapshotRoot) {
        Remove-Item Env:HELPER_PARITY_SNAPSHOT_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:HELPER_PARITY_SNAPSHOT_ROOT = $prevSnapshotRoot
    }
}

Write-Host "[ParityBackfill] Completed." -ForegroundColor Green
