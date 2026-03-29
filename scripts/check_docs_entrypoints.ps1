param(
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "common/DocsGovernanceCommon.ps1")

$requiredFiles = @(
    "README.md",
    "doc/README.md",
    "doc/architecture/README.md",
    "doc/architecture/SYSTEM_OVERVIEW.md",
    "doc/architecture/RUNTIME_SURFACES.md",
    "doc/architecture/REPO_ATLAS.md",
    "doc/config/README.md",
    "doc/config/ENV_REFERENCE.md",
    "doc/operator/README.md",
    "doc/research/README.md",
    "doc/security/README.md",
    "doc/extensions/README.md",
    "doc/security/TRUST_MODEL.md",
    "doc/security/REPO_HYGIENE_AND_RUNTIME_ARTIFACT_GOVERNANCE.md",
    "doc/adr/README.md",
    "doc/archive/README.md",
    "doc/archive/HISTORICAL_TOP_LEVEL_DOCS_INDEX.md",
    "doc/certification/README.md"
)

$requiredReadmeLinks = @(
    "doc/README.md",
    "doc/architecture/README.md",
    "doc/config/README.md",
    "doc/operator/README.md",
    "doc/research/README.md",
    "doc/security/README.md",
    "doc/extensions/README.md"
)

$requiredDocIndexLinks = @(
    "doc/architecture/README.md",
    "doc/config/README.md",
    "doc/operator/README.md",
    "doc/research/README.md",
    "doc/security/README.md",
    "doc/extensions/README.md",
    "doc/adr/README.md",
    "doc/archive/README.md"
)

$failures = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

function Normalize-DocLinkTarget {
    param(
        [Parameter(Mandatory = $true)][string]$DocumentRelativePath,
        [Parameter(Mandatory = $true)][string]$Target
    )

    $trimmed = $Target.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $null
    }

    if ($trimmed -match '^(#|mailto:|https?:)') {
        return $null
    }

    $trimmed = $trimmed.Split('#')[0].Split('?')[0]
    $trimmed = $trimmed.Replace('\', '/')

    if ($trimmed.StartsWith('/C:/', [System.StringComparison]::OrdinalIgnoreCase)) {
        $trimmed = $trimmed.Substring(1)
    }

    $repoRootNormalized = $repoRoot.Replace('\', '/').TrimEnd('/')
    if ($trimmed.StartsWith($repoRootNormalized + '/', [System.StringComparison]::OrdinalIgnoreCase)) {
        $trimmed = $trimmed.Substring($repoRootNormalized.Length + 1)
    }

    if ([System.IO.Path]::IsPathRooted($trimmed)) {
        return $trimmed.Replace('\', '/')
    }

    $documentDirectory = Split-Path $DocumentRelativePath -Parent
    if ([string]::IsNullOrWhiteSpace($documentDirectory)) {
        $documentDirectory = "."
    }

    $candidate = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $repoRoot $documentDirectory) $trimmed))
    $normalizedCandidate = $candidate.Replace('\', '/')
    if ($normalizedCandidate.StartsWith($repoRootNormalized + '/', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $normalizedCandidate.Substring($repoRootNormalized.Length + 1)
    }

    return $normalizedCandidate
}

function Get-NormalizedDocumentReferences {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $fullPath = Join-Path $repoRoot $RelativePath
    $content = Get-Content -Path $fullPath -Raw
    $targets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    $markdownLinks = [System.Text.RegularExpressions.Regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')
    foreach ($match in $markdownLinks) {
        $normalized = Normalize-DocLinkTarget -DocumentRelativePath $RelativePath -Target $match.Groups[1].Value
        if (-not [string]::IsNullOrWhiteSpace($normalized)) {
            [void]$targets.Add($normalized)
        }
    }

    $inlineCodeSpans = [System.Text.RegularExpressions.Regex]::Matches($content, '`([^`]+)`')
    foreach ($match in $inlineCodeSpans) {
        $candidate = $match.Groups[1].Value
        if ($candidate -notmatch '[/\\]' -and $candidate -notmatch '\.md$') {
            continue
        }

        $normalized = Normalize-DocLinkTarget -DocumentRelativePath $RelativePath -Target $candidate
        if (-not [string]::IsNullOrWhiteSpace($normalized)) {
            [void]$targets.Add($normalized)
        }
    }

    return $targets
}

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        $failures.Add("Missing required documentation entrypoint: $relativePath")
    }
}

$rootReadmePath = Join-Path $repoRoot "README.md"
if (Test-Path -LiteralPath $rootReadmePath) {
    $rootReadmeLinks = Get-NormalizedDocumentReferences -RelativePath "README.md"
    foreach ($link in $requiredReadmeLinks) {
        if (-not $rootReadmeLinks.Contains($link)) {
            $failures.Add("README.md does not reference $link")
        }
    }
}

