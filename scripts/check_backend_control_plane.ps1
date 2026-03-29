param(
    [string]$ApiBaseUrl = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    $ApiBaseUrl = $env:HELPER_RUNTIME_SMOKE_API_BASE
}

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    Write-Host "[CI Gate] Control-plane gate skipped: HELPER_RUNTIME_SMOKE_API_BASE not set. See doc/config/ENV_REFERENCE.md."
    exit 0
}

$budgets = Get-Content (Join-Path $PSScriptRoot "performance_budgets.json") -Raw | ConvertFrom-Json
$headers = if (-not [string]::IsNullOrWhiteSpace($env:HELPER_API_KEY)) {
    @{ "X-API-KEY" = $env:HELPER_API_KEY }
} else {
    @{}
}

$controlPlane = Invoke-RestMethod -Method Get -Uri "$ApiBaseUrl/api/control-plane" -Headers $headers -TimeoutSec 10

if (-not [bool]$controlPlane.readiness.readyForChat) {
    throw "Control-plane gate failed: readiness.readyForChat is false."
}

if ($null -ne $controlPlane.readiness.timeToListeningMs -and [double]$controlPlane.readiness.timeToListeningMs -gt [double]$budgets.startup.listenMs) {
    throw "Listen budget exceeded: $($controlPlane.readiness.timeToListeningMs) ms > $($budgets.startup.listenMs) ms."
}

if ($null -ne $controlPlane.readiness.timeToReadyMs -and [double]$controlPlane.readiness.timeToReadyMs -gt [double]$budgets.startup.coldReadyMs) {
    throw "Cold readiness budget exceeded: $($controlPlane.readiness.timeToReadyMs) ms > $($budgets.startup.coldReadyMs) ms."
}

if ($controlPlane.readiness.warmupMode -ne "disabled" -and
    $null -ne $controlPlane.readiness.timeToWarmReadyMs -and
    [double]$controlPlane.readiness.timeToWarmReadyMs -gt [double]$budgets.startup.warmReadyMs) {
    throw "Warm readiness budget exceeded: $($controlPlane.readiness.timeToWarmReadyMs) ms > $($budgets.startup.warmReadyMs) ms."
}

if (-not [bool]$controlPlane.configuration.isValid) {
    $alerts = @($controlPlane.configuration.alerts) -join "; "
    throw "Configuration validation failed: $alerts"
}

if ([int]$controlPlane.auditQueue.pending -gt [int]$budgets.queues.auditBacklog) {
    throw "Audit backlog exceeded: $($controlPlane.auditQueue.pending) > $($budgets.queues.auditBacklog)."
}

$auditProcessed = [int]$controlPlane.auditQueue.processed
if ($auditProcessed -gt 0) {
    $auditFailureRate = [double]$controlPlane.auditQueue.failed / [double]$auditProcessed
    if ($auditFailureRate -gt [double]$budgets.queues.auditFailureRate) {
        throw "Audit failure rate exceeded: $auditFailureRate > $($budgets.queues.auditFailureRate)."
    }
}

if ([int]$controlPlane.persistenceQueue.pending -gt [int]$budgets.queues.persistenceBacklog) {
    throw "Persistence queue backlog exceeded: $($controlPlane.persistenceQueue.pending) > $($budgets.queues.persistenceBacklog)."
}

$pendingDirty = [int]$controlPlane.persistence.pendingDirtyConversations
if ($pendingDirty -gt 0 -and $null -ne $controlPlane.persistence.lastJournalWriteAtUtc) {
    $lastJournalWrite = [DateTimeOffset]$controlPlane.persistence.lastJournalWriteAtUtc
    $lagMs = [Math]::Max(0, [int](([DateTimeOffset]::UtcNow - $lastJournalWrite).TotalMilliseconds))
    if ($lagMs -gt [int]$budgets.queues.persistenceLagMs) {
        throw "Persistence lag exceeded: $lagMs ms > $($budgets.queues.persistenceLagMs) ms."
    }
}

Write-Host "[CI Gate] Control-plane thresholds passed."
