param(
    [string]$BlindHumanEvalReportPath = "doc/archive/top_level_history/human_eval_parity_report_latest.md",
    [string]$IntegrityReportPath = "doc/human_eval_parity_report_latest.integrity.json",
    [string]$RealModelEvalReportPath = "doc/archive/top_level_history/eval_real_model_report.md",
    [string]$CertificationReportPath = "doc/HELPER_HUMAN_LEVEL_PARITY_CERTIFICATION_REPORT_2026-02-26.md",
    [string]$DailyDir = "doc/parity_daily",
    [string]$OutputJsonPath = "doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_BUNDLE.json",
    [string]$OutputMarkdownPath = "doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_BUNDLE.md"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\ParityEvidenceCommon.ps1")

$blindHumanEval = Get-BlindHumanEvalReportMetadata -Path $BlindHumanEvalReportPath
$realModelEval = Get-RealModelEvalReportMetadata -Path $RealModelEvalReportPath
$certification = Get-ParityCertificationReportMetadata -Path $CertificationReportPath
$dailyImport = Import-ParityDailySnapshots -DailyDir $DailyDir
$latestCountedSnapshot = $dailyImport.countedSnapshots | Sort-Object { $_.snapshot.date } -Descending | Select-Object -First 1
$latestAnySnapshot = $dailyImport.validSnapshots | Sort-Object { $_.snapshot.date } -Descending | Select-Object -First 1
$integrityExists = Test-Path $IntegrityReportPath

$blockingReasons = New-Object System.Collections.Generic.List[string]
if (-not $blindHumanEval.authoritative) {
    $blockingReasons.Add("blind_human_eval_non_authoritative")
}
if (-not $integrityExists) {
    $blockingReasons.Add("integrity_report_missing")
}
if (-not $realModelEval.authoritative) {
    $blockingReasons.Add("real_model_eval_non_authoritative")
}
if (-not $certification.windowComplete) {
    $blockingReasons.Add("certification_window_incomplete")
}
if (-not $certification.go) {
    $blockingReasons.Add("certification_no_go")
}
if ($null -eq $latestCountedSnapshot) {
    $blockingReasons.Add("no_counted_daily_snapshot")
}
elseif ([int]$latestCountedSnapshot.snapshot.open_p0_p1 -ne 0) {
    $blockingReasons.Add("latest_counted_snapshot_open_p0_p1_nonzero")
}
elseif (-not [string]::Equals([string]$latestCountedSnapshot.snapshot.release_baseline_status, "PASS", [System.StringComparison]::OrdinalIgnoreCase)) {
    $blockingReasons.Add("latest_counted_snapshot_release_baseline_not_pass")
}

$claimEligible = $blockingReasons.Count -eq 0
$status = if ($claimEligible) { "COMPLETE" } else { "INCOMPLETE" }

$payload = [ordered]@{
    generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    status = $status
    claimEligible = $claimEligible
    blindHumanEval = $blindHumanEval
    integrityReport = [ordered]@{
        path = $IntegrityReportPath
        exists = $integrityExists
    }
    realModelEval = $realModelEval
    certification = $certification
    dailyCoverage = [ordered]@{
        validSnapshots = $dailyImport.validSnapshots.Count
        countedSnapshots = $dailyImport.countedSnapshots.Count
        invalidSnapshots = $dailyImport.invalidSnapshots.Count
        latestCountedSnapshotPath = if ($null -ne $latestCountedSnapshot) { [string]$latestCountedSnapshot.path } else { "" }
        latestCountedSnapshotDate = if ($null -ne $latestCountedSnapshot) { [string]$latestCountedSnapshot.snapshot.date } else { "" }
        latestCountedSnapshotOpenP0P1 = if ($null -ne $latestCountedSnapshot) { [int]$latestCountedSnapshot.snapshot.open_p0_p1 } else { -1 }
        latestCountedSnapshotReleaseBaselineStatus = if ($null -ne $latestCountedSnapshot) { [string]$latestCountedSnapshot.snapshot.release_baseline_status } else { "" }
        latestAnySnapshotPath = if ($null -ne $latestAnySnapshot) { [string]$latestAnySnapshot.path } else { "" }
        latestAnySnapshotDate = if ($null -ne $latestAnySnapshot) { [string]$latestAnySnapshot.snapshot.date } else { "" }
    }
    blockingReasons = @($blockingReasons)
}

$jsonDir = [System.IO.Path]::GetDirectoryName($OutputJsonPath)
if (-not [string]::IsNullOrWhiteSpace($jsonDir)) {
    New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null
}
$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputJsonPath -Encoding UTF8

$mdDir = [System.IO.Path]::GetDirectoryName($OutputMarkdownPath)
if (-not [string]::IsNullOrWhiteSpace($mdDir)) {
    New-Item -ItemType Directory -Force -Path $mdDir | Out-Null
}

$lines = @()
$lines += "# Current Parity Evidence Bundle"
$lines += "Generated: $($payload.generated)"
$lines += "Status: $status"
$lines += "Claim eligible: $(if ($claimEligible) { "YES" } else { "NO" })"
$lines += "Blocking reasons: $(if ($blockingReasons.Count -eq 0) { "none" } else { $blockingReasons -join "; " })"
$lines += ""
$lines += "## Blind human-eval"
$lines += "- Report: $BlindHumanEvalReportPath"
$lines += "- Evidence level: $($blindHumanEval.evidenceLevel)"
$lines += "- Authoritative: $(if ($blindHumanEval.authoritative) { "YES" } else { "NO" })"
$lines += "- Integrity sufficiency: $($blindHumanEval.integritySufficiency)"
$lines += "- Provenance status: $($blindHumanEval.provenanceStatus)"
$lines += "- Reviewer diversity status: $($blindHumanEval.reviewerDiversityStatus)"
$lines += "- Integrity report: $IntegrityReportPath"
$lines += "- Integrity report exists: $(if ($integrityExists) { "YES" } else { "NO" })"
$lines += ""
$lines += "## Real-model eval"
$lines += "- Report: $RealModelEvalReportPath"
$lines += "- Mode: $($realModelEval.mode)"
$lines += "- Requested evidence level: $($realModelEval.requestedEvidenceLevel)"
$lines += "- Effective evidence level: $($realModelEval.evidenceLevel)"
$lines += "- Authoritative gate status: $($realModelEval.authoritativeGateStatus)"
$lines += "- Authoritative: $(if ($realModelEval.authoritative) { "YES" } else { "NO" })"
$lines += "- Non-authoritative reasons: $($realModelEval.nonAuthoritativeReasons)"
$lines += ""
$lines += "## Certification"
$lines += "- Report: $CertificationReportPath"
$lines += "- Window complete: $(if ($certification.windowComplete) { "YES" } else { "NO" })"
$lines += "- Go/No-Go: $(if ($certification.go) { "GO" } else { "NO-GO" })"
$lines += "- Evidence completeness status: $($certification.evidenceCompletenessStatus)"
$lines += "- Authoritative source status: $($certification.authoritativeSourceStatus)"
$lines += "- NO-GO reasons: $($certification.noGoReasons)"
$lines += ""
$lines += "## Daily coverage"
$lines += "- Valid snapshots: $($dailyImport.validSnapshots.Count)"
$lines += "- Counted snapshots: $($dailyImport.countedSnapshots.Count)"
$lines += "- Invalid snapshots: $($dailyImport.invalidSnapshots.Count)"
$lines += "- Latest counted snapshot: $(if ($null -ne $latestCountedSnapshot) { [string]$latestCountedSnapshot.path } else { "missing" })"
$lines += "- Latest counted snapshot open P0/P1: $(if ($null -ne $latestCountedSnapshot) { [int]$latestCountedSnapshot.snapshot.open_p0_p1 } else { "missing" })"
$lines += "- Latest counted snapshot release baseline status: $(if ($null -ne $latestCountedSnapshot) { [string]$latestCountedSnapshot.snapshot.release_baseline_status } else { "missing" })"
$lines += "- Latest any snapshot: $(if ($null -ne $latestAnySnapshot) { [string]$latestAnySnapshot.path } else { "missing" })"

Set-Content -Path $OutputMarkdownPath -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[ParityBundle] Bundle saved to $OutputJsonPath"
