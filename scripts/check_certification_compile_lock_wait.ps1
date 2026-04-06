param(
    [string]$Configuration = "Debug",
    [string]$Filter = "FullyQualifiedName~DotnetServiceTraceBehaviorTests",
    [int]$FirstLaunchDelaySec = 1,
    [int]$LockWaitSec = 180,
    [int]$LockPollSec = 1
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$laneScript = Join-Path $PSScriptRoot "run_certification_compile_tests.ps1"
$artifactRoot = Join-Path $repoRoot "temp\certification_compile_lock_wait_check"
$timestamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$runRoot = Join-Path $artifactRoot $timestamp
$firstOutputPath = Join-Path $runRoot "first_run.log"
$secondOutputPath = Join-Path $runRoot "second_run.log"

New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

function Join-ArgumentString {
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

function Start-LaneProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $arguments = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $laneScript,
        "-Configuration", $Configuration,
        "-NoBuild",
        "-NoRestore",
        "-Filter", $Filter
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = "powershell"
    $startInfo.Arguments = Join-ArgumentString -Values $arguments
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.EnvironmentVariables["HELPER_CERTIFICATION_COMPILE_LOCK_WAIT_SEC"] = [string]$LockWaitSec
    $startInfo.EnvironmentVariables["HELPER_CERTIFICATION_COMPILE_LOCK_POLL_SEC"] = [string]$LockPollSec

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()

    return [PSCustomObject]@{
        Label = $Label
        Process = $process
    }
}

function Wait-LaneProcess {
    param(
        [Parameter(Mandatory = $true)]
        $Handle
    )

    $captured = $Handle.Process.StandardOutput.ReadToEnd()
    $stderr = $Handle.Process.StandardError.ReadToEnd()
    $Handle.Process.WaitForExit()
    $Handle.Process.WaitForExit()
    if (-not [string]::IsNullOrWhiteSpace($stderr))
    {
        if ([string]::IsNullOrWhiteSpace($captured))
        {
            $captured = $stderr
        }
        else
        {
            $captured = $captured + [Environment]::NewLine + $stderr
        }
    }

    return [PSCustomObject]@{
        Label = $Handle.Label
        ExitCode = $Handle.Process.ExitCode
        Output = $captured
    }
}

function Stop-LaneProcessIfNeeded {
    param(
        $Handle
    )

    if ($null -eq $Handle)
    {
        return
    }

    try
    {
        if (-not $Handle.Process.HasExited)
        {
            $Handle.Process.Kill($true)
        }
    }
    catch
    {
        # best effort
    }
}

function Get-RunId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    $match = [regex]::Match($Output, "^\[lane\] Run id: (?<runId>.+)$", [System.Text.RegularExpressions.RegexOptions]::Multiline)
    if ($match.Success)
    {
        return $match.Groups["runId"].Value.Trim()
    }

    return $null
}

$firstHandle = $null
$secondHandle = $null

try
{
    $firstHandle = Start-LaneProcess -Label "first"
    Start-Sleep -Seconds $FirstLaunchDelaySec
    $secondHandle = Start-LaneProcess -Label "second"

    $firstResult = Wait-LaneProcess -Handle $firstHandle
    $secondResult = Wait-LaneProcess -Handle $secondHandle

    Set-Content -Path $firstOutputPath -Value $firstResult.Output
    Set-Content -Path $secondOutputPath -Value $secondResult.Output

    if ($firstResult.ExitCode -ne 0)
    {
        throw "First compile-lane invocation failed with exit code $($firstResult.ExitCode). See $firstOutputPath"
    }

    if ($secondResult.ExitCode -ne 0)
    {
        throw "Second compile-lane invocation failed with exit code $($secondResult.ExitCode). See $secondOutputPath"
    }

    if ($secondResult.Output -notmatch "Waiting for active certification compile lane to release lock")
    {
        throw "Second compile-lane invocation did not report waiting for the active lock. See $secondOutputPath"
    }

    $firstRunId = Get-RunId -Output $firstResult.Output
    $secondRunId = Get-RunId -Output $secondResult.Output
    if ([string]::IsNullOrWhiteSpace($firstRunId) -or [string]::IsNullOrWhiteSpace($secondRunId))
    {
        throw "Run id was not observed in one of the compile-lane outputs. See $runRoot"
    }

    if ($firstRunId -eq $secondRunId)
    {
        throw "Both compile-lane invocations reported the same run id. See $runRoot"
    }

    Write-Host "[lock-check] Passed"
    Write-Host "[lock-check] First run id: $firstRunId"
    Write-Host "[lock-check] Second run id: $secondRunId"
    Write-Host "[lock-check] Artifacts: $runRoot"
}
finally
{
    Stop-LaneProcessIfNeeded -Handle $firstHandle
    Stop-LaneProcessIfNeeded -Handle $secondHandle
}
