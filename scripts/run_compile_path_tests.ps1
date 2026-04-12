[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [string]$Filter = "",
    [switch]$NoBuild,
    [switch]$NoRestore,
    [switch]$EnableBlameHang,
    [int]$BlameHangTimeoutSec = 180
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "test\Helper.Runtime.CompilePath.Tests\Helper.Runtime.CompilePath.Tests.csproj"
$runStartedAtUtc = [DateTimeOffset]::UtcNow
$runId = $null
$laneLock = $null
$laneProcess = $null
$laneOutputConfigurationRoot = $null
$runRoot = $null
$resultsRoot = $null
$stdoutLogPath = $null
$stderrLogPath = $null
$pendingError = $null
$teardownFailureMessage = $null
$preserveFailureArtifacts = $false

function New-RunId {
    return [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
}

function Read-PositiveIntFromEnv {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [int]$Fallback,

        [int]$Min = 1,
        [int]$Max = 3600
    )

    $raw = [Environment]::GetEnvironmentVariable($Name)
    $parsed = 0
    if (-not [int]::TryParse($raw, [ref]$parsed))
    {
        return $Fallback
    }

    if ($parsed -lt $Min)
    {
        return $Min
    }

    if ($parsed -gt $Max)
    {
        return $Max
    }

    return $parsed
}

function Get-HelperMsbuildStateRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:HELPER_MSBUILD_INTERMEDIATE_ROOT))
    {
        return Join-Path $env:HELPER_MSBUILD_INTERMEDIATE_ROOT "repo"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:CODEX_THREAD_ID) -or $env:CODEX_SANDBOX_NETWORK_DISABLED -eq "1")
    {
        return Join-Path $env:USERPROFILE ".codex\memories\msbuild-intermediate\repo"
    }

    return $null
}

function Get-ProjectOutputConfigurationRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$BuildConfiguration
    )

    $stateRoot = Get-HelperMsbuildStateRoot
    if (-not [string]::IsNullOrWhiteSpace($stateRoot))
    {
        return Join-Path $stateRoot ("bin\test\Helper.Runtime.CompilePath.Tests\" + $BuildConfiguration)
    }

    return Join-Path $RepoRoot ("temp\compile-path-runs\" + $BuildConfiguration)
}

function Acquire-LaneLock {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LockPath,

        [Parameter(Mandatory = $true)]
        [string]$RunId,

        [Parameter(Mandatory = $true)]
        [string]$RunRoot
    )

    $lockDirectory = Split-Path -Parent $LockPath
    if (-not [string]::IsNullOrWhiteSpace($lockDirectory))
    {
        New-Item -ItemType Directory -Force -Path $lockDirectory | Out-Null
    }

    $waitTimeout = [TimeSpan]::FromSeconds((Read-PositiveIntFromEnv -Name "HELPER_COMPILE_PATH_LOCK_WAIT_SEC" -Fallback 900 -Min 1 -Max 86400))
    $pollInterval = [TimeSpan]::FromSeconds((Read-PositiveIntFromEnv -Name "HELPER_COMPILE_PATH_LOCK_POLL_SEC" -Fallback 2 -Min 1 -Max 60))
    $deadline = [DateTimeOffset]::UtcNow.Add($waitTimeout)
    $waitAnnounced = $false

    while ($true)
    {
        try
        {
            $stream = New-Object System.IO.FileStream(
                $LockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
            break
        }
        catch
        {
            if ([DateTimeOffset]::UtcNow -ge $deadline)
            {
                throw "Compile-path lane did not acquire lock within $($waitTimeout.TotalSeconds)s. Lock path: $LockPath"
            }

            if (-not $waitAnnounced)
            {
                Write-Host "[lane] Waiting for active compile-path lane to release lock at $LockPath"
                $waitAnnounced = $true
            }

            Start-Sleep -Milliseconds ([Math]::Max(250, [int]$pollInterval.TotalMilliseconds))
        }
    }

    $writer = New-Object System.IO.StreamWriter($stream, [System.Text.Encoding]::UTF8, 1024, $true)
    $stream.SetLength(0)
    $writer.WriteLine("runId=$RunId")
    $writer.WriteLine("startedAtUtc=$($runStartedAtUtc.UtcDateTime.ToString('O'))")
    $writer.WriteLine("runRoot=$RunRoot")
    $writer.WriteLine("ownerPid=$PID")
    $writer.Flush()

    return [PSCustomObject]@{
        Path = $LockPath
        Stream = $stream
        Writer = $writer
    }
}

function Release-LaneLock {
    param(
        $LockHandle
    )

    if ($null -eq $LockHandle)
    {
        return
    }

    try
    {
        if ($null -ne $LockHandle.Writer)
        {
            $LockHandle.Writer.Dispose()
        }
    }
    catch
    {
        # best effort
    }

    try
    {
        if ($null -ne $LockHandle.Stream)
        {
            $LockHandle.Stream.Dispose()
        }
    }
    catch
    {
        # best effort
    }

    try
    {
        if (-not [string]::IsNullOrWhiteSpace($LockHandle.Path) -and (Test-Path -LiteralPath $LockHandle.Path))
        {
            Remove-Item -LiteralPath $LockHandle.Path -Force -ErrorAction Stop
        }
    }
    catch
    {
        # best effort
    }
}

function Remove-DirectoryIfPresent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathToRemove,

        [int]$MaxAttempts = 6,

        [int]$DelayMilliseconds = 1000
    )

    if (-not (Test-Path -LiteralPath $PathToRemove))
    {
        return
    }

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++)
    {
        try
        {
            Remove-Item -LiteralPath $PathToRemove -Recurse -Force -ErrorAction Stop
            Write-Host "[cleanup] Removed $PathToRemove"
            return
        }
        catch
        {
            if ($attempt -ge $MaxAttempts)
            {
                throw
            }

            Start-Sleep -Milliseconds $DelayMilliseconds
        }
    }
}

