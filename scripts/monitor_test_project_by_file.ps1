param(
    [Parameter(Mandatory = $true)][string]$RunRoot,
    [int]$RefreshSec = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not [System.IO.Path]::IsPathRooted($RunRoot)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $RunRoot = Join-Path $repoRoot $RunRoot
}

$summaryPath = Join-Path $RunRoot "summary.json"
$aggregateLogPath = Join-Path $RunRoot "live_run.log"
$statusDirectory = Join-Path $RunRoot "status"

while ($true) {
    Clear-Host
    Write-Host ("[Monitor] {0}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss")) -ForegroundColor Cyan
    Write-Host ("RunRoot: {0}" -f $RunRoot)
    Write-Host ""

    Get-Process powershell,pwsh,dotnet,testhost,vstest -ErrorAction SilentlyContinue |
        Select-Object Id,ProcessName,StartTime,CPU,Handles,@{Name='WS_MB';Expression={[math]::Round($_.WorkingSet64 / 1MB, 1)}} |
        Sort-Object StartTime |
        Format-Table -AutoSize

    if (Test-Path -LiteralPath $summaryPath) {
        $summary = Get-Content -LiteralPath $summaryPath -Raw -Encoding utf8 | ConvertFrom-Json
        Write-Host ""
        Write-Host "Summary:"
        $summary | Select-Object projectName,updatedAt,totalFiles,passedFiles,failedFiles,skippedFiles,interruptedFiles,runningFiles |
            Format-List
    }
    else {
        Write-Host ""
        Write-Host "Summary: missing"
    }

    if (Test-Path -LiteralPath $statusDirectory) {
        $latestStatuses = Get-ChildItem -LiteralPath $statusDirectory -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 3

        if ($latestStatuses.Count -gt 0) {
            Write-Host ""
            Write-Host "Latest status files:"
            $latestStatuses | Select-Object Name,LastWriteTime | Format-Table -AutoSize
            Write-Host ""
            Write-Host "--- Latest status ---"
            Get-Content -LiteralPath $latestStatuses[0].FullName
        }
    }

    if (Test-Path -LiteralPath $aggregateLogPath) {
        Write-Host ""
        Write-Host "--- Aggregate log tail ---"
        Get-Content -LiteralPath $aggregateLogPath -Tail 40
    }

    Start-Sleep -Seconds $RefreshSec
}
