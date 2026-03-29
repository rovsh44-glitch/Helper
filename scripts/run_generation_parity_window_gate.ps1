param(
    [string]$ReportPath = "",
    [int]$WindowDays = 7,
    [string]$SnapshotRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = "doc/HELPER_PARITY_WINDOW_GATE_" + (Get-Date -Format "yyyy-MM-dd_HH-mm-ss") + ".md"
}

$window = [Math]::Max(1, [Math]::Min(30, $WindowDays))
Write-Host "[ParityWindowGate] Evaluating rolling parity window ($window day(s))..."
$prevSnapshotRoot = $env:HELPER_PARITY_SNAPSHOT_ROOT
if ([string]::IsNullOrWhiteSpace($SnapshotRoot)) {
    Remove-Item Env:HELPER_PARITY_SNAPSHOT_ROOT -ErrorAction SilentlyContinue
}
else {
    $env:HELPER_PARITY_SNAPSHOT_ROOT = $SnapshotRoot
}

try {
    $cliArgs = @("certify-parity-window-gate", $ReportPath, $window.ToString())
    & (Join-Path $PSScriptRoot "invoke_helper_runtime_cli.ps1") -CliArgs $cliArgs
    if ($LASTEXITCODE -ne 0) {
        throw "[ParityWindowGate] Gate failed."
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

Write-Host "[ParityWindowGate] Passed. Report: $ReportPath" -ForegroundColor Green