$docIndexPath = Join-Path $repoRoot "doc/README.md"
if (Test-Path -LiteralPath $docIndexPath) {
    $docIndex = Get-Content -Path $docIndexPath -Raw
    $docIndexLinks = Get-NormalizedDocumentReferences -RelativePath "doc/README.md"
    foreach ($link in $requiredDocIndexLinks) {
        if (-not $docIndexLinks.Contains($link)) {
            $failures.Add("doc/README.md does not reference $link")
        }
    }

    if (-not $docIndex.Contains("Historical / Evidence Docs")) {
        $failures.Add("doc/README.md must clearly distinguish historical / evidence docs from canonical active docs.")
    }
}

$historicalTopLevelDocs = @(Get-TopLevelHistoricalMarkdownDocs -RepoRoot $repoRoot)
$indexedHistoricalTopLevelDocPaths = @(Get-HistoricalTopLevelDocLinksFromIndex -RepoRoot $repoRoot)

$indexedHistoricalTopLevelDocSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($relativePath in $indexedHistoricalTopLevelDocPaths) {
    [void]$indexedHistoricalTopLevelDocSet.Add($relativePath)
}

$actualHistoricalTopLevelDocSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$unindexedHistoricalTopLevelDocs = New-Object System.Collections.Generic.List[string]
foreach ($doc in $historicalTopLevelDocs) {
    [void]$actualHistoricalTopLevelDocSet.Add($doc.RelativePath)
    if (-not $indexedHistoricalTopLevelDocSet.Contains($doc.RelativePath)) {
        $unindexedHistoricalTopLevelDocs.Add($doc.Name)
    }
}

$staleIndexedHistoricalTopLevelDocs = New-Object System.Collections.Generic.List[string]
foreach ($relativePath in $indexedHistoricalTopLevelDocPaths) {
    if (-not $actualHistoricalTopLevelDocSet.Contains($relativePath)) {
        $staleIndexedHistoricalTopLevelDocs.Add($relativePath)
    }
}

$unindexedHistoricalTopLevelDocs = @($unindexedHistoricalTopLevelDocs | Sort-Object -Unique)
$staleIndexedHistoricalTopLevelDocs = @($staleIndexedHistoricalTopLevelDocs | Sort-Object -Unique)

if ($unindexedHistoricalTopLevelDocs.Count -gt 0) {
    $sampleCount = [Math]::Min(8, $unindexedHistoricalTopLevelDocs.Count)
    $sample = ($unindexedHistoricalTopLevelDocs | Select-Object -First $sampleCount) -join ", "
    $warnings.Add(
        "Historical or ad hoc top-level docs remain outside canonical indexes: count=$($unindexedHistoricalTopLevelDocs.Count); sample=$sample"
    )
}

if ($staleIndexedHistoricalTopLevelDocs.Count -gt 0) {
    $sampleCount = [Math]::Min(8, $staleIndexedHistoricalTopLevelDocs.Count)
    $sample = ($staleIndexedHistoricalTopLevelDocs | Select-Object -First $sampleCount) -join ", "
    $warnings.Add(
        "Historical top-level docs index contains stale entries: count=$($staleIndexedHistoricalTopLevelDocs.Count); sample=$sample"
    )
}

$report = [PSCustomObject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    failures = @($failures)
    warnings = @($warnings | Sort-Object -Unique)
    historicalTopLevelDocCount = $historicalTopLevelDocs.Count
    indexedHistoricalTopLevelDocCount = $indexedHistoricalTopLevelDocPaths.Count
    unindexedHistoricalTopLevelDocCount = $unindexedHistoricalTopLevelDocs.Count
    unindexedHistoricalTopLevelDocSample = @($unindexedHistoricalTopLevelDocs | Select-Object -First 8)
    staleHistoricalIndexEntryCount = $staleIndexedHistoricalTopLevelDocs.Count
    staleHistoricalIndexEntrySample = @($staleIndexedHistoricalTopLevelDocs | Select-Object -First 8)
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportDirectory = Split-Path -Parent $ReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }

    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding UTF8
}

if ($warnings.Count -gt 0) {
    Write-Host "[Docs] Canonical entrypoints are in place, but historical top-level docs still exist." -ForegroundColor Yellow
    $warnings | ForEach-Object { Write-Host " - $_" -ForegroundColor Yellow }
}

if ($failures.Count -gt 0) {
    Write-Host "[Docs] Documentation entrypoint check failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "[Docs] Canonical documentation entrypoints are valid." -ForegroundColor Green
