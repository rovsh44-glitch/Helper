param(
    [string]$ApiBaseUrl = "",
    [string]$UiUrl = "",
    [string]$OutputJsonPath = "doc/certification/active/CURRENT_RELEASE_BASELINE.json",
    [string]$OutputMarkdownPath = "doc/certification/active/CURRENT_RELEASE_BASELINE.md",
    [string]$UiSmokeJsonPath = "temp/verification/ui_workflow_smoke.json",
    [string]$LogPath = "temp/verification/baseline_capture.log",
    [string]$StatusPath = "temp/verification/baseline_capture_status.json",
    [string]$UiSmokeWorkspaceRoot = "",
    [int]$HeartbeatIntervalSec = 5,
    [switch]$ShowProcessMonitor,
    [int]$ProcessMonitorRefreshSec = 2,
    [switch]$SkipUiSmoke,
    [switch]$RefreshActiveGateSnapshot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-StepProgress {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("START", "PASS", "FAIL")] [string]$State,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Title,
        [string]$Details = ""
    )

    $timestamp = (Get-Date).ToString("HH:mm:ss")
    $message = "[Baseline][$timestamp][$State][$Id] $Title"
    if (-not [string]::IsNullOrWhiteSpace($Details)) {
        $message += " :: $Details"
    }

    $color = switch ($State) {
        "START" { "Cyan" }
        "PASS" { "Green" }
        default { "Red" }
    }

    Write-Host $message -ForegroundColor $color
    if (-not [string]::IsNullOrWhiteSpace($script:ResolvedLogPath)) {
        Add-Content -Path $script:ResolvedLogPath -Value $message -Encoding UTF8 -ErrorAction SilentlyContinue
    }
}

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

function Read-JsonFileOrNull {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    return Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-OptionalObjectPropertyValue {
    param(
        [AllowNull()]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) {
            return $Object[$Name]
        }

        return $null
    }

    $property = @($Object.PSObject.Properties | Where-Object { $_.Name -eq $Name }) | Select-Object -First 1
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Convert-ArtifactStatusToken {
    param([AllowNull()][string]$Status)

    $normalized = ([string]$Status).Trim().ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return "UNKNOWN"
    }

    switch ($normalized) {
        "PASS" { return "PASS" }
        "FAIL" { return "FAIL" }
        "SKIPPED" { return "SKIPPED" }
        default { return $normalized }
    }
}

function New-GateResult {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Evidence,
        [Parameter(Mandatory = $true)][int]$DurationMs,
        [Parameter(Mandatory = $true)][string]$Details
    )

    return [pscustomobject][ordered]@{
        id = $Id
        title = $Title
        status = $Status
        evidence = $Evidence
        durationMs = $DurationMs
        details = $Details
    }
}

function Write-BaselineStatusFile {
    param(
        [string]$Phase,
        [string]$Outcome,
        [string]$Details
    )

    if ($PSBoundParameters.ContainsKey("Phase") -and -not [string]::IsNullOrWhiteSpace($Phase)) {
        $script:BaselinePhase = $Phase
    }
    if ($PSBoundParameters.ContainsKey("Outcome") -and -not [string]::IsNullOrWhiteSpace($Outcome)) {
        $script:BaselineOutcome = $Outcome
    }
    if ($PSBoundParameters.ContainsKey("Details")) {
        $script:BaselineStatusDetails = $Details
    }

    $currentStep = $null
    if ($null -ne $script:BaselineCurrentStep) {
        $currentStepStartedAtUtc = [datetime]$script:BaselineCurrentStep.startedAtUtc
        $currentStep = [ordered]@{
            id = [string]$script:BaselineCurrentStep.id
            title = [string]$script:BaselineCurrentStep.title
            state = [string]$script:BaselineCurrentStep.state
            evidence = [string]$script:BaselineCurrentStep.evidence
            details = [string]$script:BaselineCurrentStep.details
            startedAtUtc = $currentStepStartedAtUtc.ToString("o")
            elapsedMs = [int]([datetime]::UtcNow - $currentStepStartedAtUtc).TotalMilliseconds
        }
    }

    $payload = [ordered]@{
        schemaVersion = 1
        processId = $PID
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        startedAtUtc = $script:BaselineStartedAtUtc.ToString("o")
        lastHeartbeatUtc = (Get-Date).ToUniversalTime().ToString("o")
        phase = $script:BaselinePhase
        outcome = $script:BaselineOutcome
        details = $script:BaselineStatusDetails
        heartbeatIntervalSec = $HeartbeatIntervalSec
        artifacts = $script:BaselineArtifacts
        currentStep = $currentStep
        completedSteps = @($script:CompletedBaselineSteps.ToArray())
    }

    Set-Content -Path $script:ResolvedStatusPath -Value ($payload | ConvertTo-Json -Depth 12) -Encoding UTF8
}

