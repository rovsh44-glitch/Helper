param(
    [string]$ReportPath = "temp/hygiene/public_surface_brand_scan.md"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$blockedTokens = @(
    (("Gem") + "ini").ToLowerInvariant(),
    (("Gen") + "esis").ToLowerInvariant()
)
$blockedRegex = "(?i)(" + (($blockedTokens | ForEach-Object { [regex]::Escape($_) }) -join "|") + ")"
$excludedPrefixes = @(
    ".git/",
    "node_modules/",
    "dist/",
    "bin/",
    "obj/",
    "temp/"
)

function Normalize-RelativePath([string]$path) {
    $normalizedRoot = $repoRoot.TrimEnd('\', '/')
    $normalizedPath = $path
    if ($normalizedPath.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalizedPath = $normalizedPath.Substring($normalizedRoot.Length).TrimStart('\', '/')
    }

    return $normalizedPath.Replace('\', '/')
}

function Test-IsExcludedPath([string]$relativePath) {
    if ($relativePath -match '(^|/)(bin|obj|node_modules|dist|TestResults)(/|$)') {
        return $true
    }

    foreach ($prefix in $excludedPrefixes) {
        if ($relativePath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

$fileHits = New-Object System.Collections.Generic.List[object]
$contentHits = New-Object System.Collections.Generic.List[object]

$allFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Force | Where-Object {
    $relative = Normalize-RelativePath $_.FullName
    -not (Test-IsExcludedPath $relative)
}

foreach ($file in $allFiles) {
    $relativePath = Normalize-RelativePath $file.FullName
    $lowerName = [string]::new($file.Name.ToCharArray()).ToLowerInvariant()
    if ($blockedTokens | Where-Object { $lowerName.Contains($_) }) {
        $fileHits.Add([PSCustomObject]@{
            Path = $relativePath
            Detail = "blocked token present in file name"
        })
    }
}

$rg = Get-Command rg -ErrorAction SilentlyContinue
if ($null -eq $rg) {
    throw "[BrandScan] ripgrep (rg) is required."
}

$rgExcludes = @(
    "--glob", "!.git/**",
    "--glob", "!**/node_modules/**",
    "--glob", "!**/dist/**",
    "--glob", "!**/bin/**",
    "--glob", "!**/obj/**",
    "--glob", "!**/TestResults/**",
    "--glob", "!temp/**"
)

$output = & rg --json -n --hidden @rgExcludes -e $blockedRegex . 2>$null
foreach ($line in $output) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $event = $line | ConvertFrom-Json
    if ($event.type -ne "match") {
        continue
    }

    $relativePath = ([string]$event.data.path.text).Replace('\', '/')
    if ($relativePath.StartsWith("./", [System.StringComparison]::Ordinal)) {
        $relativePath = $relativePath.Substring(2)
    }
    if (Test-IsExcludedPath $relativePath) {
        continue
    }

    $content = ([string]$event.data.lines.text).TrimEnd("`r", "`n")
    $contentHits.Add([PSCustomObject]@{
        Path = $relativePath
        Line = [int]$event.data.line_number
        Snippet = if ($content.Length -le 180) { $content } else { $content.Substring(0, 180) + "..." }
    })
}

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
}

$lines = @()
$lines += "# Public Surface Brand Scan"
$lines += "Generated: $timestamp"
$lines += ""
$lines += "- Blocking filename hits: $($fileHits.Count)"
$lines += "- Blocking content hits: $($contentHits.Count)"
$lines += ""

if ($fileHits.Count -eq 0 -and $contentHits.Count -eq 0) {
    $lines += "Status: PASS"
}
else {
    $lines += "Status: FAIL"
    $lines += ""
    if ($fileHits.Count -gt 0) {
        $lines += "## Filename Hits"
        foreach ($hit in $fileHits | Sort-Object Path) {
            $lines += "- $($hit.Path): $($hit.Detail)"
        }
        $lines += ""
    }

    if ($contentHits.Count -gt 0) {
        $lines += "## Content Hits"
        foreach ($hit in $contentHits | Sort-Object Path, Line) {
            $safeSnippet = $hit.Snippet.Replace("`r", " ").Replace("`n", " ")
            $lines += "- $($hit.Path):$($hit.Line) :: $safeSnippet"
        }
    }
}

Set-Content -LiteralPath $ReportPath -Value ($lines -join "`r`n") -Encoding UTF8

if ($fileHits.Count -gt 0 -or $contentHits.Count -gt 0) {
    Write-Host "[BrandScan] Blocking hits found. See $ReportPath" -ForegroundColor Red
    exit 1
}

Write-Host "[BrandScan] Passed. See $ReportPath" -ForegroundColor Green
