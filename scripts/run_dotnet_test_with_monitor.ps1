param(
    [Parameter(Mandatory = $true)][string]$Target,
    [string[]]$Arguments = @("--no-build"),
    [string]$LogPath = "temp/verification/dotnet_test.log",
    [string]$ErrorLogPath = "temp/verification/dotnet_test.stderr.log",
    [string]$StatusPath = "temp/verification/dotnet_test_status.json",
    [int]$MaxDurationSec = 0,
    [switch]$ShowProcessMonitor,
    [int]$ProcessMonitorRefreshSec = 2
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
        Remove-Item $stalePath -Force
    }
}

$dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
}
if ($null -eq $dotnetCommand) {
    throw "[DotnetTest] dotnet executable not found."
}

$argumentList = @("test", $Target) + @($Arguments)
$commandDisplay = ("dotnet " + ($argumentList -join " "))
$artifacts = [ordered]@{
    logPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedLogPath
    errorLogPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedErrorLogPath
    statusPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedStatusPath
}
$startedAtUtc = (Get-Date).ToUniversalTime()

Write-TestStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "starting" -Outcome "RUNNING" -Details "Launching dotnet test." -CommandDisplay $commandDisplay -Artifacts $artifacts
Write-Host ("[DotnetTest][START] {0}" -f $commandDisplay) -ForegroundColor Cyan

$process = Start-Process -FilePath $dotnetCommand.Source `
    -ArgumentList $argumentList `
    -WorkingDirectory $repoRoot `
    -RedirectStandardOutput $resolvedLogPath `
    -RedirectStandardError $resolvedErrorLogPath `
    -PassThru

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
    exit 124
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

if ($exitCode -eq 0) {
    Write-TestStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "completed" -Outcome "PASS" -Details "dotnet test completed successfully." -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id -ExitCode $exitCode
    Write-Host "[DotnetTest][PASS] dotnet test completed successfully." -ForegroundColor Green
    exit 0
}

$failureDetails = if ([string]::IsNullOrWhiteSpace($stderrTail)) {
    ("dotnet test exited with code {0}." -f $exitCode)
}
else {
    ("dotnet test exited with code {0}. stderr: {1}" -f $exitCode, $stderrTail)
}

Write-TestStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "failed" -Outcome "FAIL" -Details $failureDetails -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id -ExitCode $exitCode
Write-Host ("[DotnetTest][FAIL] {0}" -f $failureDetails) -ForegroundColor Red
exit $exitCode
