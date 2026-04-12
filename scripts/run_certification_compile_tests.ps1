param(
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore,
    [string]$Filter = "",
    [switch]$EnableBlameHang,
    [int]$BlameHangTimeoutSec = 180,
    [string[]]$ExtraArgs = @()
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$runStartedAtUtc = [DateTimeOffset]::UtcNow
$runId = $null
$laneProcess = $null
$laneLock = $null
$laneOutputConfigurationRoot = $null
$laneRunRoot = $null
$laneDataRoot = $null
$laneLogsRoot = $null
$laneResultsRoot = $null
$traceArchivePath = $null
$preserveFailureArtifacts = $false
$pendingError = $null
$teardownFailureMessage = $null

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
        return Join-Path $stateRoot ("bin\test\Helper.Runtime.Certification.Compile.Tests\" + $BuildConfiguration)
    }

    return Join-Path $RepoRoot ("test\Helper.Runtime.Certification.Compile.Tests\bin\" + $BuildConfiguration)
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

    $waitTimeout = [TimeSpan]::FromSeconds((Read-PositiveIntFromEnv -Name "HELPER_CERTIFICATION_COMPILE_LOCK_WAIT_SEC" -Fallback 900 -Min 1 -Max 86400))
    $pollInterval = [TimeSpan]::FromSeconds((Read-PositiveIntFromEnv -Name "HELPER_CERTIFICATION_COMPILE_LOCK_POLL_SEC" -Fallback 2 -Min 1 -Max 60))
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
                throw "Certification compile lane did not acquire lock within $($waitTimeout.TotalSeconds)s. Lock path: $LockPath"
            }

            if (-not $waitAnnounced)
            {
                Write-Host "[lane] Waiting for active certification compile lane to release lock at $LockPath"
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
        [string]$PathToRemove
    )

    if (-not (Test-Path -LiteralPath $PathToRemove))
    {
        return
    }

    Remove-Item -LiteralPath $PathToRemove -Recurse -Force -ErrorAction Stop
    Write-Host "[cleanup] Removed $PathToRemove"
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
    $prefixes = @("helper_template_e2e_", "helper_template_cert_test_")

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

function Get-RelativePathOrSelf {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    if ([string]::IsNullOrWhiteSpace($TargetPath))
    {
        return $null
    }

    if ([string]::IsNullOrWhiteSpace($BasePath))
    {
        return $TargetPath
    }

    try
    {
        $resolvedBase = (Resolve-Path -LiteralPath $BasePath -ErrorAction Stop).Path.TrimEnd('\')
        $resolvedTarget = (Resolve-Path -LiteralPath $TargetPath -ErrorAction Stop).Path
        if (-not $resolvedTarget.StartsWith($resolvedBase + '\', [System.StringComparison]::OrdinalIgnoreCase) -and
            -not $resolvedTarget.Equals($resolvedBase, [System.StringComparison]::OrdinalIgnoreCase))
        {
            return $resolvedTarget
        }

        $baseUri = [Uri]($resolvedBase + '\')
        $targetUri = [Uri]$resolvedTarget
        return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString().Replace('/', '\'))
    }
    catch
    {
        return $TargetPath
    }
}

function Get-FileTailOrEmpty {
    param(
        [string]$Path,
        [int]$Tail = 40
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path))
    {
        return @()
    }

    try
    {
        return @(Get-Content -LiteralPath $Path -Tail $Tail -ErrorAction Stop)
    }
    catch
    {
        return @("[tail-unavailable] $($_.Exception.Message)")
    }
}

function Get-MostRecentFileOrNull {
    param(
        [string]$Path,
        [string]$Filter = "*"
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path))
    {
        return $null
    }

    try
    {
        return Get-ChildItem -LiteralPath $Path -Recurse -File -Filter $Filter -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
    }
    catch
    {
        return $null
    }
}

function Write-FailureSummaryArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunRoot,

        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$StartedAtUtc,

        [string]$TracePath,

        [string]$ResultsRoot,

        $PendingError,

        [string]$TeardownFailureMessage
    )

    if ([string]::IsNullOrWhiteSpace($RunRoot))
    {
        return $null
    }

    $errorMessage = $null
    if ($null -ne $PendingError)
    {
        $errorMessage = $PendingError.Exception.Message
    }

    $latestTrx = Get-MostRecentFileOrNull -Path $ResultsRoot -Filter "*.trx"
    $latestSequence = Get-MostRecentFileOrNull -Path $ResultsRoot -Filter "Sequence_*.xml"
    $traceTail = Get-FileTailOrEmpty -Path $TracePath -Tail 60
    $trxTail = Get-FileTailOrEmpty -Path $latestTrx.FullName -Tail 40
    $sequenceTail = Get-FileTailOrEmpty -Path $latestSequence.FullName -Tail 40
    $durationSeconds = [Math]::Round(([DateTimeOffset]::UtcNow - $StartedAtUtc).TotalSeconds, 2)

    $summary = [ordered]@{
        generatedAtUtc = [DateTimeOffset]::UtcNow.UtcDateTime.ToString("O")
        runRoot = $RunRoot
        runRootRelative = Get-RelativePathOrSelf -BasePath $repoRoot -TargetPath $RunRoot
        durationSeconds = $durationSeconds
        tracePath = $TracePath
        tracePathRelative = Get-RelativePathOrSelf -BasePath $repoRoot -TargetPath $TracePath
        traceExists = -not [string]::IsNullOrWhiteSpace($TracePath) -and (Test-Path -LiteralPath $TracePath)
        resultsRoot = $ResultsRoot
        resultsRootRelative = Get-RelativePathOrSelf -BasePath $repoRoot -TargetPath $ResultsRoot
        latestTrx = $(if ($null -eq $latestTrx) { $null } else { Get-RelativePathOrSelf -BasePath $repoRoot -TargetPath $latestTrx.FullName })
        latestSequence = $(if ($null -eq $latestSequence) { $null } else { Get-RelativePathOrSelf -BasePath $repoRoot -TargetPath $latestSequence.FullName })
        errorMessage = $errorMessage
        teardownFailureMessage = $TeardownFailureMessage
        traceTail = @($traceTail)
        trxTail = @($trxTail)
        sequenceTail = @($sequenceTail)
    }

    $jsonPath = Join-Path $RunRoot "failure_summary.json"
    $mdPath = Join-Path $RunRoot "failure_summary.md"
    Write-JsonDiagnostic -Path $jsonPath -Value $summary

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Certification Compile Failure Summary")
    $lines.Add("")
    $lines.Add("* GeneratedAtUtc: $($summary.generatedAtUtc)")
    $lines.Add("* DurationSeconds: $($summary.durationSeconds)")
    $lines.Add("* RunRoot: $($summary.runRootRelative)")
    $lines.Add("* ResultsRoot: $($summary.resultsRootRelative)")
    $lines.Add("* TracePath: $($summary.tracePathRelative)")
    if (-not [string]::IsNullOrWhiteSpace($summary.latestTrx))
    {
        $lines.Add("* LatestTrx: $($summary.latestTrx)")
    }
    if (-not [string]::IsNullOrWhiteSpace($summary.latestSequence))
    {
        $lines.Add("* LatestSequence: $($summary.latestSequence)")
    }
    if (-not [string]::IsNullOrWhiteSpace($summary.errorMessage))
    {
        $lines.Add("* Error: $($summary.errorMessage)")
    }
    if (-not [string]::IsNullOrWhiteSpace($summary.teardownFailureMessage))
    {
        $lines.Add("* TeardownError: $($summary.teardownFailureMessage)")
    }

    foreach ($section in @(
        @{ Title = "Trace Tail"; Lines = $summary.traceTail },
        @{ Title = "TRX Tail"; Lines = $summary.trxTail },
        @{ Title = "Sequence Tail"; Lines = $summary.sequenceTail }
    ))
    {
        if ($null -eq $section.Lines -or $section.Lines.Count -eq 0)
        {
            continue
        }

        $lines.Add("")
        $lines.Add("## $($section.Title)")
        $lines.Add('```text')
        foreach ($line in $section.Lines)
        {
            $lines.Add([string]$line)
        }
        $lines.Add('```')
    }

    Set-Content -LiteralPath $mdPath -Value $lines -Encoding UTF8
    return [PSCustomObject]@{
        JsonPath = $jsonPath
        MarkdownPath = $mdPath
        Summary = [PSCustomObject]$summary
    }
}

function Write-FailureSummaryToConsole {
    param(
        $FailureSummaryArtifact
    )

    if ($null -eq $FailureSummaryArtifact)
    {
        return
    }

    $summary = $FailureSummaryArtifact.Summary
    Write-Host "[diagnostics] Failure summary: $($FailureSummaryArtifact.MarkdownPath)"
    Write-Host "[diagnostics] Run root: $($summary.runRootRelative)"
    if (-not [string]::IsNullOrWhiteSpace($summary.latestTrx))
    {
        Write-Host "[diagnostics] Latest TRX: $($summary.latestTrx)"
    }
    if (-not [string]::IsNullOrWhiteSpace($summary.latestSequence))
    {
        Write-Host "[diagnostics] Latest sequence: $($summary.latestSequence)"
    }
    if (-not [string]::IsNullOrWhiteSpace($summary.errorMessage))
    {
        Write-Host "[diagnostics] Error: $($summary.errorMessage)"
    }
    if (-not [string]::IsNullOrWhiteSpace($summary.teardownFailureMessage))
    {
        Write-Host "[diagnostics] Teardown error: $($summary.teardownFailureMessage)"
    }

    if ($summary.traceTail.Count -gt 0)
    {
        Write-Host "[diagnostics] Trace tail:"
        foreach ($line in $summary.traceTail)
        {
            Write-Host $line
        }
    }
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

$laneOutputConfigurationRoot = Get-ProjectOutputConfigurationRoot -RepoRoot $repoRoot -BuildConfiguration $Configuration
New-Item -ItemType Directory -Force -Path $laneOutputConfigurationRoot | Out-Null
$runId = New-RunId
$laneRunRoot = Join-Path $laneOutputConfigurationRoot ("runs\" + $runId)
$laneDataRoot = Join-Path $laneRunRoot "HELPER_DATA"
$laneLogsRoot = Join-Path $laneDataRoot "LOG"
$laneResultsRoot = Join-Path $laneRunRoot "TestResults"
$traceArchivePath = Join-Path $laneRunRoot "certification_process_trace.jsonl"
$laneLockPath = Join-Path $laneOutputConfigurationRoot "certification_compile_lane.lock"
$laneLock = Acquire-LaneLock -LockPath $laneLockPath -RunId $runId -RunRoot $laneRunRoot
New-Item -ItemType Directory -Force -Path $laneRunRoot | Out-Null
Write-Host "[lane] Run id: $runId"
Write-Host "[lane] Run root: $laneRunRoot"
Write-Host "[lane] Trace path: $traceArchivePath"
Write-Host "[lane] Mode: $(if ($EnableBlameHang) { 'forensic' } else { 'operational' })"

$arguments = @(
    "test",
    "test\Helper.Runtime.Certification.Compile.Tests\Helper.Runtime.Certification.Compile.Tests.csproj",
    "-c", $Configuration,
    "-m:1",
    "--disable-build-servers",
    "--results-directory", $laneResultsRoot,
    "--logger", "trx;LogFileName=certification_compile.trx"
)

if ($NoBuild)
{
    $arguments += "--no-build"
}

if ($NoRestore)
{
    $arguments += "--no-restore"
}

if ($EnableBlameHang)
{
    if ($BlameHangTimeoutSec -lt 120)
    {
        throw "Forensic blame timeout must be at least 120 seconds for certification compile runs."
    }

    $arguments += "--blame-hang"
    $arguments += "--blame-hang-timeout"
    $arguments += ("{0}s" -f $BlameHangTimeoutSec)
}

if (-not [string]::IsNullOrWhiteSpace($Filter))
{
    $arguments += "--filter"
    $arguments += $Filter
}

if ($ExtraArgs.Count -gt 0)
{
    $arguments += $ExtraArgs
}

$teardownSummary = $null
try
{
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = "dotnet"
    $startInfo.Arguments = Join-ProcessArguments -Values $arguments
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $false
    $startInfo.RedirectStandardError = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1"
    $startInfo.EnvironmentVariables["HELPER_CERTIFICATION_LANE_RUN_ID"] = $runId
    $startInfo.EnvironmentVariables["HELPER_CERTIFICATION_RUN_ROOT"] = $laneRunRoot
    $startInfo.EnvironmentVariables["HELPER_CERTIFICATION_PROCESS_TRACE_PATH"] = $traceArchivePath
    $startInfo.EnvironmentVariables["HELPER_DATA_ROOT"] = $laneDataRoot
    $startInfo.EnvironmentVariables["HELPER_LOGS_ROOT"] = $laneLogsRoot

    $laneProcess = New-Object System.Diagnostics.Process
    $laneProcess.StartInfo = $startInfo
    [void]$laneProcess.Start()
    Write-Host "[lane] Started certification compile lane PID=$($laneProcess.Id)"

    $maxWallTimeout = [TimeSpan]::FromSeconds((Read-PositiveIntFromEnv -Name "HELPER_CERTIFICATION_COMPILE_MAX_WALL_SEC" -Fallback 1800 -Min 30 -Max 86400))
    $idleTimeout = [TimeSpan]::FromSeconds((Read-PositiveIntFromEnv -Name "HELPER_CERTIFICATION_COMPILE_IDLE_TIMEOUT_SEC" -Fallback 180 -Min 15 -Max 3600))
    $heartbeatInterval = [TimeSpan]::FromSeconds((Read-PositiveIntFromEnv -Name "HELPER_CERTIFICATION_COMPILE_HEARTBEAT_SEC" -Fallback 30 -Min 5 -Max 300))
    $lastHeartbeatUtc = [DateTimeOffset]::UtcNow
    $lastObservedActivityUtc = [DateTimeOffset]::UtcNow
    $lastTraceWriteUtc = Get-LatestWriteUtcOrNull -Path $traceArchivePath
    if ($null -ne $lastTraceWriteUtc)
    {
        $lastObservedActivityUtc = [DateTimeOffset]$lastTraceWriteUtc
    }

    $lastResultsWriteUtc = Get-DirectoryLatestWriteUtcOrNull -Path $laneResultsRoot
    if ($null -ne $lastResultsWriteUtc -and [DateTimeOffset]$lastResultsWriteUtc -gt $lastObservedActivityUtc)
    {
        $lastObservedActivityUtc = [DateTimeOffset]$lastResultsWriteUtc
    }

    $lastCpuSnapshot = Get-ProcessCpuSnapshotOrNull -Process $laneProcess

    while (-not $laneProcess.HasExited)
    {
        Start-Sleep -Seconds 1
        $laneProcess.Refresh()

        $nowUtc = [DateTimeOffset]::UtcNow
        $traceWriteUtc = Get-LatestWriteUtcOrNull -Path $traceArchivePath
        if ($null -ne $traceWriteUtc)
        {
            $traceObservedAt = [DateTimeOffset]$traceWriteUtc
            if ($null -eq $lastTraceWriteUtc -or $traceWriteUtc -gt $lastTraceWriteUtc)
            {
                $lastTraceWriteUtc = $traceWriteUtc
                if ($traceObservedAt -gt $lastObservedActivityUtc)
                {
                    $lastObservedActivityUtc = $traceObservedAt
                }
            }
        }

        $resultsWriteUtc = Get-DirectoryLatestWriteUtcOrNull -Path $laneResultsRoot
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
            Write-Host "[lane] Certification compile lane still running after ${elapsed}s; last observed activity ${idle}s ago (PID=$($laneProcess.Id))"
            $lastHeartbeatUtc = $nowUtc
        }

        if (($nowUtc - $runStartedAtUtc) -ge $maxWallTimeout)
        {
            $preserveFailureArtifacts = $true
            throw "Certification compile lane exceeded max wall time of $([int]$maxWallTimeout.TotalSeconds)s. RunRoot=$laneRunRoot"
        }

        if (($nowUtc - $lastObservedActivityUtc) -ge $idleTimeout)
        {
            $preserveFailureArtifacts = $true
            $lastTraceLabel = if ($null -eq $lastTraceWriteUtc) { "none" } else { ([DateTimeOffset]$lastTraceWriteUtc).UtcDateTime.ToString("O") }
            throw "Certification compile lane stalled for $([int]$idleTimeout.TotalSeconds)s without trace/results/cpu activity. LastTraceWriteUtc=$lastTraceLabel RunRoot=$laneRunRoot"
        }
    }

    $laneProcess.WaitForExit()
    $laneProcess.Refresh()
    if ($laneProcess.ExitCode -ne 0)
    {
        $preserveFailureArtifacts = $true
        throw "Certification compile lane failed with exit code $($laneProcess.ExitCode)."
    }
}
catch
{
    $pendingError = $_
}
finally
{
    $teardownSummary = Stop-LaneProcesses -RootProcess $laneProcess -RunRoot $laneRunRoot
    if ($null -ne $teardownSummary -and -not $teardownSummary.rootExited)
    {
        $preserveFailureArtifacts = $true
        $teardownFailureMessage = "Certification compile lane root process PID=$($teardownSummary.rootProcessId) remained alive after teardown. RunRoot=$laneRunRoot"
    }

    if (-not $preserveFailureArtifacts)
    {
        foreach ($path in @($laneDataRoot, $laneResultsRoot))
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
        Write-Host "[cleanup] Preserving failure diagnostics under $laneRunRoot"
        $failureSummaryArtifact = Write-FailureSummaryArtifacts `
            -RunRoot $laneRunRoot `
            -StartedAtUtc $runStartedAtUtc `
            -TracePath $traceArchivePath `
            -ResultsRoot $laneResultsRoot `
            -PendingError $pendingError `
            -TeardownFailureMessage $teardownFailureMessage
        Write-FailureSummaryToConsole -FailureSummaryArtifact $failureSummaryArtifact
    }

    Remove-TempResidue -StartedAtUtc $runStartedAtUtc

    if (-not $preserveFailureArtifacts -and -not [string]::IsNullOrWhiteSpace($laneRunRoot) -and (Test-Path -LiteralPath $laneRunRoot))
    {
        try
        {
            $remaining = Get-ChildItem -LiteralPath $laneRunRoot -Force -ErrorAction SilentlyContinue
            if ($null -eq $remaining -or $remaining.Count -eq 0)
            {
                Remove-Item -LiteralPath $laneRunRoot -Force -ErrorAction Stop
                Write-Host "[cleanup] Removed empty run root $laneRunRoot"
            }
        }
        catch
        {
            Write-Host "[cleanup] Failed to remove empty run root $laneRunRoot : $($_.Exception.Message)"
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
