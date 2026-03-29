param(
    [string]$RulesPath = "doc/monitoring/helper_alert_rules.yml",
    [string]$DashboardPath = "doc/monitoring/helper_dashboard.json",
    [string]$RoutingPath = "doc/monitoring/alert_routing.yml"
)

$ErrorActionPreference = "Stop"

foreach ($path in @($RulesPath, $DashboardPath, $RoutingPath)) {
    if (-not (Test-Path $path)) {
        throw "[MonitoringGate] Required monitoring artifact missing: $path"
    }
}

$rules = Get-Content $RulesPath -Raw
$routing = Get-Content $RoutingPath -Raw
$dashboard = Get-Content $DashboardPath -Raw | ConvertFrom-Json

$requiredRuleTokens = @(
    "HelperConversationSuccessRateLow",
    "HelperUserHelpfulnessLow",
    "HelperCitationCoverageLow",
    "HelperEndToEndTtftHigh",
    "HelperToolCorrectnessLow",
    "HelperToolCorrectnessLowBySourceChat"
)

foreach ($token in $requiredRuleTokens) {
    if ($rules -notmatch [Regex]::Escape($token)) {
        throw "[MonitoringGate] Missing alert rule token: $token"
    }
}

if ($routing -notmatch "helper-oncall-p1" -or $routing -notmatch "helper-team-p2") {
    throw "[MonitoringGate] Alert routing config does not include required receivers."
}

if (-not $dashboard.panels -or $dashboard.panels.Count -lt 4) {
    throw "[MonitoringGate] Dashboard must include at least 4 panels."
}

Write-Host "[MonitoringGate] Passed. Monitoring artifacts are present and structurally valid." -ForegroundColor Green
