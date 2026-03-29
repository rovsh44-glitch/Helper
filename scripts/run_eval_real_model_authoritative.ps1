param(
    [string]$ApiBase = "http://localhost:5000",
    [string]$DatasetPath = "eval/human_level_parity_ru_en.jsonl",
    [int]$MaxScenarios = 200,
    [int]$MinScenarioCount = 200,
    [string]$OutputReport = "doc/archive/top_level_history/eval_real_model_report.md",
    [string]$ErrorLogPath = "",
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
    [string]$ReadinessReportPath = "",
    [switch]$WaiveKnowledgePreflight,
    [switch]$SkipWarmup,
    [string]$AuthoritativeSummaryJsonPath = "",
    [string]$AuthoritativeSummaryMarkdownPath = ""
)

$ErrorActionPreference = "Stop"

function Read-ReportLineValue {
    param(
        [string[]]$Lines,
        [string]$Prefix
    )

    foreach ($line in $Lines) {
        if ($line.StartsWith($Prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $line.Substring($Prefix.Length).Trim()
        }
    }

    return ""
}

function Get-OptionalJson {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    try {
        return (Get-Content -Raw $Path | ConvertFrom-Json)
    }
    catch {
        return $null
    }
}

function Get-MarkdownStatusFallback {
    param(
        [string]$Path,
        [string]$Prefix = "Status:"
    )

    if (-not (Test-Path $Path)) {
        return ""
    }

    $lines = Get-Content $Path
    $value = Read-ReportLineValue -Lines $lines -Prefix $Prefix
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        return $value.Replace([string][char]96, "").Trim()
    }

    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed -eq "- PASS") {
            return "PASS"
        }

        if ($trimmed -eq "- FAIL") {
            return "FAIL"
        }
    }

    return ""
}

if ([string]::IsNullOrWhiteSpace($AuthoritativeSummaryJsonPath)) {
    $AuthoritativeSummaryJsonPath = [System.IO.Path]::ChangeExtension($OutputReport, ".authoritative.json")
}

if ([string]::IsNullOrWhiteSpace($AuthoritativeSummaryMarkdownPath)) {
    $AuthoritativeSummaryMarkdownPath = [System.IO.Path]::ChangeExtension($OutputReport, ".authoritative.md")
}

if ([string]::IsNullOrWhiteSpace($ReadinessReportPath)) {
    $ReadinessReportPath = [System.IO.Path]::ChangeExtension($OutputReport, ".api_ready.md")
}

$coreScriptPath = Join-Path $PSScriptRoot "run_eval_real_model.ps1"
$errorLogResolved = if ([string]::IsNullOrWhiteSpace($ErrorLogPath)) { [System.IO.Path]::ChangeExtension($OutputReport, ".errors.json") } else { $ErrorLogPath }
$auditTracePath = [System.IO.Path]::ChangeExtension($OutputReport, ".audit_trace.json")
$datasetReportPath = [System.IO.Path]::ChangeExtension($OutputReport, ".dataset_validation.md")
$knowledgeReportPath = [System.IO.Path]::ChangeExtension($OutputReport, ".knowledge_preflight.md")

