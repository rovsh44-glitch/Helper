param(
    [string]$ComposeFile = "docker-compose.yml",
    [string]$ServiceName = "searxng",
    [string]$SearchBaseUrl = "http://localhost:8080",
    [string]$ProbeQuery = "helper health check",
    [int]$ReadyTimeoutSec = 120,
    [int]$PollIntervalMs = 1500,
    [string]$ReportPath = "artifacts/runtime/live_web_backend_status.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-SearchProbe {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$Query
    )

    $requestUri = "{0}/search?q={1}&format=json" -f $BaseUrl.TrimEnd('/'), [Uri]::EscapeDataString($Query)
    try {
        $response = Invoke-RestMethod -Method Get -Uri $requestUri -TimeoutSec 20
        $results = @($response.results)
        return [PSCustomObject]@{
            ready = $true
            uri = $requestUri
            resultCount = $results.Count
            error = ""
        }
    }
    catch {
        return [PSCustomObject]@{
            ready = $false
            uri = $requestUri
            resultCount = 0
            error = $_.Exception.Message
        }
    }
}

function Write-BackendReport {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][hashtable]$Payload
    )

    $directory = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $Payload | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding UTF8
}

$composePath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $ComposeFile))
if (-not (Test-Path -LiteralPath $composePath)) {
    throw "[LiveWebBackend] Compose file not found: $composePath"
}

$report = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    composeFile = $composePath
    service = $ServiceName
    searchBaseUrl = $SearchBaseUrl
    probeQuery = $ProbeQuery
    initialProbe = $null
    finalProbe = $null
    dockerComposePs = @()
    status = "unknown"
}

$initialProbe = Invoke-SearchProbe -BaseUrl $SearchBaseUrl -Query $ProbeQuery
$report.initialProbe = $initialProbe
if ($initialProbe.ready) {
    $report.finalProbe = $initialProbe
    $report.status = "already_ready"
    Write-BackendReport -Path $ReportPath -Payload $report
    Write-Host "[LiveWebBackend] Ready without restart. BaseUrl=$SearchBaseUrl Results=$($initialProbe.resultCount)" -ForegroundColor Green
    exit 0
}

Write-Host "[LiveWebBackend] Search endpoint is not ready. Starting $ServiceName from $composePath..." -ForegroundColor Yellow
& docker compose -f $composePath up -d --force-recreate $ServiceName
if ($LASTEXITCODE -ne 0) {
    throw "[LiveWebBackend] docker compose up failed for service '$ServiceName'."
}

$deadline = [DateTime]::UtcNow.AddSeconds([Math]::Max(5, $ReadyTimeoutSec))
$latestProbe = $initialProbe
while ([DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds ([Math]::Max(200, $PollIntervalMs))
    $latestProbe = Invoke-SearchProbe -BaseUrl $SearchBaseUrl -Query $ProbeQuery
    if ($latestProbe.ready) {
        break
    }
}

$report.finalProbe = $latestProbe

try {
    $report.dockerComposePs = @(& docker compose -f $composePath ps --format json | ForEach-Object {
            if (-not [string]::IsNullOrWhiteSpace($_)) {
                $_ | ConvertFrom-Json
            }
        })
}
catch {
    $report.dockerComposePs = @(
        [PSCustomObject]@{
            error = $_.Exception.Message
        }
    )
}

if ($latestProbe.ready) {
    $report.status = "ready"
    Write-BackendReport -Path $ReportPath -Payload $report
    Write-Host "[LiveWebBackend] Ready. BaseUrl=$SearchBaseUrl Results=$($latestProbe.resultCount)" -ForegroundColor Green
    exit 0
}

$report.status = "failed"
Write-BackendReport -Path $ReportPath -Payload $report
throw "[LiveWebBackend] Search backend did not become ready within $ReadyTimeoutSec seconds. Last error: $($latestProbe.error)"
