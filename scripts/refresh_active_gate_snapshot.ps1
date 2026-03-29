param(
    [string]$SnapshotPath = "doc/certification/active/CURRENT_GATE_SNAPSHOT.json",
    [string]$CurrentStatePath = "doc/CURRENT_STATE.md",
    [string]$CurrentCertStatePath = "doc/certification/active/CURRENT_CERT_STATE.md",
    [string]$ReleaseBaselinePath = "doc/certification/active/CURRENT_RELEASE_BASELINE.json",
    [string]$ExecutionBoardPath = "doc/archive/top_level_history/HELPER_EXECUTION_BOARD_2026-03-16.md",
    [string]$CycleStatePath = "",
    [string]$DaySummaryJsonPath = "",
    [string]$OperatorChecklistPath = "",
    [string]$FrontendBuildStatus = "",
    [string]$FrontendBuildEvidence = "",
    [string]$BackendApiBuildStatus = "",
    [string]$BackendApiBuildEvidence = "",
    [string]$RuntimeCliBuildStatus = "",
    [string]$RuntimeCliBuildEvidence = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common\CertificationSummaryRenderer.ps1")

function Read-JsonFileOrNull {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    return Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Resolve-RepoRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    if (-not $fullRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar) -and -not $fullRoot.EndsWith([System.IO.Path]::AltDirectorySeparatorChar)) {
        $fullRoot += [System.IO.Path]::DirectorySeparatorChar
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootUri = [System.Uri]$fullRoot
    $pathUri = [System.Uri]$fullPath
    $relativeUri = $rootUri.MakeRelativeUri($pathUri)
    return [System.Uri]::UnescapeDataString($relativeUri.ToString()).Replace('/', '/')
}

function Resolve-LatestCycleStatePath {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $cyclesRoot = Join-Path $RepoRoot "doc\pre_certification\cycles"
    $latest = Get-ChildItem -Path $cyclesRoot -Filter "PRECERT_CYCLE_STATE.json" -Recurse -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "[ActiveGate] No PRECERT_CYCLE_STATE.json found under $cyclesRoot"
    }

    return $latest.FullName
}

function Get-BuildSurfaceSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()]$Previous,
        [AllowNull()]$BaselineGate,
        [string]$StatusOverride,
        [string]$EvidenceOverride
    )

    $status = if (-not [string]::IsNullOrWhiteSpace($StatusOverride)) {
        $StatusOverride
    }
    elseif ($null -ne $Previous) {
        [string]$Previous.status
    }
    elseif ($null -ne $BaselineGate) {
        [string]$BaselineGate.status
    }
    else {
        "UNKNOWN"
    }

    $evidence = if (-not [string]::IsNullOrWhiteSpace($EvidenceOverride)) {
        $EvidenceOverride
    }
    elseif ($null -ne $Previous) {
        [string]$Previous.evidence
    }
    elseif ($null -ne $BaselineGate) {
        [string]$BaselineGate.evidence
    }
    else {
        "unverified"
    }

    return [ordered]@{
        status = $status
        evidence = $evidence
    }
}