$coreStatus = "NOT_STARTED"
$coreFailureMessage = ""
try {
    $coreArgs = @{
        ApiBase = $ApiBase
        DatasetPath = $DatasetPath
        MaxScenarios = $MaxScenarios
        MinScenarioCount = $MinScenarioCount
        OutputReport = $OutputReport
        QdrantBase = $QdrantBase
        SessionToken = $SessionToken
        SessionTtlMinutes = $SessionTtlMinutes
        ApiKey = $ApiKey
        EvidenceLevel = "authoritative"
        RequestTimeoutSec = $RequestTimeoutSec
        RetryBackoffMs = $RetryBackoffMs
        ProgressEvery = $ProgressEvery
        RequireApiReady = $true
        ReadinessTimeoutSec = $ReadinessTimeoutSec
        ReadinessPollIntervalMs = $ReadinessPollIntervalMs
        ReadinessReportPath = $ReadinessReportPath
    }

    if (-not [string]::IsNullOrWhiteSpace($ErrorLogPath)) {
        $coreArgs["ErrorLogPath"] = $ErrorLogPath
    }

    if ($LaunchLocalApiIfUnavailable.IsPresent) {
        $coreArgs["LaunchLocalApiIfUnavailable"] = $true
    }

    if (-not [string]::IsNullOrWhiteSpace($ApiRuntimeDir)) {
        $coreArgs["ApiRuntimeDir"] = $ApiRuntimeDir
    }

    if ($WaiveKnowledgePreflight.IsPresent) {
        $coreArgs["SkipKnowledgePreflight"] = $true
        $coreArgs["KnowledgePreflightWaived"] = $true
    }
    else {
        $coreArgs["FailOnKnowledgePreflight"] = $true
    }

    if ($SkipWarmup.IsPresent) {
        $coreArgs["SkipWarmup"] = $true
    }

    & $coreScriptPath @coreArgs
    $coreStatus = "PASS"
}
catch {
    $coreStatus = "FAIL"
    $coreFailureMessage = $_.Exception.Message
}

$reportExists = Test-Path $OutputReport
$reportLines = if ($reportExists) { Get-Content $OutputReport } else { @() }
$effectiveEvidenceLevel = Read-ReportLineValue -Lines $reportLines -Prefix "Evidence level:"
$requestedEvidenceLevel = Read-ReportLineValue -Lines $reportLines -Prefix "Requested evidence level:"
$mode = Read-ReportLineValue -Lines $reportLines -Prefix "Mode:"
$authoritativeGateStatus = Read-ReportLineValue -Lines $reportLines -Prefix "Authoritative gate status:"
$authoritativeEvidence = Read-ReportLineValue -Lines $reportLines -Prefix "Authoritative evidence:"
$nonAuthoritativeReasons = Read-ReportLineValue -Lines $reportLines -Prefix "Non-authoritative reasons:"
$datasetValidationStatus = Read-ReportLineValue -Lines $reportLines -Prefix "Dataset validation status:"
$apiReadinessStatus = Read-ReportLineValue -Lines $reportLines -Prefix "API readiness status:"
$apiHealthStatus = Read-ReportLineValue -Lines $reportLines -Prefix "API health preflight status:"
$knowledgePreflightStatus = Read-ReportLineValue -Lines $reportLines -Prefix "Knowledge preflight status:"
$sessionBootstrapStatus = Read-ReportLineValue -Lines $reportLines -Prefix "Session bootstrap status:"
$auditTraceJoinStatus = Read-ReportLineValue -Lines $reportLines -Prefix "Audit trace join status:"
$auditTraceMatchedEntries = Read-ReportLineValue -Lines $reportLines -Prefix "Audit trace matched entries:"
$traceabilityStatus = Read-ReportLineValue -Lines $reportLines -Prefix "Traceability status:"
$runtimeErrors = Read-ReportLineValue -Lines $reportLines -Prefix "Runtime errors:"
$passRate = Read-ReportLineValue -Lines $reportLines -Prefix "Pass rate:"
$reportStatus = Read-ReportLineValue -Lines $reportLines -Prefix "Status:"

if ([string]::IsNullOrWhiteSpace($datasetValidationStatus)) {
    $datasetValidationStatus = Get-MarkdownStatusFallback -Path $datasetReportPath
}

if ([string]::IsNullOrWhiteSpace($apiReadinessStatus)) {
    $apiReadinessStatus = Get-MarkdownStatusFallback -Path $ReadinessReportPath -Prefix "- Result:"
}

if ([string]::IsNullOrWhiteSpace($knowledgePreflightStatus)) {
    $knowledgePreflightStatus = if ($WaiveKnowledgePreflight.IsPresent) { "WAIVED" } else { Get-MarkdownStatusFallback -Path $knowledgeReportPath }
}

$errorPayload = Get-OptionalJson -Path $errorLogResolved
$auditPayload = Get-OptionalJson -Path $auditTracePath

