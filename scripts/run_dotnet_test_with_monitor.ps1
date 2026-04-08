param(
    [Parameter(Mandatory = $true)][string]$Target,
    [string[]]$Arguments = @("--no-build"),
    [string]$ArgumentsJsonPath = "",
    [string]$LogPath = "temp/verification/dotnet_test.log",
    [string]$ErrorLogPath = "temp/verification/dotnet_test.stderr.log",
    [string]$StatusPath = "temp/verification/dotnet_test_status.json",
    [int]$MaxDurationSec = 0,
    [switch]$ShowProcessMonitor,
    [int]$ProcessMonitorRefreshSec = 2,
    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-RepoRelativePathOrOriginal {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    if (-not $fullRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar) -and -not $fullRoot.EndsWith([System.IO.Path]::AltDirectorySeparatorChar)) {
        $fullRoot += [System.IO.Path]::DirectorySeparatorChar
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($fullRoot.Length).Replace('\', '/')
    }

    return $fullPath
}

function Write-TestStatusFile {
    param(
        [Parameter(Mandatory = $true)][string]$ResolvedStatusPath,
        [Parameter(Mandatory = $true)][datetime]$StartedAtUtc,
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][string]$Outcome,
        [Parameter(Mandatory = $true)][string]$Details,
        [Parameter(Mandatory = $true)][string]$CommandDisplay,
        [Parameter(Mandatory = $true)][hashtable]$Artifacts,
        [AllowNull()][int]$ProcessId = $null,
        [AllowNull()][int]$ExitCode = $null
    )

    $payload = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        startedAtUtc = $StartedAtUtc.ToString("o")
        lastHeartbeatUtc = (Get-Date).ToUniversalTime().ToString("o")
        phase = $Phase
        outcome = $Outcome
        details = $Details
        command = $CommandDisplay
        processId = $ProcessId
        exitCode = $ExitCode
        artifacts = $Artifacts
    }

    Set-Content -Path $ResolvedStatusPath -Value ($payload | ConvertTo-Json -Depth 10) -Encoding UTF8
}

function Start-ProcessMonitorWindow {
    param(
        [Parameter(Mandatory = $true)][int]$RootProcessId,
        [Parameter(Mandatory = $true)][string]$MonitorScriptPath,
        [Parameter(Mandatory = $true)][string]$ResolvedStatusPath,
        [Parameter(Mandatory = $true)][string]$ResolvedLogPath,
        [Parameter(Mandatory = $true)][int]$RefreshSec
    )

    $powershellCommand = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($null -eq $powershellCommand) {
        Write-Host "[DotnetTest] Process monitor window unavailable: powershell.exe not found." -ForegroundColor DarkYellow
        return
    }

    $quotedMonitorScriptPath = '"' + $MonitorScriptPath + '"'
    $quotedTitle = '"HELPER dotnet test Monitor"'
    $quotedStatusPath = '"' + $ResolvedStatusPath + '"'
    $quotedLogPath = '"' + $ResolvedLogPath + '"'
    $arguments = @(
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-File", $quotedMonitorScriptPath,
        "-Title", $quotedTitle,
        "-RootProcessId", $RootProcessId,
        "-StatusPath", $quotedStatusPath,
        "-LogPath", $quotedLogPath,
        "-RefreshSec", $RefreshSec
    )

    Start-Process -FilePath $powershellCommand.Source -ArgumentList $arguments | Out-Null
    Write-Host "[DotnetTest] Process monitor window started." -ForegroundColor DarkCyan
}

