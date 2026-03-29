param(
    [Parameter(Mandatory = $true)][string]$CycleId,
    [Parameter(Mandatory = $true)][int]$Day,
    [string]$ApiBase = "http://127.0.0.1:5000",
    [string]$ApiKey = "",
    [int]$ParityRuns = 24,
    [int]$SmokeRuns = 50,
    [int]$EvalScenarios = 200,
    [int]$EvalMinScenarioCount = 200,
    [int]$TimeoutSec = 120,
    [int]$ApiReadyTimeoutSec = 600,
    [int]$ApiReadyPollIntervalMs = 2000,
    [int]$ParityLookbackHours = 24,
    [switch]$SkipLlmPreflight,
    [switch]$ReuseExistingArtifacts,
    [switch]$SkipOperatorChecklist
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "precert_day_runner_common.ps1")
. (Join-Path $PSScriptRoot "common\CertificationSummaryRenderer.ps1")

function ConvertTo-PreCertCalendarDate {
    param([Parameter(Mandatory = $true)][string]$IsoDate)

    $parsed = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact($IsoDate, "yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$parsed)) {
        throw "[PreCertCounted] Invalid ISO date: $IsoDate"
    }

    return $parsed.Date
}

function Read-PreCertJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path $Path)) {
        throw "[PreCertCounted] Required JSON file not found: $Path"
    }

    return (Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function ConvertTo-PreCertDouble {
    param([Parameter(Mandatory = $true)][string]$Value)

    $normalized = $Value.Trim().Replace("%", "").Replace(" ", "").Replace(",", ".")
    $number = 0.0
    if ([double]::TryParse($normalized, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
        return $number
    }

    throw "[PreCertCounted] Unable to parse numeric value '$Value'."
}

function Get-MarkdownTableValue {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Label
    )

    foreach ($line in ($Content -split "`r?`n")) {
        if ($line -match '^\|\s*(?<key>[^|]+?)\s*\|\s*(?<value>[^|]+?)\s*\|$') {
            if ($Matches["key"].Trim() -eq $Label) {
                return $Matches["value"].Trim()
            }
        }
    }

    throw "[PreCertCounted] Table value '$Label' not found."
}

function Get-LineValue {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $pattern = "(?m)^" + [regex]::Escape($Label) + ":\s*(?<value>.+?)\s*$"
    $match = [regex]::Match($Content, $pattern)
    if (-not $match.Success) {
        throw "[PreCertCounted] Line value '$Label' not found."
    }

    return $match.Groups["value"].Value.Trim()
}

function Get-MarkdownSectionBullets {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$SectionTitle
    )

    $bullets = New-Object System.Collections.Generic.List[string]
    $inSection = $false
    foreach ($line in ($Content -split "`r?`n")) {
        if ($line -match '^##\s+(?<heading>.+)$') {
            if ($Matches["heading"].Trim() -eq $SectionTitle) {
                $inSection = $true
                continue
            }

            if ($inSection) {
                break
            }
        }

        if ($inSection -and ($line -match '^\-\s+(?<value>.+)$')) {
            $bullets.Add($Matches["value"].Trim())
        }
    }

    return @($bullets.ToArray())
}

function Get-SmokeSummary {
    param([Parameter(Mandatory = $true)][string]$Path)

    $content = Get-Content -Path $Path -Raw -Encoding UTF8
    $noReportMatches = [regex]::Matches($content, '(?m)^\|\s*\d+\s*\|[^|]*\|[^|]*\|[^|]*\|[^|]*\|\s*no\s*\|')
    $topErrorCodes = @(Get-MarkdownSectionBullets -Content $content -SectionTitle "Top Error Codes")
    if ($topErrorCodes.Count -eq 0) {
        $topErrorCodes = @("none")
    }

    return [pscustomobject]@{
        Runs = [int](ConvertTo-PreCertDouble -Value (Get-MarkdownTableValue -Content $content -Label "Runs"))
        CompilePass = [int](ConvertTo-PreCertDouble -Value (Get-MarkdownTableValue -Content $content -Label "Compile pass"))
        PassRate = ConvertTo-PreCertDouble -Value (Get-MarkdownTableValue -Content $content -Label "Pass rate")
        P95DurationSec = ConvertTo-PreCertDouble -Value (Get-MarkdownTableValue -Content $content -Label "P95 duration (sec)")
        NoReportCount = $noReportMatches.Count
        TopErrorCodes = $topErrorCodes
    }
}

function Get-EvalRealModelSummary {
    param([Parameter(Mandatory = $true)][string]$Path)

    $content = Get-Content -Path $Path -Raw -Encoding UTF8
    return [pscustomobject]@{
        ScenariosRun = [int](ConvertTo-PreCertDouble -Value (Get-LineValue -Content $content -Label "Scenarios run"))
        RuntimeErrors = [int](ConvertTo-PreCertDouble -Value (Get-LineValue -Content $content -Label "Runtime errors"))
        QualityFailures = [int](ConvertTo-PreCertDouble -Value (Get-LineValue -Content $content -Label "Quality failures (non-runtime)"))
        PassRatePercent = ConvertTo-PreCertDouble -Value (Get-LineValue -Content $content -Label "Pass rate")
        Status = Get-LineValue -Content $content -Label "Status"
    }
}

