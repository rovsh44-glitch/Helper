param(
    [string]$ApiBase = "http://localhost:5000",
    [string]$BootstrapApiKey = $env:HELPER_API_KEY,
    [string]$Role = "integration",
    [string]$Scopes = "chat:read,chat:write",
    [int]$TtlDays = 90,
    [string]$KeyId = ""
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

$scopeList = $Scopes.Split(",", [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
$rotateBody = @{
    keyId = if ([string]::IsNullOrWhiteSpace($KeyId)) { $null } else { $KeyId }
    role = $Role
    scopes = $scopeList
    ttlDays = $TtlDays
} | ConvertTo-Json -Depth 6

$result = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/auth/keys/rotate" -Headers $headers -ContentType "application/json" -Body $rotateBody
$result | ConvertTo-Json -Depth 6
