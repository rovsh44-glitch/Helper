param(
    [string]$DailyDir = "doc/parity_daily",
    [string]$ReportPath = "doc/HELPER_HUMAN_LEVEL_PARITY_CERTIFICATION_REPORT_2026-02-26.md",
    [string]$EvidenceLevel = "live_non_authoritative",
    [string]$BlindHumanEvalReportPath = "doc/archive/top_level_history/human_eval_parity_report_latest.md",
    [string]$RealModelEvalReportPath = "doc/archive/top_level_history/eval_real_model_report.md",
    [switch]$NoFailOnIncompleteWindow,
    [switch]$NoFailOnThresholds
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\ParityEvidenceCommon.ps1")

$allowedEvidenceLevels = @("sample", "synthetic", "dry_run", "live_non_authoritative", "authoritative")
if ($allowedEvidenceLevels -notcontains $EvidenceLevel) {
    throw "[Certify14d] Invalid EvidenceLevel '$EvidenceLevel'. Allowed values: $($allowedEvidenceLevels -join ", ")."
}

if (-not (Test-Path $DailyDir)) {
    New-Item -ItemType Directory -Path $DailyDir -Force | Out-Null
}

$dailyImport = Import-ParityDailySnapshots -DailyDir $DailyDir
$countedSnapshots = @($dailyImport.countedSnapshots | Sort-Object { $_.snapshot.date } -Descending | Select-Object -First 14)
$window = @($countedSnapshots | ForEach-Object { $_.snapshot })
$windowSorted = $window | Sort-Object date
$hasEnoughData = $window.Count -ge 14

function Test-Threshold {
    param(
        [double]$Value,
        [double]$Threshold,
        [string]$Kind
    )

    if ($Kind -eq "max") {
        return $Value -le $Threshold
    }

    return $Value -ge $Threshold
}

$thresholds = @(
    @{ Name = "ttft_local_ms"; Threshold = 1200; Kind = "max" },
    @{ Name = "ttft_network_ms"; Threshold = 2000; Kind = "max" },
    @{ Name = "conversation_success_rate"; Threshold = 0.85; Kind = "min" },
    @{ Name = "helpfulness"; Threshold = 4.3; Kind = "min" },
    @{ Name = "citation_precision"; Threshold = 0.85; Kind = "min" },
    @{ Name = "citation_coverage"; Threshold = 0.70; Kind = "min" },
    @{ Name = "tool_correctness"; Threshold = 0.90; Kind = "min" },
    @{ Name = "security_incidents"; Threshold = 0; Kind = "max" },
    @{ Name = "open_p0_p1"; Threshold = 0; Kind = "max" }
)

$summaryChecks = @()
foreach ($threshold in $thresholds) {
    $status = "NO_DATA"
    if ($hasEnoughData) {
        $allPass = $true
        foreach ($day in $window) {
            if (-not (Test-Threshold -Value ([double]$day.($threshold.Name)) -Threshold ([double]$threshold.Threshold) -Kind $threshold.Kind)) {
                $allPass = $false
            }
        }

        $status = if ($allPass) { "PASS" } else { "FAIL" }
    }

    $summaryChecks += [PSCustomObject]@{
        metric = $threshold.Name
        threshold = $threshold.Threshold
        status = $status
    }
}

$failedChecks = @($summaryChecks | Where-Object { $_.status -eq "FAIL" })
$blindHumanEval = Get-BlindHumanEvalReportMetadata -Path $BlindHumanEvalReportPath
$realModelEval = Get-RealModelEvalReportMetadata -Path $RealModelEvalReportPath
$blindEvidenceOk = $blindHumanEval.authoritative
$realModelEvidenceOk = $realModelEval.authoritative
$authoritativeSourceStatus = if ($blindEvidenceOk -and $realModelEvidenceOk) { "COMPLETE" } else { "INCOMPLETE" }

$noGoReasons = New-Object System.Collections.Generic.List[string]
if ($dailyImport.invalidSnapshots.Count -gt 0) {
    $noGoReasons.Add("invalid_daily_snapshots=" + $dailyImport.invalidSnapshots.Count)
}
if (-not $hasEnoughData) {
    $noGoReasons.Add("missing_counted_days=" + $window.Count)
}
if (-not $blindEvidenceOk) {
    $noGoReasons.Add("blind_human_eval_non_authoritative=" + $blindHumanEval.integritySufficiency)
}
if (-not $realModelEvidenceOk) {
    $reason = if ([string]::IsNullOrWhiteSpace([string]$realModelEval.nonAuthoritativeReasons)) { "non_authoritative" } else { [string]$realModelEval.nonAuthoritativeReasons }
    $noGoReasons.Add("real_model_eval_non_authoritative=" + $reason)
}
if ($hasEnoughData -and $failedChecks.Count -gt 0) {
    $noGoReasons.Add("threshold_failures=" + (($failedChecks | Select-Object -ExpandProperty metric) -join ","))
}

$evidenceCompletenessStatus = if ($hasEnoughData -and $blindEvidenceOk -and $realModelEvidenceOk) { "COMPLETE" } else { "INCOMPLETE" }
$allCertified = ($noGoReasons.Count -eq 0)
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"

$lines = @()
$lines += "# HELPER Human-Level Parity Certification Report"
$lines += "Generated: $timestamp"
$lines += "Daily source: $DailyDir"
$lines += "Evidence level: $EvidenceLevel"
$lines += "Status: $(if ($allCertified) { "PASS" } else { "FAIL" })"
$lines += "Evidence completeness status: $evidenceCompletenessStatus"
$lines += "Authoritative source status: $authoritativeSourceStatus"
$lines += "Authoritative evidence: $(if (($EvidenceLevel -eq "authoritative") -and $allCertified) { "YES" } else { "NO" })"
$lines += ""
$lines += "Data points in counted window: $($window.Count)"
$lines += "Valid snapshots in directory: $($dailyImport.validSnapshots.Count)"
$lines += "Invalid snapshots in directory: $($dailyImport.invalidSnapshots.Count)"
$lines += "Certification window complete (14 days): $(if ($hasEnoughData) { "YES" } else { "NO" })"
if ($windowSorted.Count -gt 0) {
    $lines += "Window range: $($windowSorted[0].date) .. $($windowSorted[$windowSorted.Count - 1].date)"
}
$lines += "Linked blind human-eval status: $(if ($blindHumanEval.authoritative) { "AUTHORITATIVE" } else { $blindHumanEval.integritySufficiency })"
$lines += "Linked real-model eval status: $(if ($realModelEval.authoritative) { "AUTHORITATIVE" } else { $realModelEval.authoritativeGateStatus })"
$lines += "NO-GO reasons: $(if ($noGoReasons.Count -eq 0) { "none" } else { ($noGoReasons -join "; ") })"
$lines += ""
$lines += "| Metric | Threshold | Status |"
$lines += "|---|---:|---|"
foreach ($check in $summaryChecks) {
    $lines += "| $($check.metric) | $($check.threshold) | $($check.status) |"
}

$lines += ""
$lines += "## Linked evidence"
$lines += "- Blind human-eval report: $BlindHumanEvalReportPath"
$lines += "- Blind human-eval authoritative: $(if ($blindHumanEval.authoritative) { "YES" } else { "NO" })"
$lines += "- Blind human-eval integrity sufficiency: $($blindHumanEval.integritySufficiency)"
$lines += "- Real-model eval report: $RealModelEvalReportPath"
$lines += "- Real-model eval authoritative: $(if ($realModelEval.authoritative) { "YES" } else { "NO" })"
$lines += "- Real-model eval non-authoritative reasons: $($realModelEval.nonAuthoritativeReasons)"

$lines += ""
$lines += "## Daily values (latest counted 14)"
$lines += "| Date | Evidence level | ttft_local_ms | ttft_network_ms | success | helpfulness | citation_precision | citation_coverage | tool_correctness | security_incidents | open_p0_p1 |"
$lines += "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|"
foreach ($day in $windowSorted) {
    $lines += "| $($day.date) | $($day.evidenceLevel) | $($day.ttft_local_ms) | $($day.ttft_network_ms) | $($day.conversation_success_rate) | $($day.helpfulness) | $($day.citation_precision) | $($day.citation_coverage) | $($day.tool_correctness) | $($day.security_incidents) | $($day.open_p0_p1) |"
}

if ($dailyImport.invalidSnapshots.Count -gt 0) {
    $lines += ""
    $lines += "## Invalid snapshots"
    foreach ($invalid in $dailyImport.invalidSnapshots) {
        $lines += "- $($invalid.path): $($invalid.reason) $($invalid.details)"
    }
}

$lines += ""
$lines += "Go/No-Go: $(if ($allCertified) { "GO" } else { "NO-GO" })"

$reportDir = [System.IO.Path]::GetDirectoryName($ReportPath)
if (-not [string]::IsNullOrWhiteSpace($reportDir)) {
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
}
Set-Content -Path $ReportPath -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[Certify14d] Report saved to $ReportPath"

if ((-not $hasEnoughData) -and (-not $NoFailOnIncompleteWindow.IsPresent)) {
    throw "[Certify14d] Certification window incomplete: need 14 counted days, got $($window.Count)."
}

if (($noGoReasons.Count -gt 0) -and (-not $NoFailOnThresholds.IsPresent)) {
    throw "[Certify14d] Certification failed: $($noGoReasons -join "; ")."
}

if ($allCertified) {
    Write-Host "[Certify14d] Certified. All parity requirements passed." -ForegroundColor Green
}
else {
    Write-Host "[Certify14d] Completed with NO-GO status." -ForegroundColor Yellow
}