function Write-JsonDiagnostic {
    param(
        [string]$Path,
        $Value
    )

    if ([string]::IsNullOrWhiteSpace($Path))
    {
        return
    }

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory))
    {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Test-ProcessAliveById {
    param(
        [int]$ProcessId
    )

    return $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

function Wait-ProcessExitById {
    param(
        [int]$ProcessId,
        [int]$TimeoutSeconds = 10
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(1, $TimeoutSeconds))
    while ([DateTimeOffset]::UtcNow -lt $deadline)
    {
        if (-not (Test-ProcessAliveById -ProcessId $ProcessId))
        {
            return $true
        }

        Start-Sleep -Milliseconds 250
    }

    return -not (Test-ProcessAliveById -ProcessId $ProcessId)
}

function Get-ProcessTreeSnapshotOrEmpty {
    param(
        [int]$RootProcessId
    )

    try
    {
        $rows = @(Get-CimInstance Win32_Process -ErrorAction Stop | Select-Object ProcessId, ParentProcessId, Name, CommandLine)
        $byProcessId = @{}
        $childrenByParent = @{}
        foreach ($row in $rows)
        {
            $pid = [int]$row.ProcessId
            $ppid = [int]$row.ParentProcessId
            $byProcessId[$pid] = $row
            if (-not $childrenByParent.ContainsKey($ppid))
            {
                $childrenByParent[$ppid] = New-Object System.Collections.Generic.List[object]
            }

            $childrenByParent[$ppid].Add($row)
        }

        $queue = New-Object 'System.Collections.Generic.Queue[int]'
        $visited = New-Object 'System.Collections.Generic.HashSet[int]'
        $processes = New-Object System.Collections.Generic.List[object]
        $queue.Enqueue($RootProcessId)
        while ($queue.Count -gt 0)
        {
            $currentId = $queue.Dequeue()
            if (-not $visited.Add($currentId))
            {
                continue
            }

            if ($byProcessId.ContainsKey($currentId))
            {
                $row = $byProcessId[$currentId]
                $processes.Add([PSCustomObject]@{
                    ProcessId = [int]$row.ProcessId
                    ParentProcessId = [int]$row.ParentProcessId
                    Name = $row.Name
                    CommandLine = $row.CommandLine
                })
            }

            if ($childrenByParent.ContainsKey($currentId))
            {
                foreach ($child in $childrenByParent[$currentId])
                {
                    $queue.Enqueue([int]$child.ProcessId)
                }
            }
        }

        return [PSCustomObject]@{
            capturedAtUtc = [DateTimeOffset]::UtcNow.UtcDateTime.ToString("O")
            rootProcessId = $RootProcessId
            processes = @($processes)
        }
    }
    catch
    {
        return [PSCustomObject]@{
            capturedAtUtc = [DateTimeOffset]::UtcNow.UtcDateTime.ToString("O")
            rootProcessId = $RootProcessId
            error = $_.Exception.Message
            processes = @()
        }
    }
}

function Get-LatestWriteUtcOrNull {
    param(
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path))
    {
        return $null
    }

    try
    {
        return (Get-Item -LiteralPath $Path -ErrorAction Stop).LastWriteTimeUtc
    }
    catch
    {
        return $null
    }
}

function Get-DirectoryLatestWriteUtcOrNull {
    param(
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path))
    {
        return $null
    }

    try
    {
        $latest = Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -eq $latest)
        {
            return $null
        }

        return $latest.LastWriteTimeUtc
    }
    catch
    {
        return $null
    }
}