function Set-BaselineCurrentStep {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$State,
        [Parameter(Mandatory = $true)][string]$Evidence,
        [string]$Details = "",
        [datetime]$StartedAtUtc = [datetime]::UtcNow
    )

    $script:BaselineCurrentStep = [ordered]@{
        id = $Id
        title = $Title
        state = $State
        evidence = $Evidence
        details = $Details
        startedAtUtc = $StartedAtUtc
    }
}

function Add-BaselineCompletedStep {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Evidence,
        [Parameter(Mandatory = $true)][int]$DurationMs,
        [Parameter(Mandatory = $true)][string]$Details,
        [Parameter(Mandatory = $true)][datetime]$StartedAtUtc
    )

    $script:CompletedBaselineSteps.Add([ordered]@{
        id = $Id
        title = $Title
        status = $Status
        evidence = $Evidence
        durationMs = $DurationMs
        details = $Details
        startedAtUtc = $StartedAtUtc.ToString("o")
        completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }) | Out-Null
}

function Start-BaselineHeartbeat {
    if ($HeartbeatIntervalSec -le 0) {
        return
    }

    $job = Start-Job -ArgumentList $script:ResolvedStatusPath, $HeartbeatIntervalSec, $PID -ScriptBlock {
        param(
            [string]$StatusPath,
            [int]$IntervalSec,
            [int]$ParentProcessId
        )

        $ErrorActionPreference = "SilentlyContinue"
        $sleepSeconds = [Math]::Max(1, $IntervalSec)

        while ($null -ne (Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue)) {
            if (Test-Path $StatusPath) {
                try {
                    $status = Get-Content -Path $StatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
                    if ($null -eq ($status.PSObject.Properties | Where-Object { $_.Name -eq "lastHeartbeatUtc" } | Select-Object -First 1)) {
                        $status | Add-Member -NotePropertyName lastHeartbeatUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString("o"))
                    }
                    else {
                        $status.lastHeartbeatUtc = (Get-Date).ToUniversalTime().ToString("o")
                    }

                    Set-Content -Path $StatusPath -Value ($status | ConvertTo-Json -Depth 12) -Encoding UTF8
                }
                catch {
                }
            }

            Start-Sleep -Seconds $sleepSeconds
        }
    }

    $script:BaselineHeartbeatJob = $job
}

function Stop-BaselineHeartbeat {
    if ($null -ne $script:BaselineHeartbeatJob) {
        try {
            Stop-Job -Id $script:BaselineHeartbeatJob.Id -ErrorAction SilentlyContinue
        }
        catch {
        }
        try {
            Remove-Job -Id $script:BaselineHeartbeatJob.Id -Force -ErrorAction SilentlyContinue
        }
        catch {
        }
    }
}