$authoritativeStatus = if (($coreStatus -eq "PASS") -and $reportExists -and $authoritativeEvidence.Equals("YES", [System.StringComparison]::OrdinalIgnoreCase)) { "PASS" } else { "FAIL" }
$payload = [ordered]@{
    generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    status = $authoritativeStatus
    coreStatus = $coreStatus
    coreFailureMessage = $coreFailureMessage
    reportPath = $OutputReport
    reportExists = $reportExists
    requestedEvidenceLevel = if ([string]::IsNullOrWhiteSpace($requestedEvidenceLevel)) { "authoritative" } else { $requestedEvidenceLevel }
    effectiveEvidenceLevel = if ([string]::IsNullOrWhiteSpace($effectiveEvidenceLevel)) { "unknown" } else { $effectiveEvidenceLevel }
    mode = if ([string]::IsNullOrWhiteSpace($mode)) { "unknown" } else { $mode }
    authoritativeGateStatus = if ([string]::IsNullOrWhiteSpace($authoritativeGateStatus)) { "FAIL" } else { $authoritativeGateStatus }
    authoritativeEvidence = $authoritativeEvidence
    nonAuthoritativeReasons = if ([string]::IsNullOrWhiteSpace($nonAuthoritativeReasons)) { "unknown" } else { $nonAuthoritativeReasons }
    datasetValidationStatus = if ([string]::IsNullOrWhiteSpace($datasetValidationStatus)) { "unknown" } else { $datasetValidationStatus }
    datasetValidationReport = $datasetReportPath
    apiReadinessStatus = if ([string]::IsNullOrWhiteSpace($apiReadinessStatus)) { "unknown" } else { $apiReadinessStatus }
    apiReadinessReport = $ReadinessReportPath
    apiHealthPreflightStatus = if ([string]::IsNullOrWhiteSpace($apiHealthStatus)) { "unknown" } else { $apiHealthStatus }
    knowledgePreflightStatus = if ([string]::IsNullOrWhiteSpace($knowledgePreflightStatus)) { $(if ($WaiveKnowledgePreflight.IsPresent) { "WAIVED" } else { "unknown" }) } else { $knowledgePreflightStatus }
    knowledgePreflightReport = $knowledgeReportPath
    sessionBootstrapStatus = if ([string]::IsNullOrWhiteSpace($sessionBootstrapStatus)) { "unknown" } else { $sessionBootstrapStatus }
    runtimeErrors = if ([string]::IsNullOrWhiteSpace($runtimeErrors)) { "unknown" } else { $runtimeErrors }
    passRate = if ([string]::IsNullOrWhiteSpace($passRate)) { "unknown" } else { $passRate }
    reportStatus = if ([string]::IsNullOrWhiteSpace($reportStatus)) { "unknown" } else { $reportStatus }
    auditTraceJoinStatus = if ([string]::IsNullOrWhiteSpace($auditTraceJoinStatus)) { $(if ($null -ne $auditPayload) { [string]$auditPayload.readStatus } else { "unknown" }) } else { $auditTraceJoinStatus }
    auditTraceMatchedEntries = if ([string]::IsNullOrWhiteSpace($auditTraceMatchedEntries)) { $(if ($null -ne $auditPayload) { [string]$auditPayload.matched } else { "unknown" }) } else { $auditTraceMatchedEntries }
    auditTraceExpectedEntries = if ($null -ne $auditPayload) { [string]$auditPayload.totalExpectedTraceTurns } else { "unknown" }
    auditEligibleTurns = if ($null -ne $auditPayload) { [string]$auditPayload.totalEligibleAuditTurns } else { "unknown" }
    auditSkippedEligibleTurns = if ($null -ne $auditPayload) { [string]$auditPayload.totalSkippedEligibleAuditTurns } else { "unknown" }
    strictAuditObserved = if ($null -ne $auditPayload) { [bool]$auditPayload.strictAuditObserved } else { $false }
    traceabilityStatus = if ([string]::IsNullOrWhiteSpace($traceabilityStatus)) { $(if ($null -ne $auditPayload -and $null -ne $auditPayload.traceabilityStatus) { [string]$auditPayload.traceabilityStatus } else { "unknown" }) } else { $traceabilityStatus }
    waiverUsed = $WaiveKnowledgePreflight.IsPresent
    errorLogPath = $errorLogResolved
    errorPayloadPresent = ($null -ne $errorPayload)
    auditTracePath = $auditTracePath
    auditPayloadPresent = ($null -ne $auditPayload)
}

