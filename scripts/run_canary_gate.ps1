param(
    [string]$ApiBase = "http://localhost:5000",
    [int]$TrafficPercent = 5,
    [string]$OutputReport = "doc/canary_gate_report.md",
    [string]$MetricsJsonPath = "",
    [switch]$NoFailOnGate
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($MetricsJsonPath)) {
    $metricsUrl = "$ApiBase/api/metrics"
    Write-Host "[CanaryGate] Fetching metrics from $metricsUrl"
    try {
        $metrics = Invoke-RestMethod -Uri $metricsUrl -Method Get
    }
    catch {
        throw "[CanaryGate] Failed to fetch metrics from API. Start Helper API or pass -MetricsJsonPath."
    }
}
else {
    if (-not (Test-Path $MetricsJsonPath)) {
        throw "[CanaryGate] Metrics JSON not found: $MetricsJsonPath"
    }

    Write-Host "[CanaryGate] Loading metrics from file: $MetricsJsonPath"
    $metrics = Get-Content $MetricsJsonPath -Raw | ConvertFrom-Json
}

$conversation = $metrics.conversations
$helpfulness = $metrics.helpfulness
$tools = $metrics.tools

$successRate = if ($null -ne $conversation.successRate) { [double]$conversation.successRate } else { [double]$conversation.conversationSuccessRate }
$helpfulnessAverage = if ($null -ne $helpfulness.averageRating) { [double]$helpfulness.averageRating } else { [double]$helpfulness.ratingAverage }
$citationCoverage = if ($null -ne $conversation.citationCoverage) { [double]$conversation.citationCoverage } else { [double]$conversation.citationsCoverage }
$toolSuccessRatio = if ($null -ne $tools.successRatio) { [double]$tools.successRatio } else { [double]$tools.correctness }
$alertsCount = @($conversation.alerts).Count + @($helpfulness.alerts).Count + @($tools.alerts).Count

$checks = @(
    @{ Name = "Conversation success rate >= 0.85"; Passed = ($successRate -ge 0.85); Value = $successRate },
    @{ Name = "Helpfulness average >= 4.3"; Passed = ($helpfulnessAverage -ge 4.3); Value = $helpfulnessAverage },
    @{ Name = "Citation coverage >= 0.70"; Passed = ($citationCoverage -ge 0.70); Value = $citationCoverage },
    @{ Name = "Tool success ratio >= 0.90"; Passed = ($toolSuccessRatio -ge 0.90); Value = $toolSuccessRatio },
    @{ Name = "No alerts in metrics"; Passed = ($alertsCount -eq 0); Value = $alertsCount }
)

$allPassed = $true
foreach ($check in $checks) {
    if (-not $check.Passed) {
        $allPassed = $false
    }
}

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
$lines = @()
$lines += "# Canary KPI Gate Report"
$lines += "Generated: $timestamp"
$lines += "Target traffic stage: $TrafficPercent%"
$lines += ""
$lines += "| Check | Value | Status |"
$lines += "|---|---:|---|"
foreach ($check in $checks) {
    $status = if ($check.Passed) { "PASS" } else { "FAIL" }
    $lines += "| $($check.Name) | $([math]::Round([double]$check.Value, 4)) | $status |"
}
$lines += ""
$lines += "Decision: $(if ($allPassed) { "Promote to next canary stage" } else { "Hold / rollback" })"
$lines += "Source: $(if ([string]::IsNullOrWhiteSpace($MetricsJsonPath)) { $metricsUrl } else { $MetricsJsonPath })"

Set-Content -Path $OutputReport -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[CanaryGate] Report saved to $OutputReport"

if ((-not $allPassed) -and (-not $NoFailOnGate.IsPresent)) {
    throw "[CanaryGate] KPI gate failed. Hold traffic promotion."
}

if ($allPassed) {
    Write-Host "[CanaryGate] Passed. Canary stage can be promoted." -ForegroundColor Green
}
else {
    Write-Host "[CanaryGate] Completed with gate failure (NoFailOnGate enabled)." -ForegroundColor Yellow
}