function Get-ProcessCpuSnapshotOrNull {
    param(
        [System.Diagnostics.Process]$Process
    )

    if ($null -eq $Process)
    {
        return $null
    }

    try
    {
        $rootId = $Process.Id
    }
    catch
    {
        return $null
    }

    $treeSnapshot = Get-ProcessTreeSnapshotOrEmpty -RootProcessId $rootId
    if ($null -eq $treeSnapshot.processes -or $treeSnapshot.processes.Count -eq 0)
    {
        return $null
    }

    $total = [TimeSpan]::Zero
    foreach ($processInfo in $treeSnapshot.processes)
    {
        try
        {
            $total = $total.Add((Get-Process -Id ([int]$processInfo.ProcessId) -ErrorAction Stop).TotalProcessorTime)
        }
        catch
        {
            # process exited between snapshot and inspection
        }
    }

    if ($total -eq [TimeSpan]::Zero)
    {
        return $null
    }

    try
    {
        return $total
    }
    catch
    {
        return $null
    }
}

function Stop-LaneProcesses {
    param(
        [System.Diagnostics.Process]$RootProcess,
        [string]$RunRoot
    )

    $summary = [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow.UtcDateTime.ToString("O")
        rootProcessId = $null
        killMethod = "not_needed"
        taskKillExitCode = $null
        rootExited = $true
        preKillSnapshot = @()
        postKillSnapshot = @()
        notes = @()
    }

    if ($null -eq $RootProcess)
    {
        Write-JsonDiagnostic -Path (Join-Path $RunRoot "teardown_summary.json") -Value $summary
        return [PSCustomObject]$summary
    }

    $rootId = $null
    try
    {
        $rootId = $RootProcess.Id
    }
    catch
    {
        $summary.notes += "Failed to read root process id."
        Write-JsonDiagnostic -Path (Join-Path $RunRoot "teardown_summary.json") -Value $summary
        return [PSCustomObject]$summary
    }

    $summary.rootProcessId = $rootId
    $summary.preKillSnapshot = @(Get-ProcessTreeSnapshotOrEmpty -RootProcessId $rootId)
    if (-not (Test-ProcessAliveById -ProcessId $rootId))
    {
        Write-JsonDiagnostic -Path (Join-Path $RunRoot "teardown_summary.json") -Value $summary
        return [PSCustomObject]$summary
    }

    $killedByTaskKill = $false
    try
    {
        & taskkill /PID $rootId /T /F *> $null
        $summary.taskKillExitCode = $LASTEXITCODE
        if ($LASTEXITCODE -eq 0)
        {
            $killedByTaskKill = $true
            $summary.killMethod = "taskkill"
            Write-Host "[cleanup] Stopped process tree rooted at PID=$rootId via taskkill /T"
        }
    }
    catch
    {
        $summary.notes += "taskkill failed: $($_.Exception.Message)"
    }

    if ($killedByTaskKill)
    {
        $summary.rootExited = Wait-ProcessExitById -ProcessId $rootId -TimeoutSeconds 10
        if ($summary.rootExited)
        {
            Write-JsonDiagnostic -Path (Join-Path $RunRoot "teardown_summary.json") -Value $summary
            return [PSCustomObject]$summary
        }
    }

    try
    {
        if (Test-ProcessAliveById -ProcessId $rootId)
        {
            Stop-Process -Id $rootId -Force -ErrorAction Stop
            $summary.killMethod = if ($killedByTaskKill) { "taskkill+Stop-Process" } else { "Stop-Process" }
            Write-Host "[cleanup] Stopped root process PID=$rootId via Stop-Process fallback"
        }
    }
    catch
    {
        $summary.notes += "Stop-Process fallback failed: $($_.Exception.Message)"
    }

    $summary.rootExited = Wait-ProcessExitById -ProcessId $rootId -TimeoutSeconds 10
    if (-not $summary.rootExited)
    {
        $summary.postKillSnapshot = @(Get-ProcessTreeSnapshotOrEmpty -RootProcessId $rootId)
    }

    Write-JsonDiagnostic -Path (Join-Path $RunRoot "teardown_summary.json") -Value $summary
    return [PSCustomObject]$summary
}

