param(
    [string]$ApiBaseUrl = "",
    [string]$UiUrl = ""
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\RuntimeSmokeCommon.ps1")

$smokePromptToken = "__HELPER_SMOKE_READY__"
$longSmokePromptToken = "__HELPER_SMOKE_LONG_STREAM__"

Write-Host "[UI Perf] Checking bundle budget..."
powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check_bundle_budget.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "Bundle budget gate failed."
}

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    $ApiBaseUrl = $env:HELPER_RUNTIME_SMOKE_API_BASE
}

if ([string]::IsNullOrWhiteSpace($UiUrl)) {
    $UiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
}

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    Write-Host "[UI Perf] Runtime scenarios skipped: HELPER_RUNTIME_SMOKE_API_BASE not set. See doc/config/ENV_REFERENCE.md."
    exit 0
}

Write-Host "[UI Perf] Scenario: runtime smoke..."
$smokeArgs = @("-ExecutionPolicy", "Bypass", "-File", (Join-Path $PSScriptRoot "run_runtime_smoke.ps1"), "-ApiBaseUrl", $ApiBaseUrl)
if (-not [string]::IsNullOrWhiteSpace($UiUrl)) {
    $smokeArgs += @("-UiUrl", $UiUrl)
}
& powershell @smokeArgs
if ($LASTEXITCODE -ne 0) {
    throw "Runtime smoke scenario failed."
}

Write-Host "[UI Perf] Scenario: restore snapshot..."
$headers = New-SessionHeaders -ApiBase $ApiBaseUrl -Surface "conversation" -RequestedScopes @("chat:read", "chat:write")
$chatBody = @{
    message = $smokePromptToken
    maxHistory = 4
    systemInstruction = "deterministic_smoke"
} | ConvertTo-Json -Depth 4
$chatResponse = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/chat" -Headers $headers -ContentType "application/json" -Body $chatBody -TimeoutSec 30
Assert-Condition (-not [string]::IsNullOrWhiteSpace($chatResponse.conversationId)) "Deterministic chat did not return conversationId."

$restoreStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$snapshot = Invoke-RestMethod -Method Get -Uri "$ApiBaseUrl/api/chat/$($chatResponse.conversationId)" -Headers $headers -TimeoutSec 30
$restoreStopwatch.Stop()
Assert-Condition ($snapshot.messages.Count -ge 2) "Restore scenario returned incomplete conversation snapshot."
Assert-Condition ($restoreStopwatch.ElapsedMilliseconds -le 2000) "Restore scenario exceeded regression budget: $($restoreStopwatch.ElapsedMilliseconds) ms > 2000 ms."

$httpClient = [System.Net.Http.HttpClient]::new()

Write-Host "[UI Perf] Scenario: public deterministic long stream..."
$descriptor = Invoke-RestMethod -Method Get -Uri "$ApiBaseUrl/api/smoke/long" -TimeoutSec 30
$publicLongRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, "$ApiBaseUrl/api/smoke/stream/long")
$publicLongRequest.Headers.Accept.Add([System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new("text/event-stream"))
$publicLongStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$publicLongStream = Read-SseDoneChunk -Client $httpClient -Request $publicLongRequest
$publicLongStopwatch.Stop()
Assert-Condition ($publicLongStream.TokenCount -eq [int]$descriptor.chunkCount) "Public long stream token count mismatch."
Assert-Condition ($publicLongStream.Done.fullResponse.Length -eq [int]$descriptor.responseLength) "Public long stream response length mismatch."
Assert-Condition ($publicLongStopwatch.ElapsedMilliseconds -le 10000) "Public long stream exceeded regression budget: $($publicLongStopwatch.ElapsedMilliseconds) ms > 10000 ms."

Write-Host "[UI Perf] Scenario: authenticated deterministic long stream..."
$chatStreamRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$ApiBaseUrl/api/chat/stream")
$chatStreamRequest.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $headers.Authorization.Substring("Bearer ".Length))
$chatStreamRequest.Headers.Accept.Add([System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new("text/event-stream"))
$chatPayload = @{
    message = $longSmokePromptToken
    maxHistory = 4
    systemInstruction = "deterministic_smoke_long"
} | ConvertTo-Json -Depth 4
$chatStreamRequest.Content = [System.Net.Http.StringContent]::new($chatPayload, [System.Text.Encoding]::UTF8, "application/json")
$chatLongStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$chatLongStream = Read-SseDoneChunk -Client $httpClient -Request $chatStreamRequest
$chatLongStopwatch.Stop()
Assert-Condition ($chatLongStream.TokenCount -eq [int]$descriptor.chunkCount) "Authenticated long stream token count mismatch."
Assert-Condition ($chatLongStream.Done.fullResponse.Length -eq [int]$descriptor.responseLength) "Authenticated long stream response length mismatch."
Assert-Condition ($chatLongStopwatch.ElapsedMilliseconds -le 10000) "Authenticated long stream exceeded regression budget: $($chatLongStopwatch.ElapsedMilliseconds) ms > 10000 ms."

Write-Host "[UI Perf] Checking latency budget..."
powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check_latency_budget.ps1") -ApiBaseUrl $ApiBaseUrl
if ($LASTEXITCODE -ne 0) {
    throw "Latency budget gate failed."
}

Write-Host "[UI Perf] Regression checks passed."
