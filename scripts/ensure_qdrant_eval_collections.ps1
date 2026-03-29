param(
    [string]$QdrantBase = "http://localhost:6333",
    [int]$VectorSize = 768,
    [string]$ReportPath = "doc/qdrant_eval_collections_preflight.md",
    [string[]]$RequiredCollections = @(
        "helper_knowledge",
        "knowledge_generic",
        "knowledge_computer_science",
        "knowledge_engineering",
        "knowledge_history",
        "knowledge_encyclopedias"
    )
)

$ErrorActionPreference = "Stop"

function Get-ExistingCollections {
    param([string]$BaseUrl)

    $response = Invoke-RestMethod -Method Get -Uri "$BaseUrl/collections" -TimeoutSec 20
    $items = @($response.result.collections)
    return @($items | ForEach-Object { [string]$_.name } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Ensure-Collection {
    param(
        [string]$BaseUrl,
        [string]$CollectionName,
        [int]$Size
    )

    $uri = "$BaseUrl/collections/$CollectionName"
    $payload = @{
        vectors = @{
            size = $Size
            distance = "Cosine"
        }
    } | ConvertTo-Json -Depth 6 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    Invoke-RestMethod -Method Put -Uri $uri -ContentType "application/json; charset=utf-8" -Body $bytes -TimeoutSec 20 | Out-Null
}

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
$statuses = New-Object System.Collections.Generic.List[object]
$failed = New-Object System.Collections.Generic.List[string]

Write-Host "[QdrantPreflight] Loading collection list from $QdrantBase..."
$existing = Get-ExistingCollections -BaseUrl $QdrantBase
$existingSet = New-Object "System.Collections.Generic.HashSet[string]" ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($item in $existing) { [void]$existingSet.Add($item) }

foreach ($collection in $RequiredCollections) {
    if ([string]::IsNullOrWhiteSpace($collection)) {
        continue
    }

    if ($existingSet.Contains($collection)) {
        $statuses.Add([PSCustomObject]@{
            collection = $collection
            status = "exists"
            detail = "already present"
        })
        continue
    }

    try {
        Ensure-Collection -BaseUrl $QdrantBase -CollectionName $collection -Size $VectorSize
        [void]$existingSet.Add($collection)
        $statuses.Add([PSCustomObject]@{
            collection = $collection
            status = "created"
            detail = "created with cosine/$VectorSize"
        })
    }
    catch {
        $msg = $_.Exception.Message
        $statuses.Add([PSCustomObject]@{
            collection = $collection
            status = "failed"
            detail = $msg
        })
        $failed.Add("${collection}: $msg")
    }
}

$outDir = [System.IO.Path]::GetDirectoryName($ReportPath)
if (-not [string]::IsNullOrWhiteSpace($outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$lines = @()
$lines += "# Qdrant Eval Collection Preflight"
$lines += "Generated: $timestamp"
$lines += "Qdrant base: $QdrantBase"
$lines += "Vector size: $VectorSize"
$lines += ""
$lines += "| Collection | Status | Detail |"
$lines += "|---|---|---|"
foreach ($row in $statuses) {
    $safeDetail = ([string]$row.detail).Replace("|", "\|")
    $lines += "| $($row.collection) | $($row.status) | $safeDetail |"
}

$lines += ""
if ($failed.Count -eq 0) {
    $lines += "Status: PASS"
}
else {
    $lines += "Status: FAIL"
    $lines += "## Failures"
    foreach ($entry in $failed) {
        $lines += "- $entry"
    }
}

Set-Content -Path $ReportPath -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[QdrantPreflight] Report saved to $ReportPath"

if ($failed.Count -gt 0) {
    throw "[QdrantPreflight] Failed to ensure one or more collections."
}

Write-Host "[QdrantPreflight] Passed." -ForegroundColor Green