function Remove-TempResidue {
    param(
        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$StartedAtUtc
    )

    $tempRoot = [System.IO.Path]::GetTempPath()
    $thresholdUtc = $StartedAtUtc.UtcDateTime.AddMinutes(-1)
    $prefixes = @(
        "helper_template_e2e_",
        "helper_template_cert_test_",
        "helper_compile_gate_",
        "helper_fix_applier_"
    )

    Get-ChildItem -LiteralPath $tempRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object {
            $name = $_.Name
            $prefixes | Where-Object { $name.StartsWith($_, [System.StringComparison]::OrdinalIgnoreCase) }
        } |
        Where-Object { $_.LastWriteTimeUtc -ge $thresholdUtc } |
        ForEach-Object {
            try
            {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
                Write-Host "[cleanup] Removed temp scope $($_.FullName)"
            }
            catch
            {
                Write-Host "[cleanup] Failed to remove temp scope $($_.FullName): $($_.Exception.Message)"
            }
        }
}

function Join-ProcessArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Values
    )

    return ($Values | ForEach-Object {
        if ($_ -match '[\s"]')
        {
            '"' + ($_.Replace('"', '\"')) + '"'
        }
        else
        {
            $_
        }
    }) -join ' '
}

$laneOutputConfigurationRoot = Get-ProjectOutputConfigurationRoot -RepoRoot $repoRoot -BuildConfiguration $Configuration
New-Item -ItemType Directory -Force -Path $laneOutputConfigurationRoot | Out-Null
$runId = New-RunId
$runRoot = Join-Path $laneOutputConfigurationRoot $runId
$resultsRoot = Join-Path $runRoot "TestResults"
$stdoutLogPath = Join-Path $runRoot "compile-path.stdout.log"
$stderrLogPath = Join-Path $runRoot "compile-path.stderr.log"
$laneLockPath = Join-Path $laneOutputConfigurationRoot "compile_path_lane.lock"
$laneLock = Acquire-LaneLock -LockPath $laneLockPath -RunId $runId -RunRoot $runRoot
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

Write-Host "[lane] Run id: $runId"
Write-Host "[lane] Run root: $runRoot"
Write-Host "[lane] Mode: $(if ($EnableBlameHang) { 'forensic' } else { 'operational' })"

$arguments = @(
    "test"
    "test\\Helper.Runtime.CompilePath.Tests\\Helper.Runtime.CompilePath.Tests.csproj"
    "-c"
    $Configuration
    "-m:1"
    "--disable-build-servers"
    "--results-directory"
    $resultsRoot
    "--logger"
    "console;verbosity=minimal"
)

if ($NoBuild) {
    $arguments += "--no-build"
}

if ($NoRestore) {
    $arguments += "--no-restore"
}

if ($EnableBlameHang)
{
    if ($BlameHangTimeoutSec -lt 120)
    {
        throw "Forensic blame timeout must be at least 120 seconds for compile-path runs."
    }

    $arguments += "--blame-hang"
    $arguments += "--blame-hang-timeout"
    $arguments += ("{0}s" -f $BlameHangTimeoutSec)
}

if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $arguments += "--filter"
    $arguments += $Filter
}

