param(
    [string]$Date = "",
    [string]$OutputPath = "",
    [string]$EvidenceLevel = "live_non_authoritative",
    [string]$MetricSeedPath = "",
    [double]$TtftLocalMs = -1,
    [double]$TtftNetworkMs = -1,
    [double]$ConversationSuccessRate = -1,
    [double]$Helpfulness = -1,
    [double]$CitationPrecision = -1,
    [double]$CitationCoverage = -1,
    [double]$ToolCorrectness = -1,
    [int]$SecurityIncidents = -1,
    [int]$OpenP0P1 = -1,
    [string]$BlindHumanEvalReportPath = "doc/archive/top_level_history/human_eval_parity_report_latest.md",
    [string]$RealModelEvalReportPath = "doc/archive/top_level_history/eval_real_model_report.md",
    [string]$ReleaseBaselinePath = "doc/certification/active/CURRENT_RELEASE_BASELINE.json",
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\ParityEvidenceCommon.ps1")

$allowedEvidenceLevels = @("sample", "synthetic", "dry_run", "live_non_authoritative", "authoritative")
if ($allowedEvidenceLevels -notcontains $EvidenceLevel) {
    throw "[ParityDaily] Invalid EvidenceLevel '$EvidenceLevel'. Allowed values: $($allowedEvidenceLevels -join ", ")."
}

if ([string]::IsNullOrWhiteSpace($Date)) {
    $Date = Get-Date -Format "yyyy-MM-dd"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path "doc/parity_daily" ("parity_snapshot_{0}.json" -f $Date.Replace("-", ""))
}

$seed = $null
if (-not [string]::IsNullOrWhiteSpace($MetricSeedPath)) {
    if (-not (Test-Path $MetricSeedPath)) {
        throw "[ParityDaily] MetricSeedPath not found: $MetricSeedPath"
    }

    $seed = Get-Content -Raw $MetricSeedPath | ConvertFrom-Json
}

function Resolve-MetricValue {
    param(
        $ExplicitValue,
        $SeedObject,
        [string]$SeedProperty,
        [string]$MetricName
    )

    if ($ExplicitValue -is [double]) {
        if ($ExplicitValue -ge 0) {
            return [double]$ExplicitValue
        }
    }
    elseif ($ExplicitValue -is [int]) {
        if ($ExplicitValue -ge 0) {
            return [int]$ExplicitValue
        }
    }

    if ($null -ne $SeedObject) {
        $property = $SeedObject.PSObject.Properties[$SeedProperty]
        if ($null -ne $property -and $null -ne $property.Value) {
            return $property.Value
        }
    }

    throw "[ParityDaily] Missing required metric '$MetricName'. Provide it explicitly or via MetricSeedPath."
}

$blindHumanEval = Get-BlindHumanEvalReportMetadata -Path $BlindHumanEvalReportPath
$realModelEval = Get-RealModelEvalReportMetadata -Path $RealModelEvalReportPath
$releaseBaselineStatus = "missing"
if (Test-Path $ReleaseBaselinePath) {
    try {
        $releaseBaseline = Get-Content -Raw $ReleaseBaselinePath | ConvertFrom-Json
        $releaseBaselineStatus = [string]$releaseBaseline.status
    }
    catch {
        $releaseBaselineStatus = "invalid"
    }
}

$payload = [ordered]@{
    schemaVersion = 2
    generatedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    date = $Date
    evidenceLevel = $EvidenceLevel
    ttft_local_ms = [double](Resolve-MetricValue -ExplicitValue $TtftLocalMs -SeedObject $seed -SeedProperty "ttft_local_ms" -MetricName "ttft_local_ms")
    ttft_network_ms = [double](Resolve-MetricValue -ExplicitValue $TtftNetworkMs -SeedObject $seed -SeedProperty "ttft_network_ms" -MetricName "ttft_network_ms")
    conversation_success_rate = [double](Resolve-MetricValue -ExplicitValue $ConversationSuccessRate -SeedObject $seed -SeedProperty "conversation_success_rate" -MetricName "conversation_success_rate")
    helpfulness = [double](Resolve-MetricValue -ExplicitValue $Helpfulness -SeedObject $seed -SeedProperty "helpfulness" -MetricName "helpfulness")
    citation_precision = [double](Resolve-MetricValue -ExplicitValue $CitationPrecision -SeedObject $seed -SeedProperty "citation_precision" -MetricName "citation_precision")
    citation_coverage = [double](Resolve-MetricValue -ExplicitValue $CitationCoverage -SeedObject $seed -SeedProperty "citation_coverage" -MetricName "citation_coverage")
    tool_correctness = [double](Resolve-MetricValue -ExplicitValue $ToolCorrectness -SeedObject $seed -SeedProperty "tool_correctness" -MetricName "tool_correctness")
    security_incidents = [int](Resolve-MetricValue -ExplicitValue $SecurityIncidents -SeedObject $seed -SeedProperty "security_incidents" -MetricName "security_incidents")
    open_p0_p1 = [int](Resolve-MetricValue -ExplicitValue $OpenP0P1 -SeedObject $seed -SeedProperty "open_p0_p1" -MetricName "open_p0_p1")
    blind_human_eval_status = if ($blindHumanEval.exists) { $(if ($blindHumanEval.authoritative) { "AUTHORITATIVE" } else { [string]$blindHumanEval.integritySufficiency }) } else { "missing" }
    real_model_eval_status = if ($realModelEval.exists) { $(if ($realModelEval.authoritative) { "AUTHORITATIVE" } else { [string]$realModelEval.authoritativeGateStatus }) } else { "missing" }
    release_baseline_status = $releaseBaselineStatus
    sourceLinks = [ordered]@{
        blindHumanEvalReport = $BlindHumanEvalReportPath
        realModelEvalReport = $RealModelEvalReportPath
        releaseBaseline = $ReleaseBaselinePath
        metricSeed = if ([string]::IsNullOrWhiteSpace($MetricSeedPath)) { "" } else { $MetricSeedPath }
    }
    notes = $Notes
}

$validation = Test-ParityDailySnapshot -Snapshot ([PSCustomObject]$payload)
if (-not $validation.valid) {
    $details = if ($validation.missingFields.Count -gt 0) { $validation.missingFields -join ", " } else { "invalid evidence level" }
    throw "[ParityDaily] Generated payload failed schema validation: $details"
}

$outDir = [System.IO.Path]::GetDirectoryName($OutputPath)
if (-not [string]::IsNullOrWhiteSpace($outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}
$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding UTF8

$markdownPath = [System.IO.Path]::ChangeExtension($OutputPath, ".md")
$lines = @()
$lines += "# Parity Daily Snapshot"
$lines += "Generated: $($payload.generatedAt)"
$lines += "Date: $Date"
$lines += "Evidence level: $EvidenceLevel"
$lines += ""
$lines += "## Metrics"
$lines += "- ttft_local_ms: $($payload.ttft_local_ms)"
$lines += "- ttft_network_ms: $($payload.ttft_network_ms)"
$lines += "- conversation_success_rate: $($payload.conversation_success_rate)"
$lines += "- helpfulness: $($payload.helpfulness)"
$lines += "- citation_precision: $($payload.citation_precision)"
$lines += "- citation_coverage: $($payload.citation_coverage)"
$lines += "- tool_correctness: $($payload.tool_correctness)"
$lines += "- security_incidents: $($payload.security_incidents)"
$lines += "- open_p0_p1: $($payload.open_p0_p1)"
$lines += ""
$lines += "## Linked evidence"
$lines += "- blind_human_eval_status: $($payload.blind_human_eval_status)"
$lines += "- real_model_eval_status: $($payload.real_model_eval_status)"
$lines += "- release_baseline_status: $($payload.release_baseline_status)"
$lines += "- blindHumanEvalReport: $BlindHumanEvalReportPath"
$lines += "- realModelEvalReport: $RealModelEvalReportPath"
$lines += "- releaseBaseline: $ReleaseBaselinePath"
if (-not [string]::IsNullOrWhiteSpace($Notes)) {
    $lines += ""
    $lines += "## Notes"
    $lines += "- $Notes"
}

Set-Content -Path $markdownPath -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[ParityDaily] Snapshot saved to $OutputPath"