function Start-BaselineProcessMonitorWindow {
    param(
        [Parameter(Mandatory = $true)][string]$MonitorScriptPath,
        [Parameter(Mandatory = $true)][string]$StatusPath,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $powershellCommand = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($null -eq $powershellCommand) {
        Write-Host "[Baseline] Process monitor window unavailable: powershell.exe not found." -ForegroundColor DarkYellow
        return
    }

    $quotedMonitorScriptPath = '"' + $MonitorScriptPath + '"'
    $quotedTitle = '"HELPER Baseline Capture Monitor"'
    $quotedStatusPath = '"' + $StatusPath + '"'
    $quotedLogPath = '"' + $LogPath + '"'
    $arguments = @(
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-File", $quotedMonitorScriptPath,
        "-Title", $quotedTitle,
        "-RootProcessId", "$PID",
        "-StatusPath", $quotedStatusPath,
        "-LogPath", $quotedLogPath,
        "-RefreshSec", $ProcessMonitorRefreshSec
    )

    Start-Process -FilePath $powershellCommand.Source -ArgumentList $arguments | Out-Null
    Write-Host "[Baseline] Process monitor window started." -ForegroundColor DarkCyan
}

function Invoke-CheckedStep {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$Evidence,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $stepStartedAtUtc = (Get-Date).ToUniversalTime()
    Write-StepProgress -State "START" -Id $Id -Title $Title -Details $Evidence
    Set-BaselineCurrentStep -Id $Id -Title $Title -State "START" -Evidence $Evidence -Details $Evidence -StartedAtUtc $stepStartedAtUtc
    Write-BaselineStatusFile -Phase "running" -Outcome "RUNNING" -Details ("Running step {0}." -f $Id)
    try {
        $global:LASTEXITCODE = 0
        & $Action | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Exit code $LASTEXITCODE."
        }
        $stopwatch.Stop()
        Write-StepProgress -State "PASS" -Id $Id -Title $Title -Details ("{0} ms" -f [int]$stopwatch.ElapsedMilliseconds)
        Add-BaselineCompletedStep -Id $Id -Title $Title -Status "PASS" -Evidence $Evidence -DurationMs ([int]$stopwatch.ElapsedMilliseconds) -Details "passed" -StartedAtUtc $stepStartedAtUtc
        $script:BaselineCurrentStep = $null
        Write-BaselineStatusFile -Phase "running" -Outcome "RUNNING" -Details ("Completed step {0}." -f $Id)
        return (New-GateResult -Id $Id -Title $Title -Status "PASS" -Evidence $Evidence -DurationMs ([int]$stopwatch.ElapsedMilliseconds) -Details "passed")
    }
    catch {
        $stopwatch.Stop()
        Write-StepProgress -State "FAIL" -Id $Id -Title $Title -Details $_.Exception.Message
        Add-BaselineCompletedStep -Id $Id -Title $Title -Status "FAIL" -Evidence $Evidence -DurationMs ([int]$stopwatch.ElapsedMilliseconds) -Details $_.Exception.Message -StartedAtUtc $stepStartedAtUtc
        $script:BaselineCurrentStep = $null
        Write-BaselineStatusFile -Phase "running" -Outcome "RUNNING" -Details ("Step {0} failed." -f $Id)
        return (New-GateResult -Id $Id -Title $Title -Status "FAIL" -Evidence $Evidence -DurationMs ([int]$stopwatch.ElapsedMilliseconds) -Details $_.Exception.Message)
    }
}

function Invoke-FrontendBuildStep {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$Evidence,
        [Parameter(Mandatory = $true)][string]$SummaryPath,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    $baseGate = Invoke-CheckedStep -Id $Id -Title $Title -Evidence $Evidence -Action $Action
    if ($baseGate.status -eq "FAIL") {
        return $baseGate
    }

    $summary = Read-JsonFileOrNull -Path $SummaryPath
    if ($null -eq $summary) {
        return (New-GateResult -Id $Id -Title $Title -Status "FAIL" -Evidence $Evidence -DurationMs $baseGate.durationMs -Details ("Frontend build summary not found at {0}." -f $SummaryPath))
    }

    $status = Convert-ArtifactStatusToken -Status ([string](Get-OptionalObjectPropertyValue -Object $summary -Name "status"))
    $mode = [string](Get-OptionalObjectPropertyValue -Object $summary -Name "mode")
    $details = [string](Get-OptionalObjectPropertyValue -Object $summary -Name "details")
    $resolvedDetails = if ([string]::IsNullOrWhiteSpace($mode)) { $details } else { "mode=$mode; $details" }

    if ($status -eq "UNKNOWN") {
        return (New-GateResult -Id $Id -Title $Title -Status "FAIL" -Evidence $Evidence -DurationMs $baseGate.durationMs -Details "Frontend build summary reported an unknown status.")
    }

    return (New-GateResult -Id $Id -Title $Title -Status $status -Evidence $Evidence -DurationMs $baseGate.durationMs -Details $resolvedDetails)
}