try {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = "dotnet"
    $startInfo.Arguments = Join-ProcessArguments -Values $arguments
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1"
    $startInfo.EnvironmentVariables["HELPER_COMPILE_PATH_LANE_RUN_ID"] = $runId
    $startInfo.EnvironmentVariables["HELPER_COMPILE_PATH_RUN_ROOT"] = $runRoot

    $laneProcess = New-Object System.Diagnostics.Process
    $laneProcess.StartInfo = $startInfo
    [void]$laneProcess.Start()

    $stdoutTask = $laneProcess.StandardOutput.ReadToEndAsync()
    $stderrTask = $laneProcess.StandardError.ReadToEndAsync()
    $maxWallTimeout = [TimeSpan]::FromSeconds((Read-PositiveIntFromEnv -Name "HELPER_COMPILE_PATH_MAX_WALL_SEC" -Fallback 1800 -Min 30 -Max 86400))
    $idleTimeout = [TimeSpan]::FromSeconds((Read-PositiveIntFromEnv -Name "HELPER_COMPILE_PATH_IDLE_TIMEOUT_SEC" -Fallback 180 -Min 15 -Max 3600))
    $heartbeatInterval = [TimeSpan]::FromSeconds((Read-PositiveIntFromEnv -Name "HELPER_COMPILE_PATH_HEARTBEAT_SEC" -Fallback 30 -Min 5 -Max 300))
    $lastHeartbeatUtc = [DateTimeOffset]::UtcNow
    $lastObservedActivityUtc = [DateTimeOffset]::UtcNow
    $lastStdoutWriteUtc = Get-LatestWriteUtcOrNull -Path $stdoutLogPath
    $lastStderrWriteUtc = Get-LatestWriteUtcOrNull -Path $stderrLogPath
    $lastResultsWriteUtc = Get-DirectoryLatestWriteUtcOrNull -Path $resultsRoot
    $lastCpuSnapshot = Get-ProcessCpuSnapshotOrNull -Process $laneProcess
    Write-Host "[lane] Started compile-path lane PID=$($laneProcess.Id)"

    while (-not $laneProcess.HasExited)
    {
        Start-Sleep -Seconds 1
        $laneProcess.Refresh()

        $nowUtc = [DateTimeOffset]::UtcNow
        $stdoutWriteUtc = Get-LatestWriteUtcOrNull -Path $stdoutLogPath
        if ($null -ne $stdoutWriteUtc)
        {
            $stdoutObservedAt = [DateTimeOffset]$stdoutWriteUtc
            if ($null -eq $lastStdoutWriteUtc -or $stdoutWriteUtc -gt $lastStdoutWriteUtc)
            {
                $lastStdoutWriteUtc = $stdoutWriteUtc
                if ($stdoutObservedAt -gt $lastObservedActivityUtc)
                {
                    $lastObservedActivityUtc = $stdoutObservedAt
                }
            }
        }

        $stderrWriteUtc = Get-LatestWriteUtcOrNull -Path $stderrLogPath
        if ($null -ne $stderrWriteUtc)
        {
            $stderrObservedAt = [DateTimeOffset]$stderrWriteUtc
            if ($null -eq $lastStderrWriteUtc -or $stderrWriteUtc -gt $lastStderrWriteUtc)
            {
                $lastStderrWriteUtc = $stderrWriteUtc
                if ($stderrObservedAt -gt $lastObservedActivityUtc)
                {
                    $lastObservedActivityUtc = $stderrObservedAt
                }
            }
        }

        $resultsWriteUtc = Get-DirectoryLatestWriteUtcOrNull -Path $resultsRoot
        if ($null -ne $resultsWriteUtc)
        {
            $resultsObservedAt = [DateTimeOffset]$resultsWriteUtc
            if ($null -eq $lastResultsWriteUtc -or $resultsWriteUtc -gt $lastResultsWriteUtc)
            {
                $lastResultsWriteUtc = $resultsWriteUtc
                if ($resultsObservedAt -gt $lastObservedActivityUtc)
                {
                    $lastObservedActivityUtc = $resultsObservedAt
                }
            }
        }

        $cpuSnapshot = Get-ProcessCpuSnapshotOrNull -Process $laneProcess
        if ($null -ne $cpuSnapshot -and $null -ne $lastCpuSnapshot -and $cpuSnapshot -gt $lastCpuSnapshot)
        {
            $lastObservedActivityUtc = $nowUtc
        }

        if ($null -ne $cpuSnapshot)
        {
            $lastCpuSnapshot = $cpuSnapshot
        }

        if (($nowUtc - $lastHeartbeatUtc) -ge $heartbeatInterval)
        {
            $elapsed = [int]($nowUtc - $runStartedAtUtc).TotalSeconds
            $idle = [int]($nowUtc - $lastObservedActivityUtc).TotalSeconds
            Write-Host "[lane] Compile-path lane still running after ${elapsed}s; last observed activity ${idle}s ago (PID=$($laneProcess.Id))"
            $lastHeartbeatUtc = $nowUtc
        }

        if (($nowUtc - $runStartedAtUtc) -ge $maxWallTimeout)
        {
            $preserveFailureArtifacts = $true
            throw "Compile-path lane exceeded max wall time of $([int]$maxWallTimeout.TotalSeconds)s. RunRoot=$runRoot"
        }

        if (($nowUtc - $lastObservedActivityUtc) -ge $idleTimeout)
        {
            $preserveFailureArtifacts = $true
            throw "Compile-path lane stalled for $([int]$idleTimeout.TotalSeconds)s without stdout/stderr/results/cpu activity. RunRoot=$runRoot"
        }
    }

    $laneProcess.WaitForExit()
    $laneProcess.Refresh()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    Set-Content -Path $stdoutLogPath -Value $stdout -NoNewline
    Set-Content -Path $stderrLogPath -Value $stderr -NoNewline

    if (-not [string]::IsNullOrWhiteSpace($stdout))
    {
        Write-Host $stdout
    }

    if (-not [string]::IsNullOrWhiteSpace($stderr))
    {
        Write-Host $stderr
    }

    if ($laneProcess.ExitCode -ne 0) {
        $preserveFailureArtifacts = $true
        throw "Compile-path tests failed with exit code $($laneProcess.ExitCode)."
    }
}
catch
{
    $pendingError = $_
}
finally {
    $teardownSummary = Stop-LaneProcesses -RootProcess $laneProcess -RunRoot $runRoot
    if ($null -ne $teardownSummary -and -not $teardownSummary.rootExited)
    {
        $preserveFailureArtifacts = $true
        $teardownFailureMessage = "Compile-path lane root process PID=$($teardownSummary.rootProcessId) remained alive after teardown. RunRoot=$runRoot"
    }

    if (-not $preserveFailureArtifacts)
    {
        foreach ($path in @($resultsRoot))
        {
            if ([string]::IsNullOrWhiteSpace($path))
            {
                continue
            }

            try
            {
                Remove-DirectoryIfPresent -PathToRemove $path
            }
            catch
            {
                Write-Host "[cleanup] Failed to remove $path : $($_.Exception.Message)"
            }
        }
    }
    else
    {
        Write-Host "[cleanup] Preserving failure diagnostics under $runRoot"
    }

    Remove-TempResidue -StartedAtUtc $runStartedAtUtc

    if (-not $preserveFailureArtifacts -and -not [string]::IsNullOrWhiteSpace($runRoot) -and (Test-Path -LiteralPath $runRoot))
    {
        try
        {
            $remaining = Get-ChildItem -LiteralPath $runRoot -Force -ErrorAction SilentlyContinue
            if ($null -eq $remaining -or $remaining.Count -eq 0)
            {
                Remove-Item -LiteralPath $runRoot -Force -ErrorAction Stop
                Write-Host "[cleanup] Removed empty run root $runRoot"
            }
        }
        catch
        {
            Write-Host "[cleanup] Failed to remove empty run root $runRoot : $($_.Exception.Message)"
        }
    }

    Release-LaneLock -LockHandle $laneLock
}

if ($null -ne $pendingError)
{
    if (-not [string]::IsNullOrWhiteSpace($teardownFailureMessage))
    {
        throw "$($pendingError.Exception.Message) | $teardownFailureMessage"
    }

    throw $pendingError
}

if (-not [string]::IsNullOrWhiteSpace($teardownFailureMessage))
{
    throw $teardownFailureMessage
}
