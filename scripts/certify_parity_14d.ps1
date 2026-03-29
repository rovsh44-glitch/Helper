param(
    [string]$DailyDir = "doc/parity_daily",
    [string]$ReportPath = "doc/HELPER_HUMAN_LEVEL_PARITY_CERTIFICATION_REPORT_2026-02-26.md",
    [string]$EvidenceLevel = "live_non_authoritative",
    [switch]$NoFailOnIncompleteWindow,
    [switch]$NoFailOnThresholds
)

$ErrorActionPreference = "Stop"
$forwardedParameters = @{}
foreach ($entry in $PSBoundParameters.GetEnumerator()) {
    $forwardedParameters[$entry.Key] = $entry.Value
}

& (Join-Path $PSScriptRoot "certify_parity_14d_v2.ps1") @forwardedParameters