function Get-HumanParitySummary {
    param([Parameter(Mandatory = $true)][string]$Path)

    $content = Get-Content -Path $Path -Raw -Encoding UTF8
    $sampleMatch = [regex]::Match($content, '(?m)^Dialog sample size:\s*(?<value>\d+)')
    if (-not $sampleMatch.Success) {
        throw "[PreCertCounted] Human parity sample size not found."
    }

    $kpiChecks = @(Get-MarkdownSectionBullets -Content $content -SectionTitle "KPI checks")
    return [pscustomobject]@{
        SampleSize = [int]$sampleMatch.Groups["value"].Value
        SampleStatus = Get-LineValue -Content $content -Label "Sample status"
        Result = if (@($kpiChecks | Where-Object { $_ -match ':\s*FAIL$' }).Count -gt 0) { "FAIL" } else { "PASS" }
    }
}

function Get-ParitySnapshotEnvelope {
    param(
        [Parameter(Mandatory = $true)][string]$ParityDailyRoot,
        [Parameter(Mandatory = $true)][string]$ExpectedDateIso
    )

    $expectedPath = Join-Path $ParityDailyRoot ("parity_{0}.json" -f $ExpectedDateIso)
    if (Test-Path $expectedPath) {
        return [pscustomobject]@{
            Path = $expectedPath
            Data = Read-PreCertJsonFile -Path $expectedPath
            UsedFallback = $false
        }
    }

    $latest = Get-ChildItem -Path $ParityDailyRoot -Filter "parity_*.json" -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $latest) {
        throw "[PreCertCounted] No parity daily snapshots found in $ParityDailyRoot"
    }

    return [pscustomobject]@{
        Path = $latest.FullName
        Data = Read-PreCertJsonFile -Path $latest.FullName
        UsedFallback = $true
    }
}

function Format-PreCertPercent {
    param([double]$Fraction)
    return ("{0:N2}%" -f ($Fraction * 100.0))
}

function Format-PreCertMetricPercent {
    param([double]$PercentValue)
    return ("{0:N2}%" -f $PercentValue)
}

function Format-PreCertSeconds {
    param([double]$Seconds)
    return ("{0:N2}s" -f $Seconds)
}

function Format-PreCertRatio {
    param([double]$Value)
    return ("{0:N2}" -f $Value)
}

function Get-StepLookup {
    param([Parameter(Mandatory = $true)][object[]]$Steps)

    $lookup = @{}
    foreach ($step in $Steps) {
        $lookup[$step.Name] = $step
    }

    return $lookup
}

function Get-StepResultOrDefault {
    param(
        [Parameter(Mandatory = $true)]$Lookup,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Lookup.ContainsKey($Name)) {
        return $Lookup[$Name]
    }

    return [pscustomobject]@{
        Name = $Name
        Status = "FAIL"
        Notes = "step_not_recorded"
    }
}

function Get-StepLookupFromTrace {
    param([Parameter(Mandatory = $true)][string]$TracePath)

    if (-not (Test-Path $TracePath)) {
        throw "[PreCertCounted] Trace file not found: $TracePath"
    }

    $lookup = @{}
    foreach ($line in Get-Content -Path $TracePath -Encoding UTF8) {
        if ($line -match 'step_end name=(?<name>[^\s]+) status=(?<status>PASS|FAIL)(?: error=(?<error>.*))?$') {
            $lookup[$Matches["name"]] = [pscustomobject]@{
                Name = $Matches["name"]
                Status = $Matches["status"]
                Notes = $Matches["error"]
            }
        }
    }

    return $lookup
}

function Read-PreCertJsonFileOrDefault {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][hashtable]$DefaultValue
    )

    if (Test-Path $Path) {
        return Read-PreCertJsonFile -Path $Path
    }

    return [pscustomobject]$DefaultValue
}

function Get-SmokeSummarySafe {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (Test-Path $Path) {
        return Get-SmokeSummary -Path $Path
    }

    return [pscustomobject]@{
        Runs = 0
        CompilePass = 0
        PassRate = 0.0
        P95DurationSec = 0.0
        NoReportCount = 0
        TopErrorCodes = @("report_missing")
    }
}

function Get-EvalRealModelSummarySafe {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (Test-Path $Path) {
        return Get-EvalRealModelSummary -Path $Path
    }

    return [pscustomobject]@{
        ScenariosRun = 0
        RuntimeErrors = 1
        QualityFailures = 0
        PassRatePercent = 0.0
        Status = "FAIL_MISSING_REPORT"
    }
}

function Get-HumanParitySummarySafe {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (Test-Path $Path) {
        return Get-HumanParitySummary -Path $Path
    }

    return [pscustomobject]@{
        SampleSize = 0
        SampleStatus = "MISSING_REPORT"
        Result = "FAIL"
    }
}

