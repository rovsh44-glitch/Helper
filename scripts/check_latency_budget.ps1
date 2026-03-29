param(
    [string]$ApiBaseUrl = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    $ApiBaseUrl = $env:HELPER_RUNTIME_SMOKE_API_BASE
}

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    Write-Host "[CI Gate] Latency budget skipped: HELPER_RUNTIME_SMOKE_API_BASE not set. See doc/config/ENV_REFERENCE.md."
    exit 0
}

$budgets = Get-Content (Join-Path $PSScriptRoot "performance_budgets.json") -Raw | ConvertFrom-Json
$headers = if (-not [string]::IsNullOrWhiteSpace($env:HELPER_API_KEY)) {
    @{ "X-API-KEY" = $env:HELPER_API_KEY }
} else {
    @{}
}

$metrics = Invoke-RestMethod -Method Get -Uri "$ApiBaseUrl/api/metrics" -Headers $headers -TimeoutSec 10
$readiness = Invoke-RestMethod -Method Get -Uri "$ApiBaseUrl/api/readiness" -TimeoutSec 10

if (-not [bool]$readiness.readyForChat) {
    throw "Readiness gate failed before latency check. Phase=$($readiness.phase)"
}

if ($metrics.conversations.avgFirstTokenLatencyMs -gt [double]$budgets.conversation.avgFirstTokenMs) {
    throw "Avg first-token latency budget exceeded: $($metrics.conversations.avgFirstTokenLatencyMs) ms > $($budgets.conversation.avgFirstTokenMs) ms."
}

if ($metrics.conversations.avgFullResponseLatencyMs -gt [double]$budgets.conversation.avgFullTurnMs) {
    throw "Avg full-turn latency budget exceeded: $($metrics.conversations.avgFullResponseLatencyMs) ms > $($budgets.conversation.avgFullTurnMs) ms."
}

if ($metrics.conversations.budgetExceededRate -gt [double]$budgets.conversation.budgetExceededRate) {
    throw "Budget exceeded rate is above threshold: $($metrics.conversations.budgetExceededRate) > $($budgets.conversation.budgetExceededRate)."
}

$stageBudgetMap = @{
    classify = [double]$budgets.conversationStages.classifyP95Ms
    plan = [double]$budgets.conversationStages.planP95Ms
    execute = [double]$budgets.conversationStages.executeP95Ms
    critic = [double]$budgets.conversationStages.criticP95Ms
    finalizer = [double]$budgets.conversationStages.finalizerP95Ms
    persistence = [double]$budgets.conversationStages.persistenceP95Ms
    audit_enqueue = [double]$budgets.conversationStages.auditEnqueueP95Ms
    audit_process = [double]$budgets.conversationStages.auditProcessP95Ms
}

foreach ($stage in @($metrics.conversationStages.stages)) {
    $name = [string]$stage.stage
    if (-not $stageBudgetMap.ContainsKey($name)) {
        continue
    }

    if ([double]$stage.p95LatencyMs -gt $stageBudgetMap[$name]) {
        throw "Stage latency budget exceeded for '$name': $($stage.p95LatencyMs) ms > $($stageBudgetMap[$name]) ms."
    }
}

Write-Host "[CI Gate] Latency budgets passed."
