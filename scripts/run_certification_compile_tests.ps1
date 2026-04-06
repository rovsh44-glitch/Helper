param(
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore,
    [string]$Filter = "",
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
        [System.Diagnostics.Process]$RootProcess
    )

    if ($null -eq $RootProcess)
    {
        return
    }

    $rootId = $null
    try
    {
        $rootId = $RootProcess.Id
    }
    catch
    {
        return
    }

    $killedByTaskKill = $false
    try
    {
        & taskkill /PID $rootId /T /F *> $null
        if ($LASTEXITCODE -eq 0)
        {
            $killedByTaskKill = $true
            Write-Host "[cleanup] Stopped process tree rooted at PID=$rootId via taskkill /T"
        }
    }
    catch
    {
        # fall through to direct process stop
    }

    if ($killedByTaskKill)
    {
        Start-Sleep -Seconds 1
        return
    }

    try
    {
        $RootProcess.Refresh()
        if (-not $RootProcess.HasExited)
        {
            Stop-Process -Id $rootId -Force -ErrorAction Stop
            Write-Host "[cleanup] Stopped root process PID=$rootId via Stop-Process fallback"
        }
    }
    catch
    {
        # best effort
    }
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

$arguments = @(
    "test",
    "test\Helper.Runtime.Certification.Compile.Tests\Helper.Runtime.Certification.Compile.Tests.csproj",
    "-c", $Configuration,
    "-m:1",
    "--results-directory", $laneResultsRoot
)

if ($NoBuild)
{
    $arguments += "--no-build"
}

if ($NoRestore)
{
    $arguments += "--no-restore"
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
    $startInfo.EnvironmentVariables["HELPER_CERTIFICATION_LANE_RUN_ID"] = $runId
    $startInfo.EnvironmentVariables["HELPER_CERTIFICATION_RUN_ROOT"] = $laneRunRoot
    $startInfo.EnvironmentVariables["HELPER_CERTIFICATION_PROCESS_TRACE_PATH"] = $traceArchivePath
    $startInfo.EnvironmentVariables["HELPER_DATA_ROOT"] = $laneDataRoot
    $startInfo.EnvironmentVariables["HELPER_LOGS_ROOT"] = $laneLogsRoot

    $laneProcess = New-Object System.Diagnostics.Process
    $laneProcess.StartInfo = $startInfo
    [void]$laneProcess.Start()
    Write-Host "[lane] Started certification compile lane PID=$($laneProcess.Id)"

    while (-not $laneProcess.HasExited)
    {
        Start-Sleep -Seconds 1
        $laneProcess.Refresh()
    }

    $laneProcess.WaitForExit()
    $laneProcess.Refresh()
    if ($laneProcess.ExitCode -ne 0)
    {
        throw "Certification compile lane failed with exit code $($laneProcess.ExitCode)."
    }
}
finally
{
    Stop-LaneProcesses -RootProcess $laneProcess

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

    Remove-TempResidue -StartedAtUtc $runStartedAtUtc

    if (-not [string]::IsNullOrWhiteSpace($laneRunRoot) -and (Test-Path -LiteralPath $laneRunRoot))
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