function Get-ArtifactLabel {
    param([Parameter(Mandatory = $true)][string]$Path)

    $label = [System.IO.Path]::GetFileName($Path)
    if (Test-Path $Path) {
        return $label
    }

    return "$label [missing]"
}

function Get-StepFailureNoteFromLogs {
    param(
        [Parameter(Mandatory = $true)][string]$StdoutPath,
        [Parameter(Mandatory = $true)][string]$StderrPath
    )

    foreach ($candidate in @($StderrPath, $StdoutPath)) {
        if (-not (Test-Path $candidate)) {
            continue
        }

        $lines = @(Get-Content -Path $candidate -Encoding UTF8 | ForEach-Object { ([regex]::Replace($_, '\x1b\[[0-9;]*m', '')).Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($lines.Count -gt 0) {
            $headline = $lines | Select-Object -First 1
            $detail = $lines | Where-Object {
                ($_ -ne $headline) -and
                ($_ -notmatch '^\s*Line\s*\|?\s*$') -and
                ($_ -notmatch '^/+$') -and
                ($_ -notmatch '^\s*\d+\s*\|') -and
                ($_ -notmatch '^\s*\|\s*[~\-]+') -and
                ($_ -match 'cannot be found|timeout|Exception|failed|canceled|terminated|missing')
            } | Select-Object -First 1

            if ($null -ne $detail) {
                return ($headline + " / " + $detail)
            }

            return $headline
        }
    }

    return "no_step_output"
}

function Get-WindowGateDecision {
    param(
        [Parameter(Mandatory = $true)]$WindowData,
        [Parameter(Mandatory = $true)][DateTime]$CycleStartDate,
        [Parameter(Mandatory = $true)][DateTime]$CurrentDayDate,
        [Parameter(Mandatory = $true)][bool]$NonWindowPackagesGreen,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$PreviousAssessmentResult,
        [Parameter(Mandatory = $true)][int]$Day,
        [Parameter(Mandatory = $true)][int]$WindowDays
    )

    if ($WindowData.passed) {
        return [pscustomobject]@{
            PackageStatus = "PASS"
            RawStatus = "PASS"
            CurrentCycleFailureDates = @()
            HistoricalFailureDates = @()
        }
    }

    $currentCycleFailureDates = New-Object System.Collections.Generic.List[string]
    $historicalFailureDates = New-Object System.Collections.Generic.List[string]

    foreach ($windowDay in @($WindowData.days)) {
        $windowDayDate = ConvertTo-PreCertCalendarDate -IsoDate $windowDay.DateUtc
        if ($windowDay.Passed) {
            continue
        }

        if (($windowDayDate -ge $CycleStartDate) -and ($windowDayDate -le $CurrentDayDate)) {
            $currentCycleFailureDates.Add($windowDay.DateUtc)
        }
        else {
            $historicalFailureDates.Add($windowDay.DateUtc)
        }
    }

    $eligible = ($Day -lt $WindowDays) `
        -and $NonWindowPackagesGreen `
        -and (-not $WindowData.windowComplete) `
        -and ($PreviousAssessmentResult -ne "FAILED") `
        -and ($currentCycleFailureDates.Count -eq 0)

    return [pscustomobject]@{
        PackageStatus = if ($eligible) { "ANCHOR_PENDING" } else { "FAIL" }
        RawStatus = if ($eligible) { "FAIL_INCOMPLETE_GLOBAL_HISTORY" } else { "FAIL" }
        CurrentCycleFailureDates = @($currentCycleFailureDates.ToArray())
        HistoricalFailureDates = @($historicalFailureDates.ToArray())
    }
}

function Update-PreCertCycleState {
    param(
        [Parameter(Mandatory = $true)]$State,
        [Parameter(Mandatory = $true)][string]$StatePath,
        [Parameter(Mandatory = $true)][int]$Day,
        [Parameter(Mandatory = $true)][string]$Verdict,
        [Parameter(Mandatory = $true)][string]$RawWindowGate,
        [Parameter(Mandatory = $true)][string]$SummaryPath
    )

    $dayText = $Day.ToString("00")
    $dayLabel = ConvertTo-PreCertDayLabel -Day $Day

    switch ($Verdict) {
        "GREEN_ANCHOR_PENDING" {
            $State.status = "day{0}_closed_green_anchor_pending" -f $dayText
            $State.progress.closedDays = $Day
            $State.progress.lastClosedDay = $dayLabel
        }
        "PASSED" {
            $State.status = "day{0}_closed_pass" -f $dayText
            $State.progress.closedDays = $Day
            $State.progress.lastClosedDay = $dayLabel
        }
        default {
            $State.status = "day{0}_failed" -f $dayText
            $State.progress.closedDays = 0
            $State.progress.lastClosedDay = ""
        }
    }

    $lastAssessment = [ordered]@{
        day = $dayText
        result = $Verdict
        rawWindowGate = $RawWindowGate
        summaryPath = [System.IO.Path]::GetFullPath($SummaryPath)
    }
    if ($null -ne $State.PSObject.Properties["lastAssessment"]) {
        $State.lastAssessment = $lastAssessment
    }
    else {
        Add-Member -InputObject $State -MemberType NoteProperty -Name "lastAssessment" -Value $lastAssessment
    }

    $lastUpdatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    if ($null -ne $State.PSObject.Properties["lastUpdatedUtc"]) {
        $State.lastUpdatedUtc = $lastUpdatedUtc
    }
    else {
        Add-Member -InputObject $State -MemberType NoteProperty -Name "lastUpdatedUtc" -Value $lastUpdatedUtc
    }

    $State | ConvertTo-Json -Depth 8 | Set-Content -Path $StatePath -Encoding UTF8
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

$cycleDir = Join-Path $root ("doc\pre_certification\cycles\" + $CycleId)
if (-not (Test-Path $cycleDir)) {
    throw "[PreCertCounted] Cycle not found: $cycleDir"
}

if ($Day -lt 1 -or $Day -gt 14) {
    throw "[PreCertCounted] Day must be between 1 and 14."
}

$statePath = Join-Path $cycleDir "PRECERT_CYCLE_STATE.json"
$state = Read-PreCertJsonFile -Path $statePath
$cycleStartDate = ConvertTo-PreCertCalendarDate -IsoDate $state.startDateUtc
$expectedCalendarDate = $cycleStartDate.AddDays($Day - 1)
$currentCalendarDate = (Get-Date).Date
$dayLabel = ConvertTo-PreCertDayLabel -Day $Day
$dayText = $Day.ToString("00")
$expectedClosedDays = [int]$state.progress.closedDays + 1

if (-not $ReuseExistingArtifacts.IsPresent) {
    if ($Day -ne $expectedClosedDays) {
        throw ("[PreCertCounted] Requested day-{0} but cycle expects {1} next." -f $dayText, (ConvertTo-PreCertDayLabel -Day $expectedClosedDays))
    }

    if ($currentCalendarDate -ne $expectedCalendarDate) {
        throw ("[PreCertCounted] Day-{0} is scheduled for {1:yyyy-MM-dd}; current calendar date is {2:yyyy-MM-dd}." -f $dayText, $expectedCalendarDate, $currentCalendarDate)
    }
}

$dayDir = Join-Path $cycleDir $dayLabel
$logsRoot = Join-Path $dayDir "logs"
New-Item -ItemType Directory -Force -Path $dayDir | Out-Null

if ((-not $ReuseExistingArtifacts.IsPresent) -and ($Day -eq 1) -and (-not $SkipOperatorChecklist.IsPresent)) {
    Write-Host "[PreCertCounted] Running operator checklist preflight..."
    $operatorChecklistLogDir = Join-Path $dayDir ("operator_checklist_wrapper_logs\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    Invoke-PreCertChildScript `
        -StepName "operator_checklist" `
        -ScriptPath (Join-Path $root "scripts\run_precert_operator_checklist.ps1") `
        -Arguments @(
            "-CycleId", $CycleId,
            "-Day", $Day.ToString(),
            "-WorkspaceRoot", $root,
            "-ExpectedParityRuns", $ParityRuns.ToString(),
            "-ExpectedSmokeRuns", $SmokeRuns.ToString(),
            "-ProbeTimeoutSec", $TimeoutSec.ToString()
        ) `
        -WorkingDirectory $root `
        -LogDirectory $operatorChecklistLogDir
}

$stepLookup = $null
$runLogDir = ""

if ($ReuseExistingArtifacts.IsPresent) {
    $existingRun = Get-ChildItem -Path $logsRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
    if ($null -eq $existingRun) {
        throw "[PreCertCounted] No existing counted artifacts/logs found to reuse."
    }

    $runLogDir = $existingRun.FullName
    $script:PreCertTracePath = Join-Path $runLogDir "counted_wrapper.trace.log"
    $stepLookup = Get-StepLookupFromTrace -TracePath $script:PreCertTracePath
}
else {
    $runId = Get-Date -Format "yyyyMMdd-HHmmss"
    $runLogDir = Join-Path $logsRoot $runId
    New-Item -ItemType Directory -Force -Path $runLogDir | Out-Null

    $script:PreCertTracePath = Join-Path $runLogDir "counted_wrapper.trace.log"
    Set-Content -Path $script:PreCertTracePath -Value @(
        "# Counted Wrapper Trace"
        ("cycle={0}" -f $CycleId)
        ("day={0}" -f $dayLabel)
        ("runId={0}" -f $runId)
    ) -Encoding UTF8
}

$cycleToken = $CycleId.ToLowerInvariant().Replace("precert_", "")
$workloadPrefix = ("precert-{0}-{1}" -f $cycleToken, $dayLabel)
$parityWorkload = $workloadPrefix + "-parity"
$smokeWorkload = $workloadPrefix + "-smoke"
$resolvedApiKey = Get-PreCertApiKey -WorkspaceRoot $root -ExplicitApiKey $ApiKey

if (-not $ReuseExistingArtifacts.IsPresent) {
    $steps = Invoke-PreCertPackageSequence `
        -WorkspaceRoot $root `
        -LogDirectory $runLogDir `
        -ParityWorkload $parityWorkload `
        -SmokeWorkload $smokeWorkload `
        -RuntimeDir $dayDir `
        -LlmPreflightReportPath (Join-Path $dayDir ("LLM_LATENCY_PREFLIGHT_day{0}.md" -f $dayText)) `
        -ParityBatchReportPath (Join-Path $dayDir ("PARITY_GOLDEN_BATCH_day{0}.md" -f $dayText)) `
        -ParityGateReportPath (Join-Path $dayDir ("HELPER_PARITY_GATE_day{0}.md" -f $dayText)) `
        -ParityWindowReportPath (Join-Path $dayDir ("HELPER_PARITY_WINDOW_GATE_day{0}.md" -f $dayText)) `
        -SmokeCompileReportPath (Join-Path $dayDir ("SMOKE_COMPILE_day{0}.md" -f $dayText)) `
        -ClosedLoopReportPath (Join-Path $dayDir ("CLOSED_LOOP_PREDICTABILITY_day{0}.md" -f $dayText)) `
        -EvalGateLogPath (Join-Path $dayDir ("EVAL_GATE_day{0}.log" -f $dayText)) `
        -EvalRealModelOutputPath (Join-Path $dayDir ("EVAL_REAL_MODEL_day{0}.md" -f $dayText)) `
        -EvalRealModelErrorLogPath (Join-Path $dayDir ("EVAL_REAL_MODEL_day{0}.errors.json" -f $dayText)) `
        -EvalRealModelReadinessReportPath (Join-Path $dayDir ("API_READY_day{0}.md" -f $dayText)) `
        -HumanParityReportPath (Join-Path $dayDir ("HUMAN_PARITY_day{0}.md" -f $dayText)) `
        -ApiBase $ApiBase `
        -ApiKey $resolvedApiKey `
        -ParityRuns $ParityRuns `
        -SmokeRuns $SmokeRuns `
        -EvalScenarios $EvalScenarios `
        -EvalMinScenarioCount $EvalMinScenarioCount `
        -TimeoutSec $TimeoutSec `
        -ApiReadyTimeoutSec $ApiReadyTimeoutSec `
        -ApiReadyPollIntervalMs $ApiReadyPollIntervalMs `
        -ParityLookbackHours $ParityLookbackHours `
        -SkipLlmPreflight:$SkipLlmPreflight

    $stepLookup = Get-StepLookup -Steps $steps
}
$windowJsonPath = Join-Path $dayDir ("HELPER_PARITY_WINDOW_GATE_day{0}.json" -f $dayText)
$closedLoopJsonPath = Join-Path $dayDir ("CLOSED_LOOP_PREDICTABILITY_day{0}.json" -f $dayText)
$smokeReportPath = Join-Path $dayDir ("SMOKE_COMPILE_day{0}.md" -f $dayText)
$evalRealModelReportPath = Join-Path $dayDir ("EVAL_REAL_MODEL_day{0}.md" -f $dayText)
$evalRealModelErrorLogPath = Join-Path $dayDir ("EVAL_REAL_MODEL_day{0}.errors.json" -f $dayText)
$humanParityReportPath = Join-Path $dayDir ("HUMAN_PARITY_day{0}.md" -f $dayText)
$dailySummaryPath = Join-Path $dayDir ("DAILY_CERT_SUMMARY_day{0}.md" -f $dayText)
$dailySummaryJsonPath = Join-Path $dayDir ("DAILY_CERT_SUMMARY_day{0}.json" -f $dayText)
$readmePath = Join-Path $dayDir "README.md"

$windowData = Read-PreCertJsonFileOrDefault -Path $windowJsonPath -DefaultValue @{
    windowDays = [int]$state.windowDays
    availableDays = 0
    windowComplete = $false
    passed = $false
    days = @()
}
$closedLoopData = Read-PreCertJsonFileOrDefault -Path $closedLoopJsonPath -DefaultValue @{
    topIncidentClasses = 0
    repeatsPerClass = 0
    maxAllowedVariance = 0.05
    passed = $false
}
$smokeSummary = Get-SmokeSummarySafe -Path $smokeReportPath
$evalSummary = Get-EvalRealModelSummarySafe -Path $evalRealModelReportPath
$humanSummary = Get-HumanParitySummarySafe -Path $humanParityReportPath
$paritySnapshotEnvelope = Get-ParitySnapshotEnvelope -ParityDailyRoot $state.directories.parityDaily -ExpectedDateIso $currentCalendarDate.ToString("yyyy-MM-dd")
$paritySnapshot = $paritySnapshotEnvelope.Data

$parityGateStep = Get-StepResultOrDefault -Lookup $stepLookup -Name "parity_gate"
$smokeCompileStep = Get-StepResultOrDefault -Lookup $stepLookup -Name "smoke_compile"
$evalGateStep = Get-StepResultOrDefault -Lookup $stepLookup -Name "eval_gate"
$evalRealModelStep = Get-StepResultOrDefault -Lookup $stepLookup -Name "eval_real_model"
$previousAssessmentResult = ""
if ($null -ne $state.PSObject.Properties["lastAssessment"] -and $null -ne $state.lastAssessment) {
    if ($null -ne $state.lastAssessment.PSObject.Properties["result"]) {
        $previousAssessmentResult = [string]$state.lastAssessment.result
    }
}
$evalFailureNote = if ($evalRealModelStep.Status -ne "PASS") {
    Get-StepFailureNoteFromLogs `
        -StdoutPath (Join-Path $runLogDir "eval_real_model.stdout.log") `
        -StderrPath (Join-Path $runLogDir "eval_real_model.stderr.log")
}
else {
    ""
}

$package31Status = if (($parityGateStep.Status -eq "PASS") -and (-not $paritySnapshot.GoldenSampleInsufficient)) { "PASS" } else { "FAIL" }
$package33Status = if ($smokeCompileStep.Status -eq "PASS") { "PASS" } else { "FAIL" }
$package34Status = if ($closedLoopData.passed) { "PASS" } else { "FAIL" }
$package35Status = if (($evalGateStep.Status -eq "PASS") -and ($evalRealModelStep.Status -eq "PASS") -and ($evalSummary.Status -eq "PASS") -and ($humanSummary.Result -eq "PASS")) { "PASS" } else { "FAIL" }
$nonWindowPackagesGreen = ($package31Status -eq "PASS") -and ($package33Status -eq "PASS") -and ($package34Status -eq "PASS") -and ($package35Status -eq "PASS")
$windowDecision = Get-WindowGateDecision `
    -WindowData $windowData `
    -CycleStartDate $cycleStartDate `
    -CurrentDayDate $currentCalendarDate `
    -NonWindowPackagesGreen $nonWindowPackagesGreen `
    -PreviousAssessmentResult $previousAssessmentResult `
    -Day $Day `
    -WindowDays ([int]$state.windowDays)

$verdict = "FAILED"
if ($nonWindowPackagesGreen) {
    if ($windowDecision.PackageStatus -eq "PASS") {
        $verdict = "PASSED"
    }
    elseif ($windowDecision.PackageStatus -eq "ANCHOR_PENDING") {
        $verdict = "GREEN_ANCHOR_PENDING"
    }
}

$historicalFailuresText = if ($windowDecision.HistoricalFailureDates.Count -gt 0) { $windowDecision.HistoricalFailureDates -join ", " } else { "none" }
$currentCycleFailuresText = if ($windowDecision.CurrentCycleFailureDates.Count -gt 0) { $windowDecision.CurrentCycleFailureDates -join ", " } else { "none" }

$packages = @(
    [pscustomobject]@{
        Name = "3.1 Parity certification snapshot"
        Status = $package31Status
        Evidence = ("{0}, {1}, {2}" -f [System.IO.Path]::GetFileName((Join-Path $dayDir ("PARITY_GOLDEN_BATCH_day{0}.md" -f $dayText))), [System.IO.Path]::GetFileName((Join-Path $dayDir ("HELPER_PARITY_GATE_day{0}.md" -f $dayText))), [System.IO.Path]::GetFileName($paritySnapshotEnvelope.Path))
        Notes = ("TotalRuns={0}, GoldenHit={1}, Success={2}, P95Ready={3}, UnknownError={4}" -f $paritySnapshot.TotalRuns, (Format-PreCertPercent -Fraction $paritySnapshot.GoldenHitRate), (Format-PreCertPercent -Fraction $paritySnapshot.GenerationSuccessRate), (Format-PreCertSeconds -Seconds $paritySnapshot.P95ReadySeconds), (Format-PreCertPercent -Fraction $paritySnapshot.UnknownErrorRate))
    },
    [pscustomobject]@{
        Name = "3.2 Parity window gate (14d strict)"
        Status = $windowDecision.PackageStatus
        Evidence = ("{0}, {1}" -f [System.IO.Path]::GetFileName((Join-Path $dayDir ("HELPER_PARITY_WINDOW_GATE_day{0}.md" -f $dayText))), [System.IO.Path]::GetFileName($windowJsonPath))
        Notes = ("AvailableDays={0}/{1}, WindowComplete={2}, CurrentCycleFailures={3}, HistoricalFailures={4}" -f $windowData.availableDays, $windowData.windowDays, $windowData.windowComplete, $currentCycleFailuresText, $historicalFailuresText)
    },
    [pscustomobject]@{
        Name = "3.3 Real-task generation smoke"
        Status = $package33Status
        Evidence = [System.IO.Path]::GetFileName($smokeReportPath)
        Notes = ("Runs={0}, CompilePass={1}/{0}, PassRate={2}, P95={3}, NoReport={4}, TopErrors={5}" -f $smokeSummary.Runs, $smokeSummary.CompilePass, (Format-PreCertRatio -Value $smokeSummary.PassRate), (Format-PreCertSeconds -Seconds $smokeSummary.P95DurationSec), $smokeSummary.NoReportCount, ($smokeSummary.TopErrorCodes -join "; "))
    },
    [pscustomobject]@{
        Name = "3.4 Closed-loop predictability"
        Status = $package34Status
        Evidence = ("{0}, {1}" -f [System.IO.Path]::GetFileName((Join-Path $dayDir ("CLOSED_LOOP_PREDICTABILITY_day{0}.md" -f $dayText))), [System.IO.Path]::GetFileName($closedLoopJsonPath))
        Notes = ("TopIncidentClasses={0}, RepeatsPerClass={1}, MaxAllowedVariance={2}, Passed={3}" -f $closedLoopData.topIncidentClasses, $closedLoopData.repeatsPerClass, (Format-PreCertPercent -Fraction $closedLoopData.maxAllowedVariance), $closedLoopData.passed)
    },
    [pscustomobject]@{
        Name = "3.5 Dialog quality eval suite"
        Status = $package35Status
        Evidence = ("{0}, {1}, {2}, {3}" -f [System.IO.Path]::GetFileName((Join-Path $dayDir ("EVAL_GATE_day{0}.log" -f $dayText))), (Get-ArtifactLabel -Path $evalRealModelReportPath), (Get-ArtifactLabel -Path $evalRealModelErrorLogPath), (Get-ArtifactLabel -Path $humanParityReportPath))
        Notes = ("eval_gate={0}, eval_real_model={1}, scenarios={2}, runtimeErrors={3}, qualityFailures={4}, passRate={5}, humanSample={6}/{7}, failureNote={8}" -f $evalGateStep.Status, $evalRealModelStep.Status, $evalSummary.ScenariosRun, $evalSummary.RuntimeErrors, $evalSummary.QualityFailures, (Format-PreCertMetricPercent -PercentValue $evalSummary.PassRatePercent), $humanSummary.SampleSize, $humanSummary.SampleStatus, $(if ([string]::IsNullOrWhiteSpace($evalFailureNote)) { "none" } else { $evalFailureNote.Replace("|", "/") }))
    }
)

$interpretationLines = New-Object System.Collections.Generic.List[string]
if ($windowDecision.PackageStatus -eq "ANCHOR_PENDING") {
    $interpretationLines.Add(("Raw strict `3.2` remains anchor-pending because the 14-day moving window is incomplete (`{0}/{1}`) and only historical pre-cycle red days remain in scope (`{2}`)." -f $windowData.availableDays, $windowData.windowDays, $historicalFailuresText))
    $interpretationLines.Add(("Current cycle dates `{0:yyyy-MM-dd}` .. `{1:yyyy-MM-dd}` have no strict window failures." -f $cycleStartDate, $currentCalendarDate))
    $interpretationLines.Add("This counted closure increments only the pre-cert counter; official Day 01 remains unavailable.")
}
else {
    if ($package31Status -eq "FAIL") { $interpretationLines.Add(("`3.1` failed: {0}" -f $packages[0].Notes)) }
    if ($windowDecision.PackageStatus -eq "FAIL") { $interpretationLines.Add(("`3.2` failed as a counted gate: CurrentCycleFailures={0}; HistoricalFailures={1}; WindowComplete={2}." -f $currentCycleFailuresText, $historicalFailuresText, $windowData.windowComplete)) }
    if ($package33Status -eq "FAIL") { $interpretationLines.Add(("`3.3` failed: {0}" -f $packages[2].Notes)) }
    if ($package34Status -eq "FAIL") { $interpretationLines.Add(("`3.4` failed: {0}" -f $packages[3].Notes)) }
    if ($package35Status -eq "FAIL") { $interpretationLines.Add(("`3.5` failed: {0}" -f $packages[4].Notes)) }
}

$earliestOfficialDay01Date = ConvertTo-PreCertCalendarDate -IsoDate $state.earliestOfficialDay01Utc
$summary = [pscustomobject]@{
    CycleId = $CycleId
    Day = $Day
    DayText = $dayText
    ExecutionDateIso = $currentCalendarDate.ToString("yyyy-MM-dd")
    Verdict = $verdict
    EarliestOfficialDay01Utc = $state.earliestOfficialDay01Utc
    EarliestOfficialDay02Utc = $earliestOfficialDay01Date.AddDays(1).ToString("yyyy-MM-dd")
    EarliestOfficialDay14Utc = $state.earliestOfficialDay14Utc
    IsOfficialWindowOpen = ($currentCalendarDate -ge $earliestOfficialDay01Date)
    ParityWorkload = $parityWorkload
    SmokeWorkload = $smokeWorkload
    ParityGoldenHitRate = Format-PreCertPercent -Fraction $paritySnapshot.GoldenHitRate
    ParitySuccessRate = Format-PreCertPercent -Fraction $paritySnapshot.GenerationSuccessRate
    ParityP95Ready = Format-PreCertSeconds -Seconds $paritySnapshot.P95ReadySeconds
    ParityUnknownErrorRate = Format-PreCertPercent -Fraction $paritySnapshot.UnknownErrorRate
    SmokeRuns = $smokeSummary.Runs
    SmokeCompilePass = $smokeSummary.CompilePass
    SmokePassRate = Format-PreCertRatio -Value $smokeSummary.PassRate
    SmokeP95Duration = Format-PreCertSeconds -Seconds $smokeSummary.P95DurationSec
    EvalScenariosRun = $evalSummary.ScenariosRun
    EvalRuntimeErrors = $evalSummary.RuntimeErrors
    EvalQualityFailures = $evalSummary.QualityFailures
    EvalPassRate = Format-PreCertMetricPercent -PercentValue $evalSummary.PassRatePercent
    HumanSampleSize = $humanSummary.SampleSize
    HumanSampleStatus = $humanSummary.SampleStatus
    ParitySnapshotFile = [System.IO.Path]::GetFileName($paritySnapshotEnvelope.Path)
    ParitySnapshotUsedFallback = $paritySnapshotEnvelope.UsedFallback
    SequentialGuard = "YES"
    CalendarGuard = "YES"
    StrictWindowGuard = if ($state.strict.HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE -eq "false") { "YES" } else { "NO" }
    SoftBypassFlagsUsed = if ($state.strict.noSoftBypassFlags) { "NO" } else { "YES" }
    Packages = $packages
    InterpretationLines = @($interpretationLines.ToArray())
    PreCertCounter = if ($verdict -eq "FAILED") { "0/14 (reset)" } else { "{0}/14" -f $Day }
    OfficialCounter = if ($verdict -eq "PASSED") { "{0}/14" -f $Day } else { "N/A" }
    ReleaseDecision = if ($verdict -eq "FAILED") { "NO-GO" } else { "GO" }
    NextExecutableProfile = if ($verdict -eq "FAILED") { "RESET_REQUIRED" } else { "{0}/day-{1}" -f $CycleId, ($Day + 1).ToString("00") }
    PrimaryArtifacts = @(
        $(if (($Day -eq 1) -and (-not $SkipOperatorChecklist.IsPresent)) { "OPERATOR_CHECKLIST_day$dayText.md" }),
        "DAILY_CERT_SUMMARY_day$dayText.md",
        "HELPER_PARITY_GATE_day$dayText.md",
        "HELPER_PARITY_WINDOW_GATE_day$dayText.md",
        "SMOKE_COMPILE_day$dayText.md",
        "CLOSED_LOOP_PREDICTABILITY_day$dayText.md",
        "EVAL_GATE_day$dayText.log",
        "EVAL_REAL_MODEL_day$dayText.md",
        "HUMAN_PARITY_day$dayText.md"
    )
}

Write-CountedDayArtifacts -MarkdownPath $dailySummaryPath -JsonPath $dailySummaryJsonPath -ReadmePath $readmePath -Summary $summary
Update-PreCertCycleState -State $state -StatePath $statePath -Day $Day -Verdict $verdict -RawWindowGate $windowDecision.RawStatus -SummaryPath $dailySummaryPath
Write-PreCertTrace -Message ("counted_summary_written path={0} verdict={1}" -f $dailySummaryPath, $verdict)

$refreshArgs = @{
    SnapshotPath = "doc/certification/active/CURRENT_GATE_SNAPSHOT.json"
    CurrentStatePath = "doc/CURRENT_STATE.md"
    CurrentCertStatePath = "doc/certification/active/CURRENT_CERT_STATE.md"
    ExecutionBoardPath = "doc/archive/top_level_history/HELPER_EXECUTION_BOARD_2026-03-16.md"
    CycleStatePath = $statePath
    DaySummaryJsonPath = $dailySummaryJsonPath
}
if (($Day -eq 1) -and (-not $SkipOperatorChecklist.IsPresent)) {
    $operatorChecklistPath = Join-Path $dayDir ("OPERATOR_CHECKLIST_day{0}.md" -f $dayText)
    if (Test-Path $operatorChecklistPath) {
        $refreshArgs.OperatorChecklistPath = $operatorChecklistPath
    }
}

& (Join-Path $PSScriptRoot "refresh_active_gate_snapshot.ps1") @refreshArgs

Write-Host ("[PreCertCounted] Summary: {0}" -f $dailySummaryPath)
if ($verdict -eq "FAILED") {
    Write-PreCertTrace -Message "counted_completed status=FAIL"
    Write-Host "[PreCertCounted] Counted day failed." -ForegroundColor Yellow
    exit 1
}

Write-PreCertTrace -Message ("counted_completed status={0}" -f $verdict)
Write-Host ("[PreCertCounted] Completed with verdict {0}." -f $verdict) -ForegroundColor Green
