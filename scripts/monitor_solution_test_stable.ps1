param(
    [Parameter(Mandatory = $true)]
    [string]$RunRoot,
    [int]$RefreshSec = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$resolvedRunRoot = [System.IO.Path]::GetFullPath($RunRoot)
$aggregateLogPath = Join-Path $resolvedRunRoot "live_run.log"
$summaryPath = Join-Path $resolvedRunRoot "run_summary.json"

function Read-JsonFileOrNull {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or (-not (Test-Path -LiteralPath $Path))) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Get-FileSnapshotOrNull {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or (-not (Test-Path -LiteralPath $Path))) {
        return $null
    }

    $item = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }

    $ageSec = [Math]::Round(((Get-Date) - $item.LastWriteTime).TotalSeconds, 1)
    return [pscustomobject]@{
        path = $item.FullName
        lastWriteTime = $item.LastWriteTime
        ageSeconds = $ageSec
        length = $item.Length
    }
}

function Format-Age {
    param([AllowNull()][double]$AgeSeconds)

    if ($null -eq $AgeSeconds) {
        return "n/a"
    }

    if ($AgeSeconds -lt 60) {
        return ("{0}s" -f [Math]::Round($AgeSeconds, 1))
    }

    return ("{0}m {1}s" -f [Math]::Floor($AgeSeconds / 60), [Math]::Round($AgeSeconds % 60, 1))
}

function Show-FileSnapshot {
    param(
        [string]$Label,
        [AllowNull()]$Snapshot
    )

    if ($null -eq $Snapshot) {
        Write-Host ("{0}: missing" -f $Label) -ForegroundColor DarkYellow
        return
    }

    Write-Host ("{0}: {1}" -f $Label, $Snapshot.path)
    Write-Host ("  LastWrite: {0} | Age: {1} | Size: {2} bytes" -f $Snapshot.lastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"), (Format-Age -AgeSeconds $Snapshot.ageSeconds), $Snapshot.length)
}

function Get-RunnerProcessSnapshot {
    param([AllowNull()]$Summary)

    if ($null -eq $Summary -or $null -eq $Summary.runnerProcessId) {
        return $null
    }

    $process = Get-Process -Id ([int]$Summary.runnerProcessId) -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return [pscustomobject]@{
            pid = [int]$Summary.runnerProcessId
            alive = $false
            processName = "powershell"
            startTime = $null
            cpu = $null
            handles = $null
            wsMb = $null
        }
    }

    return [pscustomobject]@{
        pid = $process.Id
        alive = $true
        processName = $process.ProcessName
        startTime = $process.StartTime
        cpu = $process.CPU
        handles = $process.Handles
        wsMb = [Math]::Round($process.WorkingSet64 / 1MB, 1)
    }
}

