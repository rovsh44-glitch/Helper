param(
    [Parameter(Mandatory = $true)]
    [string]$KeyId,
    [string]$ApiBase = "http://localhost:5000",
    [string]$BootstrapApiKey = $env:HELPER_API_KEY,
    [string]$Reason = "manual revoke"
)

if ([string]::IsNullOrWhiteSpace($BootstrapApiKey)) {
    throw "Bootstrap API key is required. Pass -BootstrapApiKey or set HELPER_API_KEY."
}

$bootstrapBody = @{
    apiKey = $BootstrapApiKey
    requestedScopes = @("auth:manage")
    ttlMinutes = 10
} | ConvertTo-Json -Depth 4

$session = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/auth/session" -ContentType "application/json" -Body $bootstrapBody
if (-not $session.accessToken) {
    throw "Failed to issue session token for auth:manage scope."
}

$headers = @{
    Authorization = "Bearer $($session.accessToken)"
}

$revokeBody = @{
    reason = $Reason
} | ConvertTo-Json -Depth 3

$result = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/auth/keys/$KeyId/revoke" -Headers $headers -ContentType "application/json" -Body $revokeBody
$result | ConvertTo-Json -Depth 4
