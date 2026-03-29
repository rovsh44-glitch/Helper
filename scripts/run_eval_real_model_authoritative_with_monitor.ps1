param(
    [string]$ApiBase = "http://127.0.0.1:5239",
    [string]$DatasetPath = "eval/human_level_parity_ru_en.jsonl",
    [int]$MaxScenarios = 200,
    [int]$MinScenarioCount = 200,
    [string]$OutputReport = "temp/verification/live_authoritative_eval.md",
    [string]$StatusPath = "temp/verification/live_authoritative_eval_status.json",
    [string]$LogPath = "temp/verification/live_authoritative_eval.log",
    [string]$ErrorLogPath = "temp/verification/live_authoritative_eval.stderr.log",
    [string]$QdrantBase = "http://localhost:6333",
    [string]$SessionToken = "",
    [int]$SessionTtlMinutes = 240,
    [string]$ApiKey = "",
    [int]$RequestTimeoutSec = 90,
    [int]$RetryBackoffMs = 750,
    [int]$ProgressEvery = 10,
    [switch]$LaunchLocalApiIfUnavailable,
    [string]$ApiRuntimeDir = "",
    [int]$ReadinessTimeoutSec = 600,
    [int]$ReadinessPollIntervalMs = 2000,
    [switch]$WaiveKnowledgePreflight,
    [switch]$SkipWarmup,
    [switch]$ShowProcessMonitor,
    [int]$ProcessMonitorRefreshSec = 2,
    [int]$MaxDurationSec = 0
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

function Write-AuthoritativeEvalStatusFile {
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
        currentStep = [ordered]@{
            id = "authoritativeEval"
            state = $Phase
            elapsedMs = [int]([datetime]::UtcNow - $StartedAtUtc).TotalMilliseconds
            title = "Authoritative real-model eval"
            details = $Details
        }
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
        Write-Host "[AuthoritativeEval] Process monitor window unavailable: powershell.exe not found." -ForegroundColor DarkYellow
        return
    }

    $quotedMonitorScriptPath = '"' + $MonitorScriptPath + '"'
    $quotedTitle = '"HELPER authoritative eval Monitor"'
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
    Write-Host "[AuthoritativeEval] Process monitor window started." -ForegroundColor DarkCyan
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

function Get-AuthoritativeSummaryOrNull {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    try {
        return Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedOutputReport = if ([System.IO.Path]::IsPathRooted($OutputReport)) { $OutputReport } else { Join-Path $repoRoot $OutputReport }
$resolvedLogPath = if ([System.IO.Path]::IsPathRooted($LogPath)) { $LogPath } else { Join-Path $repoRoot $LogPath }
$resolvedErrorLogPath = if ([System.IO.Path]::IsPathRooted($ErrorLogPath)) { $ErrorLogPath } else { Join-Path $repoRoot $ErrorLogPath }
$resolvedStatusPath = if ([System.IO.Path]::IsPathRooted($StatusPath)) { $StatusPath } else { Join-Path $repoRoot $StatusPath }
$resolvedMonitorScriptPath = Join-Path $PSScriptRoot "watch_baseline_capture_processes.ps1"
$resolvedAuthoritativeSummaryJsonPath = [System.IO.Path]::ChangeExtension($resolvedOutputReport, ".authoritative.json")
$resolvedAuthoritativeSummaryMarkdownPath = [System.IO.Path]::ChangeExtension($resolvedOutputReport, ".authoritative.md")
$resolvedReadinessReportPath = [System.IO.Path]::ChangeExtension($resolvedOutputReport, ".api_ready.md")
$resolvedDatasetValidationReportPath = [System.IO.Path]::ChangeExtension($resolvedOutputReport, ".dataset_validation.md")
$resolvedKnowledgeReportPath = [System.IO.Path]::ChangeExtension($resolvedOutputReport, ".knowledge_preflight.md")

foreach ($path in @($resolvedOutputReport, $resolvedLogPath, $resolvedErrorLogPath, $resolvedStatusPath, $resolvedAuthoritativeSummaryJsonPath, $resolvedAuthoritativeSummaryMarkdownPath, $resolvedReadinessReportPath, $resolvedDatasetValidationReportPath, $resolvedKnowledgeReportPath)) {
    $directory = Split-Path -Parent $path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
}

foreach ($stalePath in @($resolvedOutputReport, $resolvedLogPath, $resolvedErrorLogPath, $resolvedStatusPath, $resolvedAuthoritativeSummaryJsonPath, $resolvedAuthoritativeSummaryMarkdownPath, $resolvedReadinessReportPath, $resolvedDatasetValidationReportPath, $resolvedKnowledgeReportPath)) {
    if (Test-Path $stalePath) {
        Remove-Item $stalePath -Force
    }
}

$powershellCommand = Get-Command powershell.exe -ErrorAction SilentlyContinue
if ($null -eq $powershellCommand) {
    $powershellCommand = Get-Command powershell -ErrorAction SilentlyContinue
}
if ($null -eq $powershellCommand) {
    throw "[AuthoritativeEval] powershell executable not found."
}

$argumentList = @(
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "run_eval_real_model_authoritative.ps1"),
    "-ApiBase", $ApiBase,
    "-DatasetPath", $DatasetPath,
    "-MaxScenarios", $MaxScenarios,
    "-MinScenarioCount", $MinScenarioCount,
    "-OutputReport", $resolvedOutputReport,
    "-QdrantBase", $QdrantBase,
    "-SessionTtlMinutes", $SessionTtlMinutes,
    "-RequestTimeoutSec", $RequestTimeoutSec,
    "-RetryBackoffMs", $RetryBackoffMs,
    "-ProgressEvery", $ProgressEvery,
    "-ReadinessTimeoutSec", $ReadinessTimeoutSec,
    "-ReadinessPollIntervalMs", $ReadinessPollIntervalMs,
    "-AuthoritativeSummaryJsonPath", $resolvedAuthoritativeSummaryJsonPath,
    "-AuthoritativeSummaryMarkdownPath", $resolvedAuthoritativeSummaryMarkdownPath
)

if (-not [string]::IsNullOrWhiteSpace($SessionToken)) {
    $argumentList += @("-SessionToken", $SessionToken)
}

if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $argumentList += @("-ApiKey", $ApiKey)
}

if ($LaunchLocalApiIfUnavailable.IsPresent) {
    $argumentList += "-LaunchLocalApiIfUnavailable"
}

if (-not [string]::IsNullOrWhiteSpace($ApiRuntimeDir)) {
    $argumentList += @("-ApiRuntimeDir", $ApiRuntimeDir)
}

if ($WaiveKnowledgePreflight.IsPresent) {
    $argumentList += "-WaiveKnowledgePreflight"
}

if ($SkipWarmup.IsPresent) {
    $argumentList += "-SkipWarmup"
}

$displayArguments = @(
    $argumentList |
        ForEach-Object { [string]$_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object {
            if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
        }
)
$commandDisplay = "powershell " + ($displayArguments -join " ")
$artifacts = [ordered]@{
    outputReportPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedOutputReport
    authoritativeSummaryJsonPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedAuthoritativeSummaryJsonPath
    authoritativeSummaryMarkdownPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedAuthoritativeSummaryMarkdownPath
    readinessReportPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedReadinessReportPath
    datasetValidationReportPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedDatasetValidationReportPath
    knowledgePreflightReportPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedKnowledgeReportPath
    logPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedLogPath
    errorLogPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedErrorLogPath
    statusPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedStatusPath
}
$startedAtUtc = (Get-Date).ToUniversalTime()

Write-AuthoritativeEvalStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "starting" -Outcome "RUNNING" -Details "Launching authoritative real-model eval." -CommandDisplay $commandDisplay -Artifacts $artifacts
Write-Host ("[AuthoritativeEval][START] {0}" -f $commandDisplay) -ForegroundColor Cyan

$process = Start-Process -FilePath $powershellCommand.Source `
    -ArgumentList $argumentList `
    -WorkingDirectory $repoRoot `
    -RedirectStandardOutput $resolvedLogPath `
    -RedirectStandardError $resolvedErrorLogPath `
    -PassThru

Write-AuthoritativeEvalStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "running" -Outcome "RUNNING" -Details "Authoritative real-model eval is running." -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id

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
            $timeoutDetails = "Authoritative real-model eval timed out after $MaxDurationSec seconds."
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

    Write-AuthoritativeEvalStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "running" -Outcome "RUNNING" -Details "Authoritative real-model eval is running." -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id
}

if ($timedOut) {
    Write-AuthoritativeEvalStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "failed" -Outcome "FAIL" -Details $timeoutDetails -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id -ExitCode 124
    Write-Host ("[AuthoritativeEval][FAIL] {0}" -f $timeoutDetails) -ForegroundColor Red
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

$summary = Get-AuthoritativeSummaryOrNull -Path $resolvedAuthoritativeSummaryJsonPath
$failureDetails = ""
if ($null -ne $summary) {
    $failureDetails = if ($summary.status -eq "PASS") {
        "Authoritative real-model eval completed successfully."
    }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$summary.coreFailureMessage)) {
        [string]$summary.coreFailureMessage
    }
    else {
        "Authoritative gate failed."
    }
}

$summaryPass = ($null -ne $summary) -and ([string]$summary.status -eq "PASS")
if (($exitCode -eq 0) -and $summaryPass) {
    Write-AuthoritativeEvalStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "completed" -Outcome "PASS" -Details "Authoritative real-model eval completed successfully." -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id -ExitCode $exitCode
    Write-Host "[AuthoritativeEval][PASS] Authoritative real-model eval completed successfully." -ForegroundColor Green
    exit 0
}

if ([string]::IsNullOrWhiteSpace($failureDetails)) {
    $stderrTail = if (Test-Path $resolvedErrorLogPath) {
        (@(Get-Content -Path $resolvedErrorLogPath -Tail 5 -ErrorAction SilentlyContinue) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) -join " | "
    }
    else {
        ""
    }

    $failureDetails = if ([string]::IsNullOrWhiteSpace($stderrTail)) {
        ("Authoritative real-model eval exited with code {0}." -f $exitCode)
    }
    else {
        ("Authoritative real-model eval exited with code {0}. stderr: {1}" -f $exitCode, $stderrTail)
    }
}

Write-AuthoritativeEvalStatusFile -ResolvedStatusPath $resolvedStatusPath -StartedAtUtc $startedAtUtc -Phase "failed" -Outcome "FAIL" -Details $failureDetails -CommandDisplay $commandDisplay -Artifacts $artifacts -ProcessId $process.Id -ExitCode $exitCode
Write-Host ("[AuthoritativeEval][FAIL] {0}" -f $failureDetails) -ForegroundColor Red
$failureExitCode = if ($exitCode -eq 0) { 1 } else { $exitCode }
exit $failureExitCode