while ($true) {
    $summary = Read-JsonFileOrNull -Path $summaryPath
    $summarySnapshot = Get-FileSnapshotOrNull -Path $summaryPath
    $aggregateSnapshot = Get-FileSnapshotOrNull -Path $aggregateLogPath
    $runnerSnapshot = Get-RunnerProcessSnapshot -Summary $summary
    $runningProject = $null
    if ($null -ne $summary -and $summary.projects) {
        $runningProject = @($summary.projects | Where-Object { $_.status -eq "running" } | Select-Object -First 1)[0]
    }

    $activeProjectLogSnapshot = $null
    $activeProjectErrorSnapshot = $null
    $activeProjectStatusSnapshot = $null
    $activeProjectStatus = $null
    if ($null -ne $runningProject) {
        $activeProjectLogSnapshot = Get-FileSnapshotOrNull -Path $runningProject.logPath
        $activeProjectErrorSnapshot = Get-FileSnapshotOrNull -Path $runningProject.stderrPath
        $activeProjectStatusSnapshot = Get-FileSnapshotOrNull -Path $runningProject.statusPath
        $activeProjectStatus = Read-JsonFileOrNull -Path $runningProject.statusPath
    }

    Clear-Host
    Write-Host ("[Stable Matrix Monitor] {0}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
    Write-Host ("Run root: {0}" -f $resolvedRunRoot)
    Write-Host ""

    Write-Host "Runner:"
    if ($null -eq $runnerSnapshot) {
        Write-Host "Runner PID: unavailable" -ForegroundColor DarkYellow
    }
    else {
        Write-Host ("PID={0} | Alive={1} | Name={2}" -f $runnerSnapshot.pid, $(if ($runnerSnapshot.alive) { "YES" } else { "NO" }), $runnerSnapshot.processName)
        if ($runnerSnapshot.alive) {
            Write-Host ("StartTime={0} | CPU={1} | Handles={2} | WS={3} MB" -f $runnerSnapshot.startTime.ToString("yyyy-MM-dd HH:mm:ss"), [Math]::Round([double]$runnerSnapshot.cpu, 2), $runnerSnapshot.handles, $runnerSnapshot.wsMb)
        }
    }

    Write-Host ""
    Write-Host "Summary file:"
    Show-FileSnapshot -Label "run_summary.json" -Snapshot $summarySnapshot
    if ($null -ne $summary) {
        Write-Host ("  Summary updatedAt: {0}" -f $summary.updatedAt)
        Write-Host ("  Progress: completed={0}/{1}; passed={2}; failed={3}; running={4}" -f $summary.completedProjects, $summary.totalProjects, $summary.passedProjects, $summary.failedProjects, $summary.runningProjects)
    }

    Write-Host ""
    Write-Host "Aggregate log:"
    Show-FileSnapshot -Label "live_run.log" -Snapshot $aggregateSnapshot

    Write-Host ""
    Write-Host "Active project:"
    if ($null -eq $runningProject) {
        Write-Host "No running project recorded in summary." -ForegroundColor DarkYellow
    }
    else {
        Write-Host ("Name={0} | Status={1} | ExitCode={2}" -f $runningProject.name, $runningProject.status, $runningProject.exitCode)
        Show-FileSnapshot -Label "project log" -Snapshot $activeProjectLogSnapshot
        Show-FileSnapshot -Label "project stderr" -Snapshot $activeProjectErrorSnapshot
        Show-FileSnapshot -Label "project status" -Snapshot $activeProjectStatusSnapshot
        if ($null -ne $activeProjectStatus) {
            Write-Host ("  Wrapper phase={0} | outcome={1} | heartbeat={2} | childPid={3} | exitCode={4}" -f $activeProjectStatus.phase, $activeProjectStatus.outcome, $activeProjectStatus.lastHeartbeatUtc, $activeProjectStatus.processId, $activeProjectStatus.exitCode)
            Write-Host ("  Wrapper details: {0}" -f $activeProjectStatus.details)
        }
    }

    Write-Host ""
    $childProcesses = Get-Process dotnet,testhost,vstest -ErrorAction SilentlyContinue |
        Select-Object Id, ProcessName, StartTime, CPU, Handles, @{ Name = 'WS_MB'; Expression = { [Math]::Round($_.WorkingSet64 / 1MB, 1) } } |
        Sort-Object StartTime
    if ($childProcesses) {
        Write-Host "Child test processes:"
        $childProcesses | Format-Table -AutoSize
    }
    else {
        Write-Host "Child test processes: none"
    }

    Write-Host ""
    if ($null -ne $runningProject -and $null -ne $activeProjectLogSnapshot) {
        Write-Host ("Active project log tail: {0}" -f $runningProject.name)
        Get-Content -LiteralPath $runningProject.logPath -Tail 30 -ErrorAction SilentlyContinue
        if ($null -ne $activeProjectErrorSnapshot -and $activeProjectErrorSnapshot.length -gt 0) {
            Write-Host ""
            Write-Host "Active project stderr tail:"
            Get-Content -LiteralPath $runningProject.stderrPath -Tail 20 -ErrorAction SilentlyContinue
        }
    }
    else {
        Write-Host "Aggregate log tail:"
        if (Test-Path -LiteralPath $aggregateLogPath) {
            Get-Content -LiteralPath $aggregateLogPath -Tail 40 -ErrorAction SilentlyContinue
        }
        else {
            Write-Host "(log not created yet)"
        }
    }

    Start-Sleep -Seconds ([Math]::Max(1, $RefreshSec))
}