function Convert-UiSmokeArtifactToGate {
    param(
        [Parameter(Mandatory = $true)][string]$JsonPath,
        [Parameter(Mandatory = $true)][int]$DurationMs,
        [Parameter(Mandatory = $true)][string]$Evidence
    )

    $artifact = Read-JsonFileOrNull -Path $JsonPath
    if ($null -eq $artifact) {
        return (New-GateResult -Id "uiWorkflowSmoke" -Title "UI workflow smoke" -Status "FAIL" -Evidence $Evidence -DurationMs $DurationMs -Details ("UI smoke artifact not found at {0}." -f $JsonPath))
    }

    $status = Convert-ArtifactStatusToken -Status ([string](Get-OptionalObjectPropertyValue -Object $artifact -Name "status"))
    $failures = @((Get-OptionalObjectPropertyValue -Object $artifact -Name "failures"))
    $workspaceRoot = [string](Get-OptionalObjectPropertyValue -Object $artifact -Name "workspaceRoot")
    $scenarioCount = @((Get-OptionalObjectPropertyValue -Object $artifact -Name "scenarios")).Count

    $details = switch ($status) {
        "PASS" {
            if ([string]::IsNullOrWhiteSpace($workspaceRoot)) {
                "scenarios=$scenarioCount"
            }
            else {
                "scenarios=$scenarioCount; workspace=$workspaceRoot"
            }
        }
        "SKIPPED" {
            if ($failures.Count -gt 0) { [string]$failures[0] } else { "UI smoke skipped." }
        }
        default {
            if ($failures.Count -gt 0) {
                ($failures | ForEach-Object { [string]$_ }) -join " | "
            }
            else {
                "UI smoke reported status $status."
            }
        }
    }

    return (New-GateResult -Id "uiWorkflowSmoke" -Title "UI workflow smoke" -Status $status -Evidence $Evidence -DurationMs $DurationMs -Details $details)
}

