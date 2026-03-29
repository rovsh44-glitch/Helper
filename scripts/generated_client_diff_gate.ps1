param(
    [string]$OpenApiSnapshot = "doc/openapi_contract_snapshot.json",
    [string]$ClientPath = "services/generatedApiClient.ts"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $OpenApiSnapshot)) {
    throw "[ClientDiffGate] OpenAPI snapshot not found: $OpenApiSnapshot"
}

if (-not (Test-Path $ClientPath)) {
    throw "[ClientDiffGate] Generated client file not found: $ClientPath"
}

$openApi = Get-Content $OpenApiSnapshot -Raw | ConvertFrom-Json
$clientText = Get-Content $ClientPath -Raw

$requiredBindings = @(
    @{ Path = "/api/chat"; Method = "post"; ClientPattern = "chat\s*\(" },
    @{ Path = "/api/chat/stream"; Method = "post"; ClientPattern = "streamChat\s*\(" },
    @{ Path = "/api/chat/{conversationId}"; Method = "get"; ClientPattern = "getConversation\s*\(" },
    @{ Path = "/api/chat/{conversationId}/resume"; Method = "post"; ClientPattern = "resumeConversationTurn\s*\(" },
    @{ Path = "/api/chat/{conversationId}/stream/resume"; Method = "post"; ClientPattern = "resumeChatStream\s*\(" },
    @{ Path = "/api/chat/{conversationId}/turns/{turnId}/regenerate"; Method = "post"; ClientPattern = "regenerateTurn\s*\(" },
    @{ Path = "/api/chat/{conversationId}/branches"; Method = "post"; ClientPattern = "createBranch\s*\(" },
    @{ Path = "/api/chat/{conversationId}/branches/{branchId}/activate"; Method = "post"; ClientPattern = "activateBranch\s*\(" },
    @{ Path = "/api/chat/{conversationId}/branches/compare"; Method = "get"; ClientPattern = "compareBranches\s*\(" },
    @{ Path = "/api/chat/{conversationId}/branches/merge"; Method = "post"; ClientPattern = "mergeBranches\s*\(" },
    @{ Path = "/api/chat/{conversationId}/repair"; Method = "post"; ClientPattern = "repairConversation\s*\(" },
    @{ Path = "/api/chat/{conversationId}/feedback"; Method = "post"; ClientPattern = "submitFeedback\s*\(" },
    @{ Path = "/api/chat/{conversationId}/memory"; Method = "get"; ClientPattern = "getConversationMemory\s*\(" },
    @{ Path = "/api/chat/{conversationId}/memory/{memoryId}"; Method = "delete"; ClientPattern = "deleteConversationMemoryItem\s*\(" },
    @{ Path = "/api/chat/{conversationId}/preferences"; Method = "post"; ClientPattern = "setConversationPreferences\s*\(" },
    @{ Path = "/api/metrics"; Method = "get"; ClientPattern = "getMetrics\s*\(" },
    @{ Path = "/api/runtime/logs"; Method = "get"; ClientPattern = "getRuntimeLogs\s*\(" },
    @{ Path = "/api/openapi.json"; Method = "get"; ClientPattern = "getOpenApi\s*\(" }
)

$errors = @()
foreach ($binding in $requiredBindings) {
    $path = $binding.Path
    $method = $binding.Method
    $pattern = $binding.ClientPattern

    if (-not $openApi.paths.PSObject.Properties.Name.Contains($path)) {
        $errors += "OpenAPI path missing in snapshot: $path"
        continue
    }

    $pathNode = $openApi.paths.$path
    if (-not ($pathNode.PSObject.Properties.Name -contains $method)) {
        $errors += "OpenAPI method missing for path: $path [$method]"
    }

    if ($clientText -notmatch $pattern) {
        $errors += "Generated client binding missing for $path [$method], pattern: $pattern"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "[ClientDiffGate] Detected contract/client mismatch:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  - $_" }
    throw "[ClientDiffGate] Failed."
}

Write-Host "[ClientDiffGate] Passed. Generated client matches required OpenAPI bindings." -ForegroundColor Green
