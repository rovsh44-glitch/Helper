param(
    [string]$CurrentStatePath = "doc/CURRENT_STATE.md",
    [string]$HumanEvalReportPath = "doc/archive/top_level_history/human_eval_parity_report_latest.md",
    [string]$RealModelReportPath = "doc/archive/top_level_history/eval_real_model_report.md",
    [string]$CertificationReportPath = "doc/HELPER_HUMAN_LEVEL_PARITY_CERTIFICATION_REPORT_2026-02-26.md",
    [string]$BundleJsonPath = "doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_BUNDLE.json",
    [string]$DailyDir = "doc/parity_daily",
    [string]$OutputJsonPath = "doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_STATE.json",
    [string]$OutputMarkdownPath = "doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_STATE.md"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\ParityEvidenceCommon.ps1")

$humanEval = Get-BlindHumanEvalReportMetadata -Path $HumanEvalReportPath
$realModel = Get-RealModelEvalReportMetadata -Path $RealModelReportPath
$certification = Get-ParityCertificationReportMetadata -Path $CertificationReportPath
$bundle = Get-ParityEvidenceBundleMetadata -Path $BundleJsonPath
$dailyImport = Import-ParityDailySnapshots -DailyDir $DailyDir
$latestCountedSnapshot = $dailyImport.countedSnapshots | Sort-Object { $_.snapshot.date } -Descending | Select-Object -First 1
$latestAnySnapshot = $dailyImport.validSnapshots | Sort-Object { $_.snapshot.date } -Descending | Select-Object -First 1

$status = "NOT_STARTED"
$details = "No parity evidence artifacts detected."

$hasAnyArtifact = $humanEval.exists -or $realModel.exists -or $certification.exists -or $bundle.exists -or ($dailyImport.validSnapshots.Count -gt 0) -or ($dailyImport.invalidSnapshots.Count -gt 0)
if ($hasAnyArtifact) {
    $status = "INSUFFICIENT_EVIDENCE"
    $details = "Parity evidence artifacts exist, but counted 14-day coverage has not started."
}

if ($dailyImport.countedSnapshots.Count -gt 0) {
    $status = "COLLECTING_EVIDENCE"
    $details = "Counted parity daily snapshots are being collected, but the 14-day window is incomplete."
}

if ($certification.windowComplete) {
    if ($certification.go) {
        $status = "CERTIFICATION_IN_PROGRESS"
        $details = "The 14-day certification window is complete, but the canonical parity bundle is not yet claim-eligible."
    }
    else {
        $status = "PARITY_NOT_PROVEN"
        $details = "The 14-day certification window completed with a NO-GO decision."
    }
}

if ($bundle.claimEligible) {
    $status = "PARITY_PROVEN"
    $details = "Canonical parity evidence bundle is complete and claim-eligible."
}

$payload = [ordered]@{
    generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    status = $status
    details = $details
    blindHumanEval = $humanEval
    realModelEval = $realModel
    certification = $certification
    dailyCoverage = [ordered]@{
        validSnapshots = $dailyImport.validSnapshots.Count
        countedSnapshots = $dailyImport.countedSnapshots.Count
        invalidSnapshots = $dailyImport.invalidSnapshots.Count
        latestCountedSnapshotPath = if ($null -ne $latestCountedSnapshot) { [string]$latestCountedSnapshot.path } else { "" }
        latestCountedSnapshotDate = if ($null -ne $latestCountedSnapshot) { [string]$latestCountedSnapshot.snapshot.date } else { "" }
        latestAnySnapshotPath = if ($null -ne $latestAnySnapshot) { [string]$latestAnySnapshot.path } else { "" }
        latestAnySnapshotDate = if ($null -ne $latestAnySnapshot) { [string]$latestAnySnapshot.snapshot.date } else { "" }
    }
    bundle = $bundle
}

$jsonDir = [System.IO.Path]::GetDirectoryName($OutputJsonPath)
if (-not [string]::IsNullOrWhiteSpace($jsonDir)) {
    New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null
}
$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputJsonPath -Encoding UTF8

$lines = @()
$lines += "# Current Parity Evidence State"
$lines += "Generated: $($payload.generated)"
$lines += "Status: $status"
$lines += "Details: $details"
$lines += ""
$lines += "## Topline"
$lines += "- Blind human-eval report: $(if ($humanEval.exists) { $humanEval.path } else { "missing" })"
$lines += "- Blind human-eval level: $($humanEval.evidenceLevel)"
$lines += "- Blind human-eval authoritative: $(if ($humanEval.authoritative) { "YES" } else { "NO" })"
$lines += "- Blind human-eval format status: $($humanEval.formatStatus)"
$lines += "- Blind human-eval provenance status: $($humanEval.provenanceStatus)"
$lines += "- Blind human-eval collection status: $($humanEval.blindCollectionStatus)"
$lines += "- Blind human-eval reviewer diversity status: $($humanEval.reviewerDiversityStatus)"
$lines += "- Blind human-eval integrity status: $($humanEval.integrityStatus)"
$lines += "- Blind human-eval integrity sufficiency: $($humanEval.integritySufficiency)"
$lines += "- Real-model eval report: $(if ($realModel.exists) { $realModel.path } else { "missing" })"
$lines += "- Real-model eval mode: $($realModel.mode)"
$lines += "- Real-model requested evidence level: $($realModel.requestedEvidenceLevel)"
$lines += "- Real-model eval level: $($realModel.evidenceLevel)"
$lines += "- Real-model authoritative gate status: $($realModel.authoritativeGateStatus)"
$lines += "- Real-model eval authoritative: $(if ($realModel.authoritative) { "YES" } else { "NO" })"
$lines += "- Real-model non-authoritative reasons: $($realModel.nonAuthoritativeReasons)"
$lines += "- Real-model traceability status: $($realModel.traceabilityStatus)"
$lines += "- Certification report: $(if ($certification.exists) { $certification.path } else { "missing" })"
$lines += "- 14-day window complete: $(if ($certification.windowComplete) { "YES" } else { "NO" })"
$lines += "- 14-day certification go/no-go: $(if ($certification.go) { "GO" } else { "NO-GO or unavailable" })"
$lines += "- 14-day authoritative source status: $($certification.authoritativeSourceStatus)"
$lines += "- 14-day NO-GO reasons: $($certification.noGoReasons)"
$lines += "- Daily valid snapshots: $($dailyImport.validSnapshots.Count)"
$lines += "- Daily counted snapshots: $($dailyImport.countedSnapshots.Count)"
$lines += "- Daily invalid snapshots: $($dailyImport.invalidSnapshots.Count)"
$lines += "- Latest counted snapshot: $(if ($null -ne $latestCountedSnapshot) { [string]$latestCountedSnapshot.path } else { "missing" })"
$lines += "- Latest any snapshot: $(if ($null -ne $latestAnySnapshot) { [string]$latestAnySnapshot.path } else { "missing" })"
$lines += "- Canonical parity evidence bundle: $(if ($bundle.exists) { $bundle.path } else { "missing" })"
$lines += "- Canonical bundle status: $($bundle.status)"
$lines += "- Canonical bundle claim eligible: $(if ($bundle.claimEligible) { "YES" } else { "NO" })"

Set-Content -Path $OutputMarkdownPath -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[ParityEvidence] Snapshot refreshed: $OutputJsonPath"