function Stop-ProcessTree {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    $taskkill = Get-Command taskkill.exe -ErrorAction SilentlyContinue
    if ($null -ne $taskkill) {
        & $taskkill.Source /PID $ProcessId /T /F | Out-Null
        return
    }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

function Complete-Wrapper {
    param(
        [Parameter(Mandatory = $true)][int]$ExitCode,
        [Parameter(Mandatory = $true)][switch]$ReturnExitCode
    )

    if ($ReturnExitCode) {
        $global:LASTEXITCODE = $ExitCode
        return $ExitCode
    }

    exit $ExitCode
}

function Set-ProcessEnvironmentVariables {
    param([hashtable]$Values)

    $snapshot = @{}
    foreach ($entry in $Values.GetEnumerator()) {
        $snapshot[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
    }

    return $snapshot
}

function Restore-ProcessEnvironmentVariables {
    param([hashtable]$Snapshot)

    foreach ($entry in $Snapshot.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
    }
}

function Test-OutputContainsFailureSignal {
    param(
        [string]$ResolvedLogPath,
        [string]$ResolvedErrorLogPath
    )

    $patterns = @(
        '(?m)^\s*Failed!\s+-\s+Failed:',
        '\[FAIL\]'
    )

    foreach ($path in @($ResolvedLogPath, $ResolvedErrorLogPath)) {
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        $content = Get-Content -LiteralPath $path -Raw -Encoding utf8 -ErrorAction SilentlyContinue
        if ([string]::IsNullOrWhiteSpace($content)) {
            continue
        }

        foreach ($pattern in $patterns) {
            if ([System.Text.RegularExpressions.Regex]::IsMatch($content, $pattern)) {
                return $true
            }
        }
    }

    return $false
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedLogPath = if ([System.IO.Path]::IsPathRooted($LogPath)) { $LogPath } else { Join-Path $repoRoot $LogPath }
$resolvedErrorLogPath = if ([System.IO.Path]::IsPathRooted($ErrorLogPath)) { $ErrorLogPath } else { Join-Path $repoRoot $ErrorLogPath }
$resolvedStatusPath = if ([System.IO.Path]::IsPathRooted($StatusPath)) { $StatusPath } else { Join-Path $repoRoot $StatusPath }
$resolvedMonitorScriptPath = Join-Path $PSScriptRoot "watch_baseline_capture_processes.ps1"

foreach ($path in @($resolvedLogPath, $resolvedErrorLogPath, $resolvedStatusPath)) {
    $directory = Split-Path -Parent $path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
}

foreach ($stalePath in @($resolvedLogPath, $resolvedErrorLogPath, $resolvedStatusPath)) {
    if (Test-Path $stalePath) {
        Set-Content -Path $stalePath -Value $null -Encoding UTF8 -Force
    }
}

$dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
}
if ($null -eq $dotnetCommand) {
    throw "[DotnetTest] dotnet executable not found."
}

$resolvedArguments = @($Arguments)
if (-not [string]::IsNullOrWhiteSpace($ArgumentsJsonPath)) {
    $resolvedArgumentsPath = if ([System.IO.Path]::IsPathRooted($ArgumentsJsonPath)) { $ArgumentsJsonPath } else { Join-Path $repoRoot $ArgumentsJsonPath }
    if (-not (Test-Path -LiteralPath $resolvedArgumentsPath)) {
        throw ("[DotnetTest] Arguments json not found: {0}" -f $resolvedArgumentsPath)
    }

    $decodedArguments = Get-Content -LiteralPath $resolvedArgumentsPath -Raw -Encoding utf8 | ConvertFrom-Json
    $resolvedArguments = @($decodedArguments | ForEach-Object { [string]$_ })
}

$argumentList = @("test", $Target) + @($resolvedArguments)
$commandDisplay = ("dotnet " + ($argumentList -join " "))
$artifacts = [ordered]@{
    logPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedLogPath
    errorLogPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedErrorLogPath
    statusPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedStatusPath
}
$startedAtUtc = (Get-Date).ToUniversalTime()

Write-TestStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "starting" -Outcome "RUNNING" -Details "Launching dotnet test." -CommandDisplay $commandDisplay -Artifacts $artifacts
Write-Host ("[DotnetTest][START] {0}" -f $commandDisplay) -ForegroundColor Cyan

$environmentSnapshot = $null
try {
    $dotnetRuntimeEnvironment = @{
        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
        DOTNET_NOLOGO = "1"
    }
    $environmentSnapshot = Set-ProcessEnvironmentVariables -Values $dotnetRuntimeEnvironment
    $process = Start-Process -FilePath $dotnetCommand.Source `
        -ArgumentList $argumentList `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $resolvedLogPath `
        -RedirectStandardError $resolvedErrorLogPath `
        -PassThru
}
finally {
    if ($null -ne $environmentSnapshot) {
        Restore-ProcessEnvironmentVariables -Snapshot $environmentSnapshot
    }
}

Write-TestStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "running" -Outcome "RUNNING" -Details "dotnet test is running." -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id

if ($ShowProcessMonitor) {
    Start-ProcessMonitorWindow -RootProcessId $process.Id -MonitorScriptPath $resolvedMonitorScriptPath -ResolvedStatusPath $resolvedStatusPath -ResolvedLogPath $resolvedLogPath -RefreshSec $ProcessMonitorRefreshSec
}

$timedOut = $false
$timeoutDetails = ""
while (-not $process.HasExited) {
    Start-Sleep -Seconds 2
    try {
        $process.Refresh()
    }
    catch {
    }

    if ($MaxDurationSec -gt 0) {
        $elapsedSeconds = [int]([datetime]::UtcNow - $startedAtUtc).TotalSeconds
        if ($elapsedSeconds -ge $MaxDurationSec) {
            $timedOut = $true
            $timeoutDetails = "dotnet test timed out after $MaxDurationSec seconds."
            Add-Content -Path $resolvedErrorLogPath -Value $timeoutDetails -Encoding UTF8 -ErrorAction SilentlyContinue
            Stop-ProcessTree -ProcessId $process.Id
            Start-Sleep -Seconds 2
            try {
                $process.Refresh()
            }
            catch {
            }
            break
        }
    }

    Write-TestStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "running" -Outcome "RUNNING" -Details "dotnet test is running." -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id
}

if ($timedOut) {
    Write-TestStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "failed" -Outcome "FAIL" -Details $timeoutDetails -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id -ExitCode 124
    Write-Host ("[DotnetTest][FAIL] {0}" -f $timeoutDetails) -ForegroundColor Red
    Complete-Wrapper -ExitCode 124 -ReturnExitCode:$ReturnExitCode
    return
}

try {
    $process.WaitForExit()
}
catch {
}

try {
    $process.Refresh()
}
catch {
}

$exitCode = -1
try {
    $exitCode = [int]$process.ExitCode
}
catch {
}

$stderrTail = if (Test-Path $resolvedErrorLogPath) {
    (@(Get-Content -Path $resolvedErrorLogPath -Tail 5 -ErrorAction SilentlyContinue) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) -join " | "
}
else {
    ""
}

if ($exitCode -eq 0 -and -not (Test-OutputContainsFailureSignal -ResolvedLogPath $resolvedLogPath -ResolvedErrorLogPath $resolvedErrorLogPath)) {
    Write-TestStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "completed" -Outcome "PASS" -Details "dotnet test completed successfully." -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id -ExitCode $exitCode
    Write-Host "[DotnetTest][PASS] dotnet test completed successfully." -ForegroundColor Green
    Complete-Wrapper -ExitCode 0 -ReturnExitCode:$ReturnExitCode
    return
}

$effectiveExitCode = if ($exitCode -eq 0) { 1 } else { $exitCode }

$failureDetails = if ($exitCode -eq 0) {
    "dotnet test emitted failure markers in stdout/stderr despite zero exit code."
}
elseif ([string]::IsNullOrWhiteSpace($stderrTail)) {
    ("dotnet test exited with code {0}." -f $exitCode)
}
else {
    ("dotnet test exited with code {0}. stderr: {1}" -f $exitCode, $stderrTail)
}

Write-TestStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "failed" -Outcome "FAIL" -Details $failureDetails -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id -ExitCode $effectiveExitCode
Write-Host ("[DotnetTest][FAIL] {0}" -f $failureDetails) -ForegroundColor Red
Complete-Wrapper -ExitCode $effectiveExitCode -ReturnExitCode:$ReturnExitCode
