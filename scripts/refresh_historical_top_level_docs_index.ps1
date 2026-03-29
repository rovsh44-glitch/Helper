param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "common/DocsGovernanceCommon.ps1")

$indexPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Get-HistoricalTopLevelDocsIndexPath -RepoRoot $repoRoot
}
else {
    $resolvedParent = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($resolvedParent)) {
        New-Item -ItemType Directory -Path $resolvedParent -Force | Out-Null
    }

    if ([System.IO.Path]::IsPathRooted($OutputPath)) {
        [System.IO.Path]::GetFullPath($OutputPath)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
    }
}

$indexDirectory = Split-Path -Parent $indexPath
$historicalDocs = @(Get-TopLevelHistoricalMarkdownDocs -RepoRoot $repoRoot)
$updatedDate = (Get-Date).ToString("yyyy-MM-dd")
$generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Historical Top-Level Docs Index")
$lines.Add("")
$lines.Add("Status: ``active``")
$lines.Add(("Updated: ``{0}``" -f $updatedDate))
$lines.Add(("Generated: ``{0}``" -f $generatedAtUtc))
$lines.Add("")
$lines.Add("## Purpose")
$lines.Add("")
$lines.Add("This index catalogs markdown files that still live directly under ``doc/`` for historical traceability.")
$lines.Add("")
$lines.Add("## Rule")
$lines.Add("")
$lines.Add("If a markdown file lives directly under ``doc/`` and is not a canonical active entrypoint, it must be linked from this index.")
$lines.Add("")
$lines.Add("Regenerate this file with ``powershell -ExecutionPolicy Bypass -File scripts/refresh_historical_top_level_docs_index.ps1`` after adding or relocating top-level historical markdown docs.")
$lines.Add("")
$lines.Add("## Indexed Files")
$lines.Add("")
$lines.Add(("Count: ``{0}``" -f $historicalDocs.Count))
$lines.Add("")

foreach ($doc in $historicalDocs) {
    $relativeLink = ConvertTo-RelativePath -BaseDirectory $indexDirectory -TargetPath $doc.FullPath
    $lines.Add("- [$($doc.Name)]($relativeLink)")
}

$content = ($lines -join [Environment]::NewLine) + [Environment]::NewLine
[System.IO.File]::WriteAllText($indexPath, $content, [System.Text.UTF8Encoding]::new($false))

Write-Host "[Docs] Refreshed historical top-level docs index: $indexPath" -ForegroundColor Green
Write-Host "[Docs] Indexed markdown files: $($historicalDocs.Count)" -ForegroundColor Green
