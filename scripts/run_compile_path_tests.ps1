[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [string]$Filter = "",
    [switch]$NoBuild,
    [switch]$NoRestore
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

$arguments = @(
    "test"
    "test\\Helper.Runtime.CompilePath.Tests\\Helper.Runtime.CompilePath.Tests.csproj"
    "-c"
    $Configuration
    "-m:1"
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
    $startInfo.EnvironmentVariables["HELPER_COMPILE_PATH_LANE_RUN_ID"] = $runId
    $startInfo.EnvironmentVariables["HELPER_COMPILE_PATH_RUN_ROOT"] = $runRoot

    $laneProcess = New-Object System.Diagnostics.Process
    $laneProcess.StartInfo = $startInfo
    [void]$laneProcess.Start()

    $stdoutTask = $laneProcess.StandardOutput.ReadToEndAsync()
    $stderrTask = $laneProcess.StandardError.ReadToEndAsync()
    $lastHeartbeatUtc = [DateTimeOffset]::UtcNow
    Write-Host "[lane] Started compile-path lane PID=$($laneProcess.Id)"

    while (-not $laneProcess.HasExited)
    {
        Start-Sleep -Seconds 1
        $laneProcess.Refresh()

        if (([DateTimeOffset]::UtcNow - $lastHeartbeatUtc) -ge [TimeSpan]::FromSeconds(30))
        {
            $elapsed = [DateTimeOffset]::UtcNow - $runStartedAtUtc
            Write-Host "[lane] Compile-path lane still running after $([int]$elapsed.TotalSeconds)s (PID=$($laneProcess.Id))"
            $lastHeartbeatUtc = [DateTimeOffset]::UtcNow
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
        throw "Compile-path tests failed with exit code $($laneProcess.ExitCode)."
    }
}
finally {
    Stop-LaneProcesses -RootProcess $laneProcess
    Start-Sleep -Seconds 2

    if (-not [string]::IsNullOrWhiteSpace($resultsRoot))
    {
        try
        {
            Remove-DirectoryIfPresent -PathToRemove $resultsRoot
        }
        catch
        {
            Write-Host "[cleanup] Failed to remove $resultsRoot : $($_.Exception.Message)"
        }
    }

    Remove-TempResidue -StartedAtUtc $runStartedAtUtc
    Release-LaneLock -LockHandle $laneLock
}