function Convert-GateListToMap {
    param([AllowNull()]$Gates)

    $map = [ordered]@{}
    foreach ($gate in @($Gates)) {
        if ($null -eq $gate -or [string]::IsNullOrWhiteSpace([string]$gate.id)) {
            continue
        }

        $map[[string]$gate.id] = $gate
    }

    return $map
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

function Get-OperatorChecklistStatus {
    param([string]$ChecklistPath)

    if ([string]::IsNullOrWhiteSpace($ChecklistPath) -or (-not (Test-Path $ChecklistPath))) {
        return "N/A"
    }

    $content = Get-Content -Path $ChecklistPath -Raw -Encoding UTF8
    if ($content -match '(?im)Checklist result:\s*`?GO`?' -or $content -match '(?im)^- Passed:\s*`?YES`?\s*$') {
        return "GO"
    }

    return "NO-GO"
}

function Get-PackageStatusMap {
    param([Parameter(Mandatory = $true)]$Summary)

    $packageMap = [ordered]@{}
    foreach ($package in @($Summary.Packages)) {
        if ($package.Name -match '^(?<key>3\.\d+)') {
            $packageMap[$Matches["key"]] = [string]$package.Status
        }
    }

    return $packageMap
}

function Get-CertificationStatusToken {
    param([Parameter(Mandatory = $true)]$Summary)

    $suffix = switch ([string]$Summary.Verdict) {
        "GREEN_ANCHOR_PENDING" { "GREEN_ANCHOR_PENDING" }
        "PASSED" { "PASS" }
        default { "FAILED" }
    }

    return ("PRECERT_ACTIVE_DAY{0}_{1}" -f $Summary.DayText, $suffix)
}

function Get-CurrentBlockers {
    param(
        [Parameter(Mandatory = $true)]$Summary,
        [Parameter(Mandatory = $true)]$Packages,
        [Parameter(Mandatory = $true)][string]$ExecutionBoardStatus,
        [Parameter(Mandatory = $true)]$Build,
        [AllowNull()]$ReleaseBaseline
    )

    $blockers = New-Object System.Collections.Generic.List[string]
    $freshPrecertReady = $ExecutionBoardStatus -eq "complete" -and $null -ne $ReleaseBaseline -and [string]$ReleaseBaseline.status -eq "PASS"

    if ($null -ne $ReleaseBaseline) {
        foreach ($baselineBlocker in @($ReleaseBaseline.blockers)) {
            $blockers.Add([string]$baselineBlocker)
        }
    }

    if (-not $freshPrecertReady -and ($Packages["3.2"] | Out-String).Trim() -ne "PASS") {
        $windowPackage = @($Summary.Packages | Where-Object { $_.Name -like "3.2*" }) | Select-Object -First 1
        if ($null -ne $windowPackage) {
            $blockers.Add(("The strict 14-day parity window is not yet closed: {0}" -f $windowPackage.Notes))
        }
    }

    foreach ($surface in @(
        [pscustomobject]@{ Name = "frontend"; Data = $Build.frontend },
        [pscustomobject]@{ Name = "backendApi"; Data = $Build.backendApi },
        [pscustomobject]@{ Name = "runtimeCli"; Data = $Build.runtimeCli }
    )) {
        if ([string]$surface.Data.status -ne "PASS") {
            $blockers.Add(("The {0} build surface is not green: {1}" -f $surface.Name, $surface.Data.status))
        }
    }

    if ($ExecutionBoardStatus -ne "complete") {
        $blockers.Add("The execution board is not yet complete, so a fresh full cert run remains intentionally deferred.")
    }

    if (-not $freshPrecertReady -and ($Summary.NextExecutableProfile | Out-String).Trim() -ne "RESET_REQUIRED") {
        $blockers.Add(("The next counted step is date-gated and can run only as {0}." -f $Summary.NextExecutableProfile))
    }

    return @($blockers.ToArray())
}

function Get-CertificationInterpretation {
    param(
        [Parameter(Mandatory = $true)]$Summary,
        [Parameter(Mandatory = $true)][string]$ExecutionBoardStatus,
        [AllowNull()]$ReleaseBaseline
    )

    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($line in @($Summary.InterpretationLines)) {
        $lines.Add([string]$line)
    }

    $lines.Add("This state is valid evidence for the current implementation baseline only.")
    if ($ExecutionBoardStatus -eq "complete" -and $null -ne $ReleaseBaseline -and [string]$ReleaseBaseline.status -eq "PASS") {
        $lines.Add("The roadmap baseline is green, so the next certification action is to open a fresh precert day-01 after freeze instead of continuing the current anchor-pending cycle.")
    }
    elseif ($ExecutionBoardStatus -ne "complete") {
        $lines.Add("A fresh certification campaign should begin only after the execution board is complete and the repository enters freeze.")
    }
    else {
        $lines.Add("The roadmap is complete, but the release baseline is not yet green enough to reopen certification.")
    }

    return @($lines.ToArray())
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedSnapshotPath = Join-Path $repoRoot $SnapshotPath
$resolvedCurrentStatePath = Join-Path $repoRoot $CurrentStatePath
$resolvedCurrentCertStatePath = Join-Path $repoRoot $CurrentCertStatePath
$resolvedReleaseBaselinePath = Join-Path $repoRoot $ReleaseBaselinePath
$resolvedExecutionBoardPath = Join-Path $repoRoot $ExecutionBoardPath

$previousSnapshot = Read-JsonFileOrNull -Path $resolvedSnapshotPath
$releaseBaseline = Read-JsonFileOrNull -Path $resolvedReleaseBaselinePath
$releaseBaselineLocalVerification = Get-OptionalObjectPropertyValue -Object $releaseBaseline -Name "localVerification"
$releaseBaselineCertificationEvidence = Get-OptionalObjectPropertyValue -Object $releaseBaseline -Name "certificationEvidence"
$releaseBaselineBlockers = Get-OptionalObjectPropertyValue -Object $releaseBaseline -Name "blockers"
$baselineGateMap = Convert-GateListToMap -Gates $releaseBaselineLocalVerification

if ([string]::IsNullOrWhiteSpace($CycleStatePath)) {
    if ($null -ne $previousSnapshot -and -not [string]::IsNullOrWhiteSpace([string]$previousSnapshot.certification.evidence.cycleState)) {
        $CycleStatePath = [string]$previousSnapshot.certification.evidence.cycleState
    }
    else {
        $CycleStatePath = Resolve-LatestCycleStatePath -RepoRoot $repoRoot
    }
}

$resolvedCycleStatePath = if ([System.IO.Path]::IsPathRooted($CycleStatePath)) { $CycleStatePath } else { Join-Path $repoRoot $CycleStatePath }
$cycleState = Read-JsonFileOrNull -Path $resolvedCycleStatePath
if ($null -eq $cycleState) {
    throw "[ActiveGate] Cycle state not found: $resolvedCycleStatePath"
}

if ([string]::IsNullOrWhiteSpace($DaySummaryJsonPath)) {
    if ($null -ne $cycleState.lastAssessment -and -not [string]::IsNullOrWhiteSpace([string]$cycleState.lastAssessment.summaryPath)) {
        $candidateJson = [System.IO.Path]::ChangeExtension([string]$cycleState.lastAssessment.summaryPath, ".json")
        if (Test-Path $candidateJson) {
            $DaySummaryJsonPath = $candidateJson
        }
    }
}

if ([string]::IsNullOrWhiteSpace($DaySummaryJsonPath)) {
    throw "[ActiveGate] Day summary json path is required. Re-run counted day summary generation first."
}

$resolvedDaySummaryJsonPath = if ([System.IO.Path]::IsPathRooted($DaySummaryJsonPath)) { $DaySummaryJsonPath } else { Join-Path $repoRoot $DaySummaryJsonPath }
$daySummary = Read-JsonFileOrNull -Path $resolvedDaySummaryJsonPath
if ($null -eq $daySummary) {
    throw "[ActiveGate] Day summary json not found: $resolvedDaySummaryJsonPath"
}

if ([string]::IsNullOrWhiteSpace($OperatorChecklistPath)) {
    $candidateChecklist = Join-Path ([System.IO.Path]::GetDirectoryName($resolvedDaySummaryJsonPath)) ("OPERATOR_CHECKLIST_day{0}.md" -f $daySummary.DayText)
    if (Test-Path $candidateChecklist) {
        $OperatorChecklistPath = $candidateChecklist
    }
}

$resolvedOperatorChecklistPath = if ([string]::IsNullOrWhiteSpace($OperatorChecklistPath)) {
    ""
}
elseif ([System.IO.Path]::IsPathRooted($OperatorChecklistPath)) {
    $OperatorChecklistPath
}
else {
    Join-Path $repoRoot $OperatorChecklistPath
}

$build = [ordered]@{
    frontend = Get-BuildSurfaceSnapshot -Name "frontend" -Previous $previousSnapshot.build.frontend -BaselineGate $baselineGateMap["frontendBuild"] -StatusOverride $FrontendBuildStatus -EvidenceOverride $FrontendBuildEvidence
    backendApi = Get-BuildSurfaceSnapshot -Name "backendApi" -Previous $previousSnapshot.build.backendApi -BaselineGate $baselineGateMap["backendApiBuild"] -StatusOverride $BackendApiBuildStatus -EvidenceOverride $BackendApiBuildEvidence
    runtimeCli = Get-BuildSurfaceSnapshot -Name "runtimeCli" -Previous $previousSnapshot.build.runtimeCli -BaselineGate $baselineGateMap["runtimeCliBuild"] -StatusOverride $RuntimeCliBuildStatus -EvidenceOverride $RuntimeCliBuildEvidence
}

$packages = Get-PackageStatusMap -Summary $daySummary
$executionBoardStatus = Get-ExecutionBoardStatus -BoardPath $resolvedExecutionBoardPath
$cycleDir = Split-Path -Parent $resolvedCycleStatePath
$dayDir = Split-Path -Parent $resolvedDaySummaryJsonPath
$dayReadmePath = Join-Path $dayDir "README.md"

$activeEvidence = New-Object System.Collections.Generic.List[string]
$activeEvidence.Add((Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $resolvedCycleStatePath))
$activeEvidence.Add((Resolve-RepoRelativePath -RepoRoot $repoRoot -Path ([System.IO.Path]::ChangeExtension($resolvedDaySummaryJsonPath, ".md"))))
if (Test-Path $dayReadmePath) {
    $activeEvidence.Add((Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $dayReadmePath))
}
if (-not [string]::IsNullOrWhiteSpace($resolvedOperatorChecklistPath) -and (Test-Path $resolvedOperatorChecklistPath)) {
    $activeEvidence.Add((Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $resolvedOperatorChecklistPath))
}
if ($null -ne $releaseBaseline -and (Test-Path $resolvedReleaseBaselinePath)) {
    $activeEvidence.Add((Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $resolvedReleaseBaselinePath))
    $baselineMarkdownPath = [System.IO.Path]::ChangeExtension($resolvedReleaseBaselinePath, ".md")
    if (Test-Path $baselineMarkdownPath) {
        $activeEvidence.Add((Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $baselineMarkdownPath))
    }
}

$parityBatchPath = Join-Path $dayDir ("PARITY_GOLDEN_BATCH_day{0}.md" -f $daySummary.DayText)
$parityGatePath = Join-Path $dayDir ("HELPER_PARITY_GATE_day{0}.md" -f $daySummary.DayText)
$parityWindowPath = Join-Path $dayDir ("HELPER_PARITY_WINDOW_GATE_day{0}.md" -f $daySummary.DayText)
foreach ($candidate in @($parityBatchPath, $parityGatePath, $parityWindowPath)) {
    if (Test-Path $candidate) {
        $activeEvidence.Add((Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $candidate))
    }
}

$releaseBaselineSection = if ($null -eq $releaseBaseline) {
    [ordered]@{
        status = "UNKNOWN"
        releaseDecision = "release baseline has not been captured"
        path = ""
        localVerification = @()
        certificationEvidence = @()
        blockers = @()
    }
}
else {
    [ordered]@{
        status = [string](Get-OptionalObjectPropertyValue -Object $releaseBaseline -Name "status")
        releaseDecision = [string](Get-OptionalObjectPropertyValue -Object $releaseBaseline -Name "releaseDecision")
        path = Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $resolvedReleaseBaselinePath
        localVerification = @($releaseBaselineLocalVerification)
        certificationEvidence = @($releaseBaselineCertificationEvidence)
        blockers = @($releaseBaselineBlockers)
    }
}

$baselineReady = $executionBoardStatus -eq "complete" -and $releaseBaselineSection.status -eq "PASS"
$overallStatus = if ($baselineReady) {
    "BASELINE_READY"
}
elseif ($executionBoardStatus -eq "complete") {
    "BASELINE_BLOCKED"
}
else {
    "IMPLEMENTATION_ACTIVE"
}

$releaseDecision = if ($baselineReady) {
    "eligible to open a fresh precert day-01 after freeze"
}
elseif ($executionBoardStatus -eq "complete") {
    if ($releaseBaselineSection.releaseDecision) {
        [string]$releaseBaselineSection.releaseDecision
    }
    else {
        "do not start fresh cert until the release baseline is green and the codebase is frozen"
    }
}
else {
    "do not start fresh cert until the execution board is complete and the codebase is frozen"
}

$snapshot = [ordered]@{
    schemaVersion = 3
    generatedOn = (Get-Date).ToString("yyyy-MM-dd")
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    executionBoard = [ordered]@{
        path = (Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $resolvedExecutionBoardPath)
        status = $executionBoardStatus
    }
    overallStatus = $overallStatus
    releaseDecision = $releaseDecision
    build = $build
    releaseBaseline = $releaseBaselineSection
    certification = [ordered]@{
        status = Get-CertificationStatusToken -Summary $daySummary
        activeCycle = [System.IO.Path]::GetFileName($cycleDir)
        cycleStatus = [string]$cycleState.status
        lastResult = [string]$daySummary.Verdict
        officialWindowOpen = [bool]$daySummary.IsOfficialWindowOpen
        closedDays = [int]$cycleState.progress.closedDays
        windowDays = [int]$cycleState.windowDays
        operatorChecklist = Get-OperatorChecklistStatus -ChecklistPath $resolvedOperatorChecklistPath
        nextExecutableProfile = [string]$daySummary.NextExecutableProfile
        earliestOfficialDay01Utc = [string]$daySummary.EarliestOfficialDay01Utc
        packages = $packages
        evidence = [ordered]@{
            cycleState = (Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $resolvedCycleStatePath)
            daySummary = (Resolve-RepoRelativePath -RepoRoot $repoRoot -Path ([System.IO.Path]::ChangeExtension($resolvedDaySummaryJsonPath, ".md")))
            daySummaryJson = (Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $resolvedDaySummaryJsonPath)
            operatorChecklist = if (-not [string]::IsNullOrWhiteSpace($resolvedOperatorChecklistPath) -and (Test-Path $resolvedOperatorChecklistPath)) { Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $resolvedOperatorChecklistPath } else { "" }
        }
        activeEvidence = @($activeEvidence.ToArray())
        interpretation = Get-CertificationInterpretation -Summary $daySummary -ExecutionBoardStatus $executionBoardStatus -ReleaseBaseline $releaseBaseline
    }
}

$snapshot.blockers = Get-CurrentBlockers -Summary $daySummary -Packages $packages -ExecutionBoardStatus $executionBoardStatus -Build $build -ReleaseBaseline $releaseBaseline

Write-ActiveGateDocuments `
    -SnapshotPath $resolvedSnapshotPath `
    -CurrentStatePath $resolvedCurrentStatePath `
    -CurrentCertStatePath $resolvedCurrentCertStatePath `
    -SnapshotDisplayPath $SnapshotPath `
    -Snapshot $snapshot

Write-Host "[ActiveGate] Refreshed CURRENT_GATE_SNAPSHOT.json, CURRENT_STATE.md, and CURRENT_CERT_STATE.md." -ForegroundColor Green
