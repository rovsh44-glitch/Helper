param(
    [Parameter(Mandatory = $true)][ValidatePattern("^precert_\d{4}-\d{2}-\d{2}$")][string]$CycleId,
    [int]$Day = 1,
    [string]$WorkspaceRoot = ".",
    [switch]$InitializeCycleIfMissing,
    [switch]$PlannedReuseExistingArtifacts,
    [int]$ExpectedParityRuns = 24,
    [int]$ExpectedSmokeRuns = 50,
    [int]$ProbeTimeoutSec = 120,
    [switch]$SkipPdfEpubProbe
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")
. (Join-Path $PSScriptRoot "common\GenerationArtifactDetection.ps1")
. (Join-Path $PSScriptRoot "precert_day_runner_common.ps1")
$helperRuntimeCliInvokerPath = Join-Path $PSScriptRoot "invoke_helper_runtime_cli.ps1"

function ConvertTo-PreCertCalendarDate {
    param([Parameter(Mandatory = $true)][string]$IsoDate)

    $parsed = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact(
        $IsoDate,
        "yyyy-MM-dd",
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None,
        [ref]$parsed)) {
        throw "[PreCertOperatorChecklist] Invalid ISO date: $IsoDate"
    }

    return $parsed.Date
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    return Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Read-JsonFileOrNull {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    return Read-JsonFile -Path $Path
}

function New-ChecklistItem {
    param(
        [Parameter(Mandatory = $true)][int]$Number,
        [Parameter(Mandatory = $true)][string]$Check,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Evidence,
        [Parameter(Mandatory = $true)][string]$Notes
    )

    return [pscustomobject]@{
        Number = $Number
        Check = $Check
        Status = $Status
        Evidence = $Evidence
        Notes = $Notes
    }
}

function Get-RunHistoryPath {
    param([Parameter(Mandatory = $true)]$PathConfig)

    return Join-Path $PathConfig.ProjectsRoot "generation_runs.jsonl"
}

function Test-RunHistoryContainsWorkload {
    param(
        [Parameter(Mandatory = $true)][string]$RunHistoryPath,
        [Parameter(Mandatory = $true)][string]$WorkloadClass
    )

    if (-not (Test-Path $RunHistoryPath)) {
        return $false
    }

    $pattern = '"WorkloadClass"\s*:\s*"' + [regex]::Escape($WorkloadClass) + '"'
    return [bool](Select-String -Path $RunHistoryPath -Pattern $pattern -Quiet)
}

function Get-TemplateStatusFreshness {
    param([Parameter(Mandatory = $true)][string]$TemplateRoot)

    $ignoredSegments = @("bin", "obj", ".git", ".vs", "node_modules", ".compile_gate", "__pycache__")
    $latestWriteUtc = $null
    foreach ($file in Get-ChildItem -Path $TemplateRoot -Recurse -File -ErrorAction SilentlyContinue) {
        $relativePath = [System.IO.Path]::GetRelativePath($TemplateRoot, $file.FullName)
        if ([string]::Equals([System.IO.Path]::GetFileName($relativePath), "certification_status.json", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $segments = $relativePath.Split([System.IO.Path]::DirectorySeparatorChar, [System.StringSplitOptions]::RemoveEmptyEntries)
        if (@($segments | Where-Object { $ignoredSegments -contains $_ }).Count -gt 0) {
            continue
        }

        if ($null -eq $latestWriteUtc -or $file.LastWriteTimeUtc -gt $latestWriteUtc) {
            $latestWriteUtc = $file.LastWriteTimeUtc
        }
    }

    return $latestWriteUtc
}

function Invoke-TemplateAvailabilityCheck {
    param(
        [Parameter(Mandatory = $true)][string]$WorkspaceRoot,
        [Parameter(Mandatory = $true)][string]$TemplateId,
        [Parameter(Mandatory = $true)][string]$LogDirectory
    )

    $safeName = ($TemplateId -replace '[^A-Za-z0-9_.-]', '_')
    $stdoutPath = Join-Path $LogDirectory ("template_availability_" + $safeName + ".stdout.log")
    $stderrPath = Join-Path $LogDirectory ("template_availability_" + $safeName + ".stderr.log")
    Push-Location $WorkspaceRoot
    try {
        & $helperRuntimeCliInvokerPath -Configuration Debug -NoBuild template-availability $TemplateId *> $stdoutPath
        $exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
        Set-Content -Path $stderrPath -Value @() -Encoding UTF8
    }
    finally {
        Pop-Location
    }

    $stdout = if (Test-Path $stdoutPath) { [string](Get-Content -Path $stdoutPath -Raw -Encoding UTF8) } else { "" }
    $stderr = if (Test-Path $stderrPath) { [string](Get-Content -Path $stderrPath -Raw -Encoding UTF8) } else { "" }
    return [pscustomobject]@{
        TemplateId = $TemplateId
        ExitCode = $exitCode
        Available = ($exitCode -eq 0)
        StdoutPath = $stdoutPath
        StderrPath = $stderrPath
        Stdout = [string]$stdout
        Stderr = [string]$stderr
    }
}

function Invoke-PdfEpubProbe {
    param(
        [Parameter(Mandatory = $true)][string]$WorkspaceRoot,
        [Parameter(Mandatory = $true)]$PathConfig,
        [Parameter(Mandatory = $true)][string]$WorkloadClass,
        [Parameter(Mandatory = $true)][string]$LogDirectory,
        [Parameter(Mandatory = $true)][int]$TimeoutSec
    )

    $outputPath = Join-Path $LogDirectory "pdfepub_probe.output.log"
    $probeRuntimeRoot = Join-Path $LogDirectory "probe_runtime"
    $probeDataRoot = Join-Path $probeRuntimeRoot "HELPER_DATA"
    $probeProjectsRoot = Join-Path $probeDataRoot "PROJECTS"
    $probeForgeOutputRoot = Join-Path $probeProjectsRoot "FORGE_OUTPUT"
    $probeLogsRoot = Join-Path $probeDataRoot "LOG"
    $probeTelemetryPath = Join-Path $probeLogsRoot "template_routing_decisions.jsonl"
    $prompt = "Generate a PDF to EPUB and EPUB to PDF converter in C#."
    $envNames = @(
        "HELPER_SMOKE_PROFILE",
        "HELPER_ENABLE_METACOGNITIVE_DEBUG",
        "HELPER_ENABLE_SUCCESS_REFLECTION",
        "HELPER_MAX_HEAL_ITERATIONS",
        "HELPER_CREATE_TIMEOUT_SEC",
        "HELPER_CREATE_TIMEOUT_SYNTHESIS_SEC",
        "HELPER_GENERATION_WORKLOAD_CLASS",
        "HELPER_DATA_ROOT",
        "HELPER_PROJECTS_ROOT",
        "HELPER_FORGE_OUTPUT_ROOT",
        "HELPER_LOGS_ROOT",
        "HELPER_TEMPLATE_ROUTING_TELEMETRY_PATH"
    )
    $previousEnv = @{}
    foreach ($name in $envNames) {
        $previousEnv[$name] = [Environment]::GetEnvironmentVariable($name)
    }

    function Get-ProbeFailureHints {
        param([Parameter(Mandatory = $true)][string]$Path)

        if (-not (Test-Path $Path)) {
            return @()
        }

        $content = Get-Content -Path $Path -Raw -Encoding UTF8
        $hints = New-Object System.Collections.Generic.List[string]
        if ($content -match '(?m)^\[Error\]\s*(?<value>.+)$') {
            $hints.Add($Matches["value"].Trim())
        }
        elseif ($content -match '(?im)access to the path .+ is denied\.') {
            $hints.Add($Matches[0].Trim())
        }

        return @($hints.ToArray())
    }

    try {
        foreach ($dir in @($probeRuntimeRoot, $probeDataRoot, $probeProjectsRoot, $probeForgeOutputRoot, $probeLogsRoot)) {
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
        }

        $env:HELPER_SMOKE_PROFILE = "false"
        $env:HELPER_ENABLE_METACOGNITIVE_DEBUG = "false"
        $env:HELPER_ENABLE_SUCCESS_REFLECTION = "false"
        $env:HELPER_MAX_HEAL_ITERATIONS = "0"
        $env:HELPER_CREATE_TIMEOUT_SEC = $TimeoutSec.ToString()
        $env:HELPER_CREATE_TIMEOUT_SYNTHESIS_SEC = [Math]::Max(10, $TimeoutSec - 12).ToString()
        $env:HELPER_GENERATION_WORKLOAD_CLASS = $WorkloadClass
        $env:HELPER_DATA_ROOT = $probeDataRoot
        $env:HELPER_PROJECTS_ROOT = $probeProjectsRoot
        $env:HELPER_FORGE_OUTPUT_ROOT = $probeForgeOutputRoot
        $env:HELPER_LOGS_ROOT = $probeLogsRoot
        $env:HELPER_TEMPLATE_ROUTING_TELEMETRY_PATH = $probeTelemetryPath

        $startedUtc = [DateTime]::UtcNow
        $probeArtifactRoots = @($probeProjectsRoot)
        $previousLatest = Find-LatestValidationReport -ProjectsRoots $probeArtifactRoots
        & $helperRuntimeCliInvokerPath -Configuration Debug -NoBuild -CliArgs @("create", $prompt) *> $outputPath
        $exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }

        $reportFile = Find-ValidationReportForRun `
            -ProjectsRoots $probeArtifactRoots `
            -RunStartedUtc $startedUtc `
            -PreviousLatestPath $(if ($null -ne $previousLatest) { $previousLatest.FullName } else { "" })

        if ($null -eq $reportFile) {
            $failureHints = Get-ProbeFailureHints -Path $outputPath
            return [pscustomobject]@{
                Passed = $false
                ExitCode = $exitCode
                ValidationReportPath = ""
                RoutedTemplateId = ""
                RouteMatched = $false
                GoldenTemplateMatched = $false
                CompileGatePassed = $false
                ArtifactValidationPassed = $false
                SmokePassed = $false
                Errors = @("probe_validation_report_missing") + $failureHints
                OutputPath = $outputPath
            }
        }

        $report = Read-JsonFile -Path $reportFile.FullName
        $errors = @()
        if ($null -ne $report.Errors) {
            $errors = @($report.Errors)
        }

        $passed = ($exitCode -eq 0) `
            -and ($report.RoutedTemplateId -eq "Template_PdfEpubConverter") `
            -and ($report.RouteMatched -eq $true) `
            -and ($report.GoldenTemplateMatched -eq $true) `
            -and ($report.CompileGatePassed -eq $true) `
            -and ($report.ArtifactValidationPassed -eq $true) `
            -and ($report.SmokePassed -eq $true) `
            -and (@($errors | Where-Object { $_ -match 'TEMPLATE_NOT_FOUND|TEMPLATE_BLOCKED_BY_CERTIFICATION_STATUS' }).Count -eq 0)

        return [pscustomobject]@{
            Passed = $passed
            ExitCode = $exitCode
            ValidationReportPath = $reportFile.FullName
            RoutedTemplateId = [string]$report.RoutedTemplateId
            RouteMatched = ($report.RouteMatched -eq $true)
            GoldenTemplateMatched = ($report.GoldenTemplateMatched -eq $true)
            CompileGatePassed = ($report.CompileGatePassed -eq $true)
            ArtifactValidationPassed = ($report.ArtifactValidationPassed -eq $true)
            SmokePassed = ($report.SmokePassed -eq $true)
            Errors = $errors
            OutputPath = $outputPath
        }
    }
    finally {
        foreach ($name in $envNames) {
            [Environment]::SetEnvironmentVariable($name, $previousEnv[$name])
        }
    }
}

function Write-OperatorChecklistReport {
    param(
        [Parameter(Mandatory = $true)][string]$MarkdownPath,
        [Parameter(Mandatory = $true)][string]$JsonPath,
        [Parameter(Mandatory = $true)]$Report
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add(("# Operator Checklist: {0}/day-{1}" -f $Report.CycleId, $Report.DayText))
    $lines.Add("")
    $lines.Add(('- GeneratedAtUtc: `{0}`' -f $Report.GeneratedAtUtc))
    $lines.Add(('- ExecutionDate: `{0}`' -f $Report.ExecutionDate))
    $lines.Add(('- Passed: `{0}`' -f $(if ($Report.Passed) { "YES" } else { "NO" })))
    $lines.Add(('- ParityWorkload: `{0}`' -f $Report.ParityWorkload))
    $lines.Add(('- SmokeWorkload: `{0}`' -f $Report.SmokeWorkload))
    $lines.Add(('- ProbeWorkload: `{0}`' -f $Report.ProbeWorkload))
    $lines.Add("")
    $lines.Add("| # | Check | Status | Evidence | Notes |")
    $lines.Add("|---:|---|---|---|---|")
    foreach ($item in @($Report.Checks)) {
        $lines.Add(('| {0} | {1} | `{2}` | `{3}` | {4} |' -f $item.Number, $item.Check, $item.Status, $item.Evidence, $item.Notes.Replace("|", "/")))
    }
    $lines.Add("")
    $lines.Add("## Verdict")
    $lines.Add("")
    $lines.Add(('1. Checklist result: `{0}`' -f $(if ($Report.Passed) { "GO" } else { "NO-GO" })))
    $lines.Add(('2. Blocking checks: `{0}`' -f $Report.BlockingCheckCount))

    Set-Content -Path $MarkdownPath -Value ($lines -join "`r`n") -Encoding UTF8
    $Report | ConvertTo-Json -Depth 8 | Set-Content -Path $JsonPath -Encoding UTF8
}

if ($Day -ne 1) {
    throw "[PreCertOperatorChecklist] This checklist currently supports day-01 only."
}

$pathConfig = Get-HelperPathConfig -WorkspaceRoot $WorkspaceRoot
$root = $pathConfig.HelperRoot
Set-Location $root

$dayLabel = ConvertTo-PreCertDayLabel -Day $Day
$dayText = $Day.ToString("00")
$cycleStartIso = $CycleId.Substring("precert_".Length)
$cycleStartDate = ConvertTo-PreCertCalendarDate -IsoDate $cycleStartIso
$currentCalendarDate = (Get-Date).Date
$cycleDir = Join-Path $root ("doc\pre_certification\cycles\" + $CycleId)

if ((-not (Test-Path $cycleDir)) -and $InitializeCycleIfMissing.IsPresent) {
    try {
        & (Join-Path $root "scripts\init_precert_cycle.ps1") -WorkspaceRoot $root -StartDateUtc $cycleStartIso
    }
    catch {
        throw "[PreCertOperatorChecklist] Failed to initialize missing cycle."
    }
}

$reportBaseDir = if (Test-Path $cycleDir) {
    Join-Path $cycleDir $dayLabel
}
else {
    Join-Path $root "doc\pre_certification\verification"
}
New-Item -ItemType Directory -Force -Path $reportBaseDir | Out-Null

$reportStem = if (Test-Path $cycleDir) {
    "OPERATOR_CHECKLIST_day$dayText"
}
else {
    "OPERATOR_CHECKLIST_${CycleId}_day$dayText"
}
$reportMarkdownPath = Join-Path $reportBaseDir ($reportStem + ".md")
$reportJsonPath = Join-Path $reportBaseDir ($reportStem + ".json")
$operatorLogDir = Join-Path $reportBaseDir ("operator_checklist_logs\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $operatorLogDir | Out-Null
$helperRuntimeCliDllCandidates = @(
    (Join-Path $root "temp\helper_runtime_cli_host\out\Debug\Helper.Runtime.Cli.dll")
)
$helperRuntimeCliDllPath = $helperRuntimeCliDllCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($helperRuntimeCliDllPath)) {
    $helperRuntimeCliDllPath = $helperRuntimeCliDllCandidates[0]
}
$helperRuntimeCliReady = Test-Path $helperRuntimeCliDllPath

$statePath = Join-Path $cycleDir "PRECERT_CYCLE_STATE.json"
$state = Read-JsonFileOrNull -Path $statePath
$dayDir = Join-Path $cycleDir $dayLabel
$logsRoot = Join-Path $dayDir "logs"
$cycleToken = $CycleId.ToLowerInvariant().Replace("precert_", "")
$workloadPrefix = ("precert-{0}-{1}" -f $cycleToken, $dayLabel)
$parityWorkload = $workloadPrefix + "-parity"
$smokeWorkload = $workloadPrefix + "-smoke"
$probeWorkload = ("operator-preflight-{0}-{1}-{2}" -f $cycleToken, $dayLabel, (Get-Date -Format "yyyyMMddHHmmss"))
$runHistoryPath = Get-RunHistoryPath -PathConfig $pathConfig

$checks = New-Object System.Collections.Generic.List[object]

$cycleReady = $false
$cycleReadyEvidence = "PRECERT_CYCLE_STATE.json"
$cycleReadyNote = "cycle_missing"
if ($null -ne $state) {
    $stateStartDate = ConvertTo-PreCertCalendarDate -IsoDate ([string]$state.startDateUtc)
    $cycleReady = ($stateStartDate -eq $currentCalendarDate) -and ([int]$state.progress.closedDays -eq 0)
    $cycleReadyNote = ("cycle_exists startDateUtc={0}; today={1:yyyy-MM-dd}; closedDays={2}" -f $state.startDateUtc, $currentCalendarDate, [int]$state.progress.closedDays)
}
elseif ($InitializeCycleIfMissing.IsPresent) {
    $cycleReadyNote = "cycle_initialization_requested_but_state_missing_after_init"
}
$checks.Add((New-ChecklistItem -Number 1 -Check "Cycle initialized and aligned to day-01 date" -Status $(if ($cycleReady) { "PASS" } else { "FAIL" }) -Evidence $cycleReadyEvidence -Notes $cycleReadyNote))

$previousCycleStatePath = $null
$previousCycleNote = "no_previous_cycle_found"
$previousCycleOk = -not $PlannedReuseExistingArtifacts.IsPresent
$cyclesRoot = Join-Path $root "doc\pre_certification\cycles"
if (Test-Path $cyclesRoot) {
    $previousStateFile = Get-ChildItem -Path $cyclesRoot -Filter "PRECERT_CYCLE_STATE.json" -Recurse -File |
        Where-Object { $_.FullName -ne $statePath } |
        Sort-Object FullName
    $previousCandidates = foreach ($candidate in $previousStateFile) {
        $candidateState = Read-JsonFileOrNull -Path $candidate.FullName
        if ($null -eq $candidateState) {
            continue
        }

        $candidateStart = ConvertTo-PreCertCalendarDate -IsoDate ([string]$candidateState.startDateUtc)
        if ($candidateStart -lt $cycleStartDate) {
            [pscustomobject]@{
                Path = $candidate.FullName
                State = $candidateState
                StartDate = $candidateStart
            }
        }
    }

    $latestPrevious = $previousCandidates | Sort-Object StartDate -Descending | Select-Object -First 1
    if ($null -ne $latestPrevious) {
        $previousCycleStatePath = $latestPrevious.Path
        $previousStatus = [string]$latestPrevious.State.status
        $previousResult = if ($null -ne $latestPrevious.State.lastAssessment) { [string]$latestPrevious.State.lastAssessment.result } else { "" }
        $previousClosedDays = [int]$latestPrevious.State.progress.closedDays
        $previousCycleOk = $previousCycleOk -and (
            $previousStatus -like "*failed*" -or
            $previousResult -eq "FAILED" -or
            $previousClosedDays -eq 0
        )
        $previousCycleNote = ("previousCycle={0}; status={1}; lastResult={2}; closedDays={3}; reuseExistingArtifacts={4}" -f $latestPrevious.State.cycleId, $previousStatus, $(if ([string]::IsNullOrWhiteSpace($previousResult)) { "n/a" } else { $previousResult }), $previousClosedDays, $PlannedReuseExistingArtifacts.IsPresent)
    }
    else {
        $previousCycleNote = ("no_previous_cycle_before_{0}; reuseExistingArtifacts={1}" -f $CycleId, $PlannedReuseExistingArtifacts.IsPresent)
    }
}
$checks.Add((New-ChecklistItem -Number 2 -Check "Previous cycle preserved and not reused" -Status $(if ($previousCycleOk) { "PASS" } else { "FAIL" }) -Evidence $(if ($null -ne $previousCycleStatePath) { $previousCycleStatePath } else { "none" }) -Notes $previousCycleNote))

$pdfTemplateRoot = Join-Path $pathConfig.TemplatesRoot "Template_PdfEpubConverter"
$pdfTemplateStatusPath = Join-Path $pdfTemplateRoot "certification_status.json"
$pdfTemplateStatus = Read-JsonFileOrNull -Path $pdfTemplateStatusPath
$pdfStatusOk = $false
$pdfStatusNote = "certification_status_missing"
if (($null -ne $pdfTemplateStatus) -and (Test-Path $pdfTemplateRoot)) {
    $latestContentWriteUtc = Get-TemplateStatusFreshness -TemplateRoot $pdfTemplateRoot
    $evaluatedAtUtc = [DateTimeOffset]::Parse([string]$pdfTemplateStatus.EvaluatedAtUtc, [System.Globalization.CultureInfo]::InvariantCulture)
    $isStale = ($null -ne $latestContentWriteUtc) -and ($latestContentWriteUtc -gt $evaluatedAtUtc.UtcDateTime.AddSeconds(1))
    $pdfStatusOk = ($pdfTemplateStatus.Passed -eq $true) -and ($pdfTemplateStatus.HasCriticalAlerts -eq $false) -and (-not $isStale)
    $pdfStatusNote = ("passed={0}; hasCriticalAlerts={1}; stale={2}; reportPath={3}" -f $pdfTemplateStatus.Passed, $pdfTemplateStatus.HasCriticalAlerts, $isStale, $(if ([string]::IsNullOrWhiteSpace([string]$pdfTemplateStatus.ReportPath)) { "n/a" } else { [string]$pdfTemplateStatus.ReportPath }))
}
$checks.Add((New-ChecklistItem -Number 3 -Check "Template_PdfEpubConverter certification status is green" -Status $(if ($pdfStatusOk) { "PASS" } else { "FAIL" }) -Evidence $pdfTemplateStatusPath -Notes $pdfStatusNote))

$templateIds = @("Template_EngineeringCalculator", "Golden_Chess_v2", "Template_PdfEpubConverter")
$availabilityResults = @()
$availabilityOk = $false
$availabilityNote = "helper_runtime_cli_debug_binary_missing"
if ($helperRuntimeCliReady) {
    $availabilityResults = foreach ($templateId in $templateIds) {
        Invoke-TemplateAvailabilityCheck -WorkspaceRoot $root -TemplateId $templateId -LogDirectory $operatorLogDir
    }
    $availabilityOk = @($availabilityResults | Where-Object { -not $_.Available }).Count -eq 0
    $availabilityNote = ($availabilityResults | ForEach-Object { "{0}:{1}" -f $_.TemplateId, $(if ($_.Available) { "available" } else { "blocked" }) }) -join "; "
}
else {
    $availabilityNote = ("helper_runtime_cli_debug_binary_missing; expected={0}" -f $helperRuntimeCliDllPath)
}
$checks.Add((New-ChecklistItem -Number 4 -Check "Expected golden templates are route-available" -Status $(if ($availabilityOk) { "PASS" } else { "FAIL" }) -Evidence $(if ($helperRuntimeCliReady) { $operatorLogDir } else { $helperRuntimeCliDllPath }) -Notes $availabilityNote))

$probeStatus = "SKIP"
$probeEvidence = "skipped"
$probeNote = "SkipPdfEpubProbe=true"
if ((-not $SkipPdfEpubProbe.IsPresent) -and $helperRuntimeCliReady) {
    $probe = Invoke-PdfEpubProbe `
        -WorkspaceRoot $root `
        -PathConfig $pathConfig `
        -WorkloadClass $probeWorkload `
        -LogDirectory $operatorLogDir `
        -TimeoutSec $ProbeTimeoutSec
    $probeStatus = if ($probe.Passed) { "PASS" } else { "FAIL" }
    $probeEvidence = if ([string]::IsNullOrWhiteSpace($probe.ValidationReportPath)) { $probe.OutputPath } else { $probe.ValidationReportPath }
    $probeNote = ("exit={0}; routedTemplate={1}; routeMatched={2}; goldenMatched={3}; compile={4}; artifact={5}; smoke={6}; errors={7}" -f $probe.ExitCode, $(if ([string]::IsNullOrWhiteSpace($probe.RoutedTemplateId)) { "n/a" } else { $probe.RoutedTemplateId }), $probe.RouteMatched, $probe.GoldenTemplateMatched, $probe.CompileGatePassed, $probe.ArtifactValidationPassed, $probe.SmokePassed, $(if (@($probe.Errors).Count -eq 0) { "none" } else { ($probe.Errors -join "; ") }))
}
elseif (-not $helperRuntimeCliReady) {
    $probeStatus = "FAIL"
    $probeEvidence = $helperRuntimeCliDllPath
    $probeNote = ("helper_runtime_cli_debug_binary_missing; expected={0}" -f $helperRuntimeCliDllPath)
}
$checks.Add((New-ChecklistItem -Number 5 -Check "Point probe for PDF/EPUB prompt is green" -Status $probeStatus -Evidence $probeEvidence -Notes $probeNote))

$envStrictValue = [Environment]::GetEnvironmentVariable("HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE")
$strictModeOk = ($null -ne $state) `
    -and ([string]$state.strict.HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE -eq "false") `
    -and ($state.strict.noSoftBypassFlags -eq $true) `
    -and (-not $PlannedReuseExistingArtifacts.IsPresent) `
    -and ([string]::IsNullOrWhiteSpace($envStrictValue) -or ($envStrictValue -eq "false"))
$strictModeNote = if ($null -eq $state) {
    "cycle_state_missing"
}
else {
    ("state.strict.allowIncomplete={0}; state.strict.noSoftBypassFlags={1}; env.allowIncomplete={2}; reuseExistingArtifacts={3}" -f $state.strict.HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE, $state.strict.noSoftBypassFlags, $(if ([string]::IsNullOrWhiteSpace($envStrictValue)) { "unset" } else { $envStrictValue }), $PlannedReuseExistingArtifacts.IsPresent)
}
$checks.Add((New-ChecklistItem -Number 6 -Check "Strict counted mode is locked" -Status $(if ($strictModeOk) { "PASS" } else { "FAIL" }) -Evidence $(if ($null -ne $state) { $statePath } else { "state_missing" }) -Notes $strictModeNote))

$workloadUniqueOk = (-not (Test-RunHistoryContainsWorkload -RunHistoryPath $runHistoryPath -WorkloadClass $parityWorkload)) `
    -and (-not (Test-RunHistoryContainsWorkload -RunHistoryPath $runHistoryPath -WorkloadClass $smokeWorkload))
$workloadUniqueNote = ("runHistory={0}; parityWorkloadSeen={1}; smokeWorkloadSeen={2}" -f $runHistoryPath, (Test-RunHistoryContainsWorkload -RunHistoryPath $runHistoryPath -WorkloadClass $parityWorkload), (Test-RunHistoryContainsWorkload -RunHistoryPath $runHistoryPath -WorkloadClass $smokeWorkload))
$checks.Add((New-ChecklistItem -Number 7 -Check "Counted workload classes are unique for the new day-01" -Status $(if ($workloadUniqueOk) { "PASS" } else { "FAIL" }) -Evidence $runHistoryPath -Notes $workloadUniqueNote))

$artifactNames = @(
    "PARITY_GOLDEN_BATCH_day$dayText.md",
    "HELPER_PARITY_GATE_day$dayText.md",
    "HELPER_PARITY_WINDOW_GATE_day$dayText.md",
    "HELPER_PARITY_WINDOW_GATE_day$dayText.json",
    "SMOKE_COMPILE_day$dayText.md",
    "CLOSED_LOOP_PREDICTABILITY_day$dayText.md",
    "CLOSED_LOOP_PREDICTABILITY_day$dayText.json",
    "EVAL_GATE_day$dayText.log",
    "EVAL_REAL_MODEL_day$dayText.md",
    "EVAL_REAL_MODEL_day$dayText.errors.json",
    "EVAL_REAL_MODEL_day$dayText.audit_trace.json",
    "API_READY_day$dayText.md",
    "HUMAN_PARITY_day$dayText.md",
    "DAILY_CERT_SUMMARY_day$dayText.md"
)
$existingArtifacts = @()
foreach ($artifact in $artifactNames) {
    $artifactPath = Join-Path $dayDir $artifact
    if (Test-Path $artifactPath) {
        $existingArtifacts += $artifact
    }
}

$existingLogRuns = @()
if (Test-Path $logsRoot) {
    $existingLogRuns = Get-ChildItem -Path $logsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike "operator_checklist*" } |
        Select-Object -ExpandProperty Name
}

$evidenceRootClean = (Test-Path $dayDir) -and ($existingArtifacts.Count -eq 0) -and ($existingLogRuns.Count -eq 0)
$evidenceRootNote = ("existingArtifacts={0}; existingLogRuns={1}" -f $(if ($existingArtifacts.Count -eq 0) { "none" } else { $existingArtifacts -join ", " }), $(if ($existingLogRuns.Count -eq 0) { "none" } else { $existingLogRuns -join ", " }))
$checks.Add((New-ChecklistItem -Number 8 -Check "Evidence root is clean before counted launch" -Status $(if ($evidenceRootClean) { "PASS" } else { "FAIL" }) -Evidence $dayDir -Notes $evidenceRootNote))

$parityBatchScriptPath = Join-Path $root "scripts\run_parity_golden_batch.ps1"
$parityGateEvaluatorPath = Join-Path $root "src\Helper.Runtime\Generation\ParityGateEvaluator.cs"
$targetsLocked = ($ExpectedParityRuns -eq 24) `
    -and ($ExpectedSmokeRuns -eq 50) `
    -and (Test-Path $parityBatchScriptPath) `
    -and (Select-String -Path $parityBatchScriptPath -Pattern '\$successOk = \$successRate -ge 0\.95' -Quiet) `
    -and (Select-String -Path $parityBatchScriptPath -Pattern '\$p95Ok = \$p95 -le 25' -Quiet) `
    -and (Select-String -Path $parityBatchScriptPath -Pattern '\$attemptsOk = \$eligibleAttempts -ge 20' -Quiet) `
    -and (Select-String -Path $parityBatchScriptPath -Pattern '\$goldenHitOk = \$goldenHitRate -ge 0\.90' -Quiet) `
    -and (Test-Path $parityGateEvaluatorPath) `
    -and (Select-String -Path $parityGateEvaluatorPath -Pattern 'MinGoldenHitRate = 0\.90' -Quiet) `
    -and (Select-String -Path $parityGateEvaluatorPath -Pattern 'MinGenerationSuccessRate = 0\.95' -Quiet)
$targetsLockedNote = ("expectedParityRuns={0}; expectedSmokeRuns={1}; successTarget=95%; goldenTarget=90%; p95Target=25s; minGoldenAttempts=20" -f $ExpectedParityRuns, $ExpectedSmokeRuns)
$checks.Add((New-ChecklistItem -Number 9 -Check "Acceptance targets are locked before launch" -Status $(if ($targetsLocked) { "PASS" } else { "FAIL" }) -Evidence "$parityBatchScriptPath; $parityGateEvaluatorPath" -Notes $targetsLockedNote))

$countedRunnerPath = Join-Path $root "scripts\run_precert_counted_day.ps1"
$postRunRuleLocked = (Test-Path $countedRunnerPath) `
    -and (Select-String -Path $countedRunnerPath -Pattern '\$package31Status = if' -Quiet) `
    -and (Select-String -Path $countedRunnerPath -Pattern 'if \(\$verdict -eq "FAILED"\)' -Quiet) `
    -and (Select-String -Path $countedRunnerPath -Pattern 'exit 1' -Quiet)
$postRunRuleNote = "counted runner must fail the launch when verdict=FAILED and keep 3.1 as a blocking gate"
$checks.Add((New-ChecklistItem -Number 10 -Check "Post-run acceptance rule is enforced by the counted runner" -Status $(if ($postRunRuleLocked) { "PASS" } else { "FAIL" }) -Evidence $countedRunnerPath -Notes $postRunRuleNote))

$blockingCheckCount = @($checks | Where-Object { $_.Status -eq "FAIL" }).Count
$report = [pscustomobject]@{
    GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    CycleId = $CycleId
    Day = $Day
    DayText = $dayText
    ExecutionDate = $currentCalendarDate.ToString("yyyy-MM-dd")
    Passed = ($blockingCheckCount -eq 0)
    BlockingCheckCount = $blockingCheckCount
    ParityWorkload = $parityWorkload
    SmokeWorkload = $smokeWorkload
    ProbeWorkload = $probeWorkload
    Checks = @($checks.ToArray())
}

Write-OperatorChecklistReport -MarkdownPath $reportMarkdownPath -JsonPath $reportJsonPath -Report $report
Write-Host ("[PreCertOperatorChecklist] Report: {0}" -f $reportMarkdownPath)

if (-not $report.Passed) {
    Write-Host "[PreCertOperatorChecklist] Checklist failed." -ForegroundColor Yellow
    exit 1
}

Write-Host "[PreCertOperatorChecklist] Checklist passed." -ForegroundColor Green

