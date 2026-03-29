Set-StrictMode -Version Latest

function Get-CanonicalTopLevelDocAllowlist {
    return @(
        "README.md",
        "CURRENT_STATE.md",
        "GITHUB_SHOWCASE_IMPLEMENTATION_CHECKLIST_2026-03-24.md"
    )
}

function Get-HistoricalTopLevelDocsIndexPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    return Join-Path $RepoRoot "doc/archive/HISTORICAL_TOP_LEVEL_DOCS_INDEX.md"
}

function ConvertTo-RepoRelativeDocPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$FullPath
    )

    $repoRootFullPath = [System.IO.Path]::GetFullPath($RepoRoot)
    $candidateFullPath = [System.IO.Path]::GetFullPath($FullPath)
    if (-not $candidateFullPath.StartsWith($repoRootFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$FullPath' is outside repository root '$RepoRoot'."
    }

    $relativePath = $candidateFullPath.Substring($repoRootFullPath.Length).TrimStart('\', '/')
    return ($relativePath -replace '\\', '/')
}

function ConvertTo-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseDirectory,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $baseFullPath = [System.IO.Path]::GetFullPath($BaseDirectory)
    if (-not $baseFullPath.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString(), [System.StringComparison]::Ordinal)) {
        $baseFullPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $baseUri = [System.Uri]::new($baseFullPath)
    $targetUri = [System.Uri]::new([System.IO.Path]::GetFullPath($TargetPath))
    $relativeUri = $baseUri.MakeRelativeUri($targetUri)
    return [System.Uri]::UnescapeDataString($relativeUri.ToString())
}

function Get-TopLevelHistoricalMarkdownDocs {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $allowlist = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($fileName in Get-CanonicalTopLevelDocAllowlist) {
        [void]$allowlist.Add($fileName)
    }

    $docRoot = Join-Path $RepoRoot "doc"
    $files = Get-ChildItem -LiteralPath $docRoot -File -Filter "*.md" -ErrorAction SilentlyContinue |
        Sort-Object -Property Name

    $results = New-Object System.Collections.Generic.List[object]
    foreach ($file in $files) {
        if ($allowlist.Contains($file.Name)) {
            continue
        }

        $results.Add([PSCustomObject]@{
                Name = $file.Name
                RelativePath = ConvertTo-RepoRelativeDocPath -RepoRoot $RepoRoot -FullPath $file.FullName
                FullPath = $file.FullName
            })
    }

    return @($results.ToArray())
}

function Get-HistoricalTopLevelDocLinksFromIndex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $indexPath = Get-HistoricalTopLevelDocsIndexPath -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $indexPath)) {
        return @()
    }

    $content = Get-Content -LiteralPath $indexPath -Raw -Encoding UTF8
    $indexDirectory = Split-Path -Parent $indexPath
    $results = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $linkMatches = [System.Text.RegularExpressions.Regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')

    foreach ($match in $linkMatches) {
        $rawTarget = $match.Groups[1].Value.Trim()
        if ([string]::IsNullOrWhiteSpace($rawTarget)) {
            continue
        }

        if ($rawTarget.StartsWith("http://", [System.StringComparison]::OrdinalIgnoreCase) -or
            $rawTarget.StartsWith("https://", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        try {
            $fullPath = if ([System.IO.Path]::IsPathRooted($rawTarget)) {
                [System.IO.Path]::GetFullPath($rawTarget)
            }
            else {
                [System.IO.Path]::GetFullPath((Join-Path $indexDirectory $rawTarget))
            }

            if (-not (Test-Path -LiteralPath $fullPath)) {
                continue
            }

            $relativePath = ConvertTo-RepoRelativeDocPath -RepoRoot $RepoRoot -FullPath $fullPath
            if ($relativePath -match '^doc/[^/]+\.md$') {
                [void]$results.Add($relativePath)
            }
        }
        catch {
            continue
        }
    }

    return @($results | Sort-Object)
}