function Invoke-UiSmokeStep {
    param(
        [Parameter(Mandatory = $true)][string]$Evidence,
        [Parameter(Mandatory = $true)][string]$JsonPath,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    $baseGate = Invoke-CheckedStep -Id "uiWorkflowSmoke" -Title "UI workflow smoke" -Evidence $Evidence -Action $Action
    $artifactGate = $null
    if (Test-Path $JsonPath) {
        $artifactGate = Convert-UiSmokeArtifactToGate -JsonPath $JsonPath -DurationMs $baseGate.durationMs -Evidence $Evidence
    }

    if ($baseGate.status -eq "FAIL") {
        if ($null -ne $artifactGate) {
            return $artifactGate
        }

        return $baseGate
    }

    if ($null -ne $artifactGate) {
        return $artifactGate
    }

    return (New-GateResult -Id "uiWorkflowSmoke" -Title "UI workflow smoke" -Status "FAIL" -Evidence $Evidence -DurationMs $baseGate.durationMs -Details ("UI smoke artifact not found at {0}." -f $JsonPath))
}

function Get-CertificationGate {
    param(
        [Parameter(Mandatory = $true)]$Snapshot,
        [Parameter(Mandatory = $true)][string]$PackageKey,
        [Parameter(Mandatory = $true)][string]$Title,
        [string]$Notes = ""
    )

    $status = "UNKNOWN"
    if ($Snapshot.certification.packages -is [System.Collections.IDictionary]) {
        if ($Snapshot.certification.packages.Contains($PackageKey)) {
            $status = [string]$Snapshot.certification.packages[$PackageKey]
        }
    }
    else {
        $property = @($Snapshot.certification.packages.PSObject.Properties | Where-Object { $_.Name -eq $PackageKey }) | Select-Object -First 1
        if ($null -ne $property) {
            $status = [string]$property.Value
        }
    }

    return [ordered]@{
        id = "cert_" + $PackageKey.Replace('.', '_')
        title = $Title
        status = $status
        evidence = [string]$Snapshot.certification.evidence.daySummary
        durationMs = 0
        details = if ([string]::IsNullOrWhiteSpace($Notes)) { "From active counted certification evidence." } else { $Notes }
    }
}

function ConvertTo-ReleaseBaselineMarkdown {
    param([Parameter(Mandatory = $true)]$Baseline)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# HELPER Release Baseline")
    $lines.Add("")
    $lines.Add(('Generated at: `{0}`' -f $Baseline.generatedAtUtc))
    $lines.Add(('Overall status: `{0}`' -f $Baseline.status))
    $lines.Add(('Release decision: `{0}`' -f $Baseline.releaseDecision))
    $lines.Add(('Execution board status: `{0}`' -f $Baseline.executionBoardStatus))
    $lines.Add(('Certification source: `{0}`' -f $Baseline.certificationSource))
    $lines.Add("")
    $lines.Add("## Local Verification")
    $lines.Add("")
    $lines.Add("| Gate | Status | Evidence | Details |")
    $lines.Add("|---|---|---|---|")
    foreach ($gate in @($Baseline.localVerification)) {
        $lines.Add(("| {0} | `{1}` | `{2}` | {3} |" -f $gate.title, $gate.status, $gate.evidence, ([string]$gate.details).Replace('|', '/')))
    }
    $lines.Add("")
    $lines.Add("## Counted Certification Evidence")
    $lines.Add("")
    $lines.Add("| Gate | Status | Evidence | Details |")
    $lines.Add("|---|---|---|---|")
    foreach ($gate in @($Baseline.certificationEvidence)) {
        $lines.Add(("| {0} | `{1}` | `{2}` | {3} |" -f $gate.title, $gate.status, $gate.evidence, ([string]$gate.details).Replace('|', '/')))
    }

    if (@($Baseline.blockers).Count -gt 0) {
        $lines.Add("")
        $lines.Add("## Blockers")
        $lines.Add("")
        $index = 1
        foreach ($blocker in @($Baseline.blockers)) {
            $lines.Add(("{0}. {1}" -f $index, $blocker))
            $index += 1
        }
    }

    return ($lines -join "`r`n")
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedOutputJsonPath = Join-Path $repoRoot $OutputJsonPath
$resolvedOutputMarkdownPath = Join-Path $repoRoot $OutputMarkdownPath
$resolvedUiSmokeJsonPath = Join-Path $repoRoot $UiSmokeJsonPath
$resolvedLogPath = Join-Path $repoRoot $LogPath
$resolvedStatusPath = Join-Path $repoRoot $StatusPath
$resolvedFrontendBuildSummaryPath = Join-Path $repoRoot "temp/verification/frontend_build_summary.json"
$baselineRunId = (Get-Date).ToString("yyyyMMdd_HHmmss")
$baselineRunArtifactsRoot = Join-Path $repoRoot ("temp/verification/baseline_runs/" + $baselineRunId)
$resolvedDotnetTestLogPath = Join-Path $baselineRunArtifactsRoot "dotnet_test.log"
$resolvedDotnetTestErrorLogPath = Join-Path $baselineRunArtifactsRoot "dotnet_test.stderr.log"
$resolvedDotnetTestStatusPath = Join-Path $baselineRunArtifactsRoot "dotnet_test_status.json"
$currentSnapshotPath = Join-Path $repoRoot "doc/certification/active/CURRENT_GATE_SNAPSHOT.json"
$resolvedMonitorScriptPath = Join-Path $PSScriptRoot "watch_baseline_capture_processes.ps1"

$logDirectory = Split-Path -Parent $resolvedLogPath
if (-not [string]::IsNullOrWhiteSpace($logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
}
$statusDirectory = Split-Path -Parent $resolvedStatusPath
if (-not [string]::IsNullOrWhiteSpace($statusDirectory)) {
    New-Item -ItemType Directory -Path $statusDirectory -Force | Out-Null
}
New-Item -ItemType Directory -Path $baselineRunArtifactsRoot -Force | Out-Null

$script:ResolvedStatusPath = $resolvedStatusPath
$script:ResolvedLogPath = $resolvedLogPath
$script:BaselineStartedAtUtc = (Get-Date).ToUniversalTime()
$script:BaselinePhase = "initializing"
$script:BaselineOutcome = "RUNNING"
$script:BaselineStatusDetails = "baseline capture starting"
$script:BaselineCurrentStep = $null
$script:CompletedBaselineSteps = New-Object System.Collections.Generic.List[object]
$script:BaselineHeartbeatJob = $null
$script:BaselineArtifacts = [ordered]@{
    logPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedLogPath
    statusPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedStatusPath
    outputJsonPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedOutputJsonPath
    outputMarkdownPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedOutputMarkdownPath
    uiSmokeJsonPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedUiSmokeJsonPath
    frontendBuildSummaryPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedFrontendBuildSummaryPath
    dotnetTestLogPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedDotnetTestLogPath
    dotnetTestErrorLogPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedDotnetTestErrorLogPath
    dotnetTestStatusPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedDotnetTestStatusPath
}

Write-BaselineStatusFile -Phase "initializing" -Outcome "RUNNING" -Details "baseline capture starting"
Start-BaselineHeartbeat
if ($ShowProcessMonitor) {
    Start-BaselineProcessMonitorWindow -MonitorScriptPath $resolvedMonitorScriptPath -StatusPath $resolvedStatusPath -LogPath $resolvedLogPath
}

$transcriptStarted = $false
if (Test-Path $resolvedLogPath) {
    Remove-Item $resolvedLogPath -Force -ErrorAction SilentlyContinue
}
try {
    Start-Transcript -Path $resolvedLogPath -Force | Out-Null
    $transcriptStarted = $true
    Write-Host "[Baseline] Transcript: $resolvedLogPath" -ForegroundColor DarkCyan
}
catch {
    Write-Host "[Baseline] Transcript unavailable: $($_.Exception.Message)" -ForegroundColor DarkYellow
}

try {

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    $ApiBaseUrl = $env:HELPER_RUNTIME_SMOKE_API_BASE
}

if ([string]::IsNullOrWhiteSpace($UiUrl)) {
    $UiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
}

$localVerification = @(
    (Invoke-FrontendBuildStep -Id "frontendBuild" -Title "Frontend build" -Evidence "local:scripts/build_frontend_verification.ps1 -RequireRebuild" -SummaryPath $resolvedFrontendBuildSummaryPath -Action { & (Join-Path $PSScriptRoot "build_frontend_verification.ps1") -RequireRebuild -DisableTranscript }),
    (Invoke-CheckedStep -Id "backendApiBuild" -Title "Backend API build" -Evidence "local:dotnet build src/Helper.Api/Helper.Api.csproj -c Debug -m:1" -Action { dotnet build src\Helper.Api\Helper.Api.csproj -c Debug -m:1 }),
    (Invoke-CheckedStep -Id "runtimeCliBuild" -Title "Runtime CLI build" -Evidence "local:dotnet build src/Helper.Runtime.Cli/Helper.Runtime.Cli.csproj -c Debug -m:1" -Action { dotnet build src\Helper.Runtime.Cli\Helper.Runtime.Cli.csproj -c Debug -m:1 }),
    (Invoke-CheckedStep -Id "tests" -Title "Regression tests" -Evidence "local:scripts/run_dotnet_test_batched.ps1 --no-build --blame-hang --blame-hang-timeout 2m" -Action {
        dotnet build test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj -c Debug -m:1
        if ($LASTEXITCODE -ne 0) {
            throw "Tests build failed with exit code $LASTEXITCODE."
        }
        $wrapperArgs = @{
            Target = "test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj"
            BaseArguments = @("--no-build", "--blame-hang", "--blame-hang-timeout", "2m")
            LogPath = $resolvedDotnetTestLogPath
            ErrorLogPath = $resolvedDotnetTestErrorLogPath
            StatusPath = $resolvedDotnetTestStatusPath
            ResultsRoot = (Join-Path $baselineRunArtifactsRoot "batches")
            MaxDurationSec = 900
            ListTimeoutSec = 300
        }

        & (Join-Path $PSScriptRoot "run_dotnet_test_batched.ps1") @wrapperArgs
    }),
    (Invoke-CheckedStep -Id "securityScan" -Title "Secret scan" -Evidence "local:scripts/secret_scan.ps1 -ScanMode repo" -Action { powershell -ExecutionPolicy Bypass -File scripts\secret_scan.ps1 -ScanMode repo }),
    (Invoke-CheckedStep -Id "configGovernance" -Title "Config governance" -Evidence "local:scripts/check_env_governance.ps1" -Action { powershell -ExecutionPolicy Bypass -File scripts\check_env_governance.ps1 }),
    (Invoke-CheckedStep -Id "docsGate" -Title "Docs entrypoints" -Evidence "local:scripts/check_docs_entrypoints.ps1" -Action { powershell -ExecutionPolicy Bypass -File scripts\check_docs_entrypoints.ps1 }),
    (Invoke-CheckedStep -Id "apiUiBoundary" -Title "UI/API boundary" -Evidence "local:scripts/check_ui_api_usage.ps1" -Action { powershell -ExecutionPolicy Bypass -File scripts\check_ui_api_usage.ps1 }),
    (Invoke-CheckedStep -Id "frontendArchitecture" -Title "Frontend architecture" -Evidence "local:scripts/check_frontend_architecture.ps1 -SkipApiBoundary" -Action { powershell -ExecutionPolicy Bypass -File scripts\check_frontend_architecture.ps1 -SkipApiBoundary }),
    (Invoke-CheckedStep -Id "openApiContract" -Title "OpenAPI contract gate" -Evidence "local:scripts/openapi_gate.ps1" -Action { powershell -ExecutionPolicy Bypass -File scripts\openapi_gate.ps1 }),
    (Invoke-CheckedStep -Id "generatedClientDiff" -Title "Generated client diff gate" -Evidence "local:scripts/generated_client_diff_gate.ps1" -Action { powershell -ExecutionPolicy Bypass -File scripts\generated_client_diff_gate.ps1 })
)

if ($SkipUiSmoke) {
    $uiSmokeGate = Convert-UiSmokeArtifactToGate -JsonPath $resolvedUiSmokeJsonPath -DurationMs 0 -Evidence $UiSmokeJsonPath.Replace('\\', '/')
}
else {
    $uiSmokeGate = Invoke-UiSmokeStep -Evidence $UiSmokeJsonPath.Replace('\\', '/') -JsonPath $resolvedUiSmokeJsonPath -Action {
        $args = @(
            "-ExecutionPolicy", "Bypass",
            "-File", (Join-Path $PSScriptRoot "run_ui_workflow_smoke.ps1"),
            "-OutputJsonPath", $UiSmokeJsonPath,
            "-RequireConfiguredRuntime"
        )
        if (-not [string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
            $args += @("-ApiBaseUrl", $ApiBaseUrl)
        }
        if (-not [string]::IsNullOrWhiteSpace($UiUrl)) {
            $args += @("-UiUrl", $UiUrl)
        }
        if (-not [string]::IsNullOrWhiteSpace($UiSmokeWorkspaceRoot)) {
            $args += @("-WorkspaceRoot", $UiSmokeWorkspaceRoot)
        }
        & powershell @args
    }
}

$localVerification += $uiSmokeGate

$currentSnapshot = Read-JsonFileOrNull -Path $currentSnapshotPath
if ($null -eq $currentSnapshot) {
    throw "[Baseline] Active gate snapshot not found at $currentSnapshotPath. Refresh counted-day evidence first."
}

$certificationEvidence = @(
    (Get-CertificationGate -Snapshot $currentSnapshot -PackageKey "3.1" -Title "Counted parity gate"),
    (Get-CertificationGate -Snapshot $currentSnapshot -PackageKey "3.2" -Title "Strict parity window" -Notes "Informational for fresh-precert readiness; current active-cycle anchor may remain pending."),
    (Get-CertificationGate -Snapshot $currentSnapshot -PackageKey "3.3" -Title "Smoke generation compile gate"),
    (Get-CertificationGate -Snapshot $currentSnapshot -PackageKey "3.4" -Title "Human parity sample"),
    (Get-CertificationGate -Snapshot $currentSnapshot -PackageKey "3.5" -Title "Real-model eval gate")
)

$localFailures = @($localVerification | Where-Object { $_.status -eq "FAIL" })
$localSkipped = @($localVerification | Where-Object { $_.status -eq "SKIPPED" -or $_.status -eq "UNKNOWN" })
$certFailures = @($certificationEvidence | Where-Object {
    $_.id -ne "cert_3_2" -and $_.status -ne "PASS"
})

$blockers = New-Object System.Collections.Generic.List[string]
foreach ($gate in $localFailures) {
    $blockers.Add(("{0} failed: {1}" -f $gate.title, $gate.details))
}
foreach ($gate in $localSkipped) {
    $blockers.Add(("{0} is not verified in this baseline: {1}" -f $gate.title, $gate.details))
}
foreach ($gate in $certFailures) {
    $blockers.Add(("{0} is not green in active counted evidence: {1}" -f $gate.title, $gate.status))
}

$executionBoardStatus = "unknown"
try {
    . (Join-Path $PSScriptRoot "common\CertificationSummaryRenderer.ps1")
    $executionBoardStatus = Get-ExecutionBoardStatus -BoardPath (Join-Path $repoRoot "doc/archive/top_level_history/HELPER_EXECUTION_BOARD_2026-03-16.md")
}
catch {
    $executionBoardStatus = "unknown"
}

$status = if ($blockers.Count -gt 0) {
    "FAIL"
}
elseif ($executionBoardStatus -ne "complete") {
    "INCOMPLETE"
}
else {
    "PASS"
}

$releaseDecision = switch ($status) {
    "PASS" { "eligible to open a fresh precert day-01 after freeze" }
    "INCOMPLETE" { "finish the execution board before opening a fresh precert day-01" }
    default { "do not reopen certification until the release baseline is green" }
}

$baseline = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    status = $status
    releaseDecision = $releaseDecision
    executionBoardStatus = $executionBoardStatus
    certificationSource = "doc/certification/active/CURRENT_GATE_SNAPSHOT.json"
    localVerification = $localVerification
    certificationEvidence = $certificationEvidence
    blockers = @($blockers.ToArray())
}

$resolvedOutputDirectory = Split-Path -Parent $resolvedOutputJsonPath
if (-not [string]::IsNullOrWhiteSpace($resolvedOutputDirectory)) {
    New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
}
$resolvedMarkdownDirectory = Split-Path -Parent $resolvedOutputMarkdownPath
if (-not [string]::IsNullOrWhiteSpace($resolvedMarkdownDirectory)) {
    New-Item -ItemType Directory -Path $resolvedMarkdownDirectory -Force | Out-Null
}

Set-Content -Path $resolvedOutputJsonPath -Value ($baseline | ConvertTo-Json -Depth 10) -Encoding UTF8
Set-Content -Path $resolvedOutputMarkdownPath -Value (ConvertTo-ReleaseBaselineMarkdown -Baseline $baseline) -Encoding UTF8
Write-Host "[Baseline] Saved baseline: $OutputMarkdownPath" -ForegroundColor Green

if ($RefreshActiveGateSnapshot) {
    & (Join-Path $PSScriptRoot "refresh_active_gate_snapshot.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "[Baseline] Active gate snapshot refresh failed."
    }
}

Write-BaselineStatusFile -Phase "completed" -Outcome $status -Details ("Release baseline completed with status {0}." -f $status)
if ($status -ne "PASS") {
    throw "[Baseline] Release baseline status is $status. See $OutputMarkdownPath."
}
}
catch {
    if ($script:BaselinePhase -ne "completed") {
        Write-BaselineStatusFile -Phase "failed" -Outcome "FAIL" -Details $_.Exception.Message
    }
    throw
}
finally {
    Stop-BaselineHeartbeat
    if ($transcriptStarted) {
        try {
            Stop-Transcript | Out-Null
        }
        catch {
            Write-Host "[Baseline] Transcript stop unavailable: $($_.Exception.Message)" -ForegroundColor DarkYellow
        }
    }
}