$jsonDir = [System.IO.Path]::GetDirectoryName($AuthoritativeSummaryJsonPath)
if (-not [string]::IsNullOrWhiteSpace($jsonDir)) {
    New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null
}
$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $AuthoritativeSummaryJsonPath -Encoding UTF8

$mdDir = [System.IO.Path]::GetDirectoryName($AuthoritativeSummaryMarkdownPath)
if (-not [string]::IsNullOrWhiteSpace($mdDir)) {
    New-Item -ItemType Directory -Force -Path $mdDir | Out-Null
}

$lines = @()
$lines += "# Real-Model Eval Authoritative Gate"
$lines += "Generated: $($payload.generated)"
$lines += "Status: $authoritativeStatus"
$lines += "Core status: $coreStatus"
$lines += "Report path: $OutputReport"
$lines += "Waiver used: $(if ($WaiveKnowledgePreflight.IsPresent) { "YES" } else { "NO" })"
$lines += ""
$lines += "## Topline"
$lines += "- Requested evidence level: $($payload.requestedEvidenceLevel)"
$lines += "- Effective evidence level: $($payload.effectiveEvidenceLevel)"
$lines += "- Mode: $($payload.mode)"
$lines += "- Authoritative gate status: $($payload.authoritativeGateStatus)"
$lines += "- Authoritative evidence: $($payload.authoritativeEvidence)"
$lines += "- Non-authoritative reasons: $($payload.nonAuthoritativeReasons)"
$lines += "- Runtime errors: $($payload.runtimeErrors)"
$lines += "- Pass rate: $($payload.passRate)"
$lines += ""
$lines += "## Preconditions"
$lines += "- Dataset validation: $($payload.datasetValidationStatus)"
$lines += "- API readiness: $($payload.apiReadinessStatus)"
$lines += "- API health preflight: $($payload.apiHealthPreflightStatus)"
$lines += "- Knowledge preflight: $($payload.knowledgePreflightStatus)"
$lines += "- Session bootstrap: $($payload.sessionBootstrapStatus)"
$lines += ""
$lines += "## Traceability"
$lines += "- Audit trace join status: $($payload.auditTraceJoinStatus)"
$lines += "- Audit trace matched entries: $($payload.auditTraceMatchedEntries)"
$lines += "- Audit trace expected entries: $($payload.auditTraceExpectedEntries)"
$lines += "- Audit eligible turns: $($payload.auditEligibleTurns)"
$lines += "- Audit skipped eligible turns: $($payload.auditSkippedEligibleTurns)"
$lines += "- Strict audit observed: $(if ($payload.strictAuditObserved) { "YES" } else { "NO" })"
$lines += "- Traceability status: $($payload.traceabilityStatus)"
$lines += "- Error log: $($payload.errorLogPath)"
$lines += "- Audit trace payload: $($payload.auditTracePath)"
if (-not [string]::IsNullOrWhiteSpace($coreFailureMessage)) {
    $lines += ""
    $lines += "## Failure"
    $lines += "- $coreFailureMessage"
}

Set-Content -Path $AuthoritativeSummaryMarkdownPath -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[EvalRealModelAuthoritative] Summary saved to $AuthoritativeSummaryMarkdownPath"

if ($authoritativeStatus -ne "PASS") {
    throw "[EvalRealModelAuthoritative] Authoritative gate failed."
}

Write-Host "[EvalRealModelAuthoritative] Completed." -ForegroundColor Green
