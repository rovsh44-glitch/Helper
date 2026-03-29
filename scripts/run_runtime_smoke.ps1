param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [string]$UiUrl = "",
    [int]$TimeoutSec = 30
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\RuntimeSmokeCommon.ps1")

$smokePromptToken = "__HELPER_SMOKE_READY__"

Write-Host "[RuntimeSmoke] Checking readiness..."
$readiness = Invoke-RestMethod -Method Get -Uri "$ApiBaseUrl/api/readiness" -TimeoutSec $TimeoutSec
Assert-Condition ([bool]$readiness.readyForChat) "API readiness is false. Phase=$($readiness.phase)"

if (-not [string]::IsNullOrWhiteSpace($UiUrl)) {
    Write-Host "[RuntimeSmoke] Checking UI..."
    $uiResponse = Invoke-WebRequest -UseBasicParsing -Uri $UiUrl -TimeoutSec $TimeoutSec
    Assert-Condition ($uiResponse.StatusCode -ge 200 -and $uiResponse.StatusCode -lt 500) "UI check failed with status $($uiResponse.StatusCode)."
}

Write-Host "[RuntimeSmoke] Checking deterministic smoke endpoint..."
$smoke = Invoke-RestMethod -Method Get -Uri "$ApiBaseUrl/api/smoke" -TimeoutSec $TimeoutSec
Assert-Condition ($smoke.response -eq "READY") "Unexpected /api/smoke response '$($smoke.response)'."
Assert-Condition ($smoke.promptToken -eq $smokePromptToken) "Unexpected smoke prompt token '$($smoke.promptToken)'."

$httpClient = [System.Net.Http.HttpClient]::new()

Write-Host "[RuntimeSmoke] Checking deterministic public SSE..."
$publicStreamRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, "$ApiBaseUrl/api/smoke/stream")
$publicStreamRequest.Headers.Accept.Add([System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new("text/event-stream"))
$publicStream = Read-SseDoneChunk -Client $httpClient -Request $publicStreamRequest
Assert-Condition ($publicStream.Done.fullResponse -eq "READY") "Unexpected /api/smoke/stream fullResponse '$($publicStream.Done.fullResponse)'."

Write-Host "[RuntimeSmoke] Bootstrapping session..."
$chatHeaders = New-SessionHeaders -ApiBase $ApiBaseUrl -Surface "conversation" -RequestedScopes @("chat:read", "chat:write") -TimeoutSec $TimeoutSec

Write-Host "[RuntimeSmoke] Checking deterministic chat request path..."
$chatBody = @{
    message = $smokePromptToken
    maxHistory = 4
    systemInstruction = "deterministic_smoke"
} | ConvertTo-Json -Depth 4
$chatResponse = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/chat" -Headers $chatHeaders -ContentType "application/json" -Body $chatBody -TimeoutSec $TimeoutSec
Assert-Condition ($chatResponse.response -eq "READY") "Unexpected deterministic /api/chat response '$($chatResponse.response)'."

Write-Host "[RuntimeSmoke] Checking deterministic chat SSE path..."
$chatStreamRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$ApiBaseUrl/api/chat/stream")
$chatStreamRequest.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $chatHeaders.Authorization.Substring("Bearer ".Length))
$chatStreamRequest.Headers.Accept.Add([System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new("text/event-stream"))
$chatPayload = @{
    message = $smokePromptToken
    maxHistory = 4
    systemInstruction = "deterministic_smoke"
} | ConvertTo-Json -Depth 4
$chatStreamRequest.Content = [System.Net.Http.StringContent]::new($chatPayload, [System.Text.Encoding]::UTF8, "application/json")
$chatStream = Read-SseDoneChunk -Client $httpClient -Request $chatStreamRequest
Assert-Condition ($chatStream.Done.fullResponse -eq "READY") "Unexpected deterministic /api/chat/stream fullResponse '$($chatStream.Done.fullResponse)'."

Write-Host "[RuntimeSmoke] Passed."
