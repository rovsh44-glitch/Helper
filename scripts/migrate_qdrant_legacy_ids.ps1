param(
    [string]$QdrantBase = "http://localhost:6333",
    [string]$Collection = "helper_knowledge",
    [int]$BatchSize = 128
)

$ErrorActionPreference = "Stop"

function Get-NormalizedGuid([string]$value) {
    $guid = [Guid]::Empty
    if ([Guid]::TryParse($value, [ref]$guid)) {
        return $guid.ToString()
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($value)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    $guidBytes = New-Object byte[] 16
    [Array]::Copy($hash, 0, $guidBytes, 0, 16)
    return ([Guid]::new($guidBytes)).ToString()
}

$nextOffset = $null
$migrated = 0

Write-Host "[QdrantMigration] Starting migration for collection '$Collection'..."

do {
    $scrollBody = @{
        limit = $BatchSize
        with_payload = $true
        with_vector = $true
    }
    if ($nextOffset) { $scrollBody.offset = $nextOffset }

    $scrollUri = "$QdrantBase/collections/$Collection/points/scroll"
    $scroll = Invoke-RestMethod -Method Post -Uri $scrollUri -ContentType "application/json" -Body ($scrollBody | ConvertTo-Json -Depth 10)

    $points = @($scroll.result.points)
    if ($points.Count -eq 0) { break }

    $upsertPoints = @()
    $deleteIds = @()

    foreach ($point in $points) {
        $oldId = "$($point.id)"
        $newId = Get-NormalizedGuid $oldId
        if ($newId -eq $oldId) { continue }

        $upsertPoints += @{
            id = $newId
            vector = $point.vector
            payload = $point.payload
        }
        $deleteIds += $oldId
    }

    if ($upsertPoints.Count -gt 0) {
        Invoke-RestMethod -Method Put -Uri "$QdrantBase/collections/$Collection/points" -ContentType "application/json" -Body (@{ points = $upsertPoints } | ConvertTo-Json -Depth 15) | Out-Null
        Invoke-RestMethod -Method Post -Uri "$QdrantBase/collections/$Collection/points/delete" -ContentType "application/json" -Body (@{ points = $deleteIds } | ConvertTo-Json -Depth 10) | Out-Null
        $migrated += $upsertPoints.Count
        Write-Host "[QdrantMigration] Migrated batch: $($upsertPoints.Count) point(s)."
    }

    $nextOffset = $scroll.result.next_page_offset
} while ($nextOffset)

Write-Host "[QdrantMigration] Done. Total migrated points: $migrated"
