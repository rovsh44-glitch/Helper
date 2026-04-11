param(
    [string]$ReportPath = "",
    [ValidateSet("workspace", "repo")][string]$ScanMode = "workspace",
    [string]$WorkspaceRoot = "."
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolvedWorkspaceRoot = [System.IO.Path]::GetFullPath($WorkspaceRoot)

$patterns = @(
    'AIza[0-9A-Za-z_-]{35}',
    'helper_secure_vault_2026_!X',
    '(?m)^\s*HELPER_API_KEY\s*=\s*(?:"[^"\r\n]+"|''[^''\r\n]+''|[^#\r\n]+?)\s*$',
    '(?m)^\s*HELPER_SESSION_SIGNING_KEY\s*=\s*(?:"[^"\r\n]+"|''[^''\r\n]+''|[^#\r\n]+?)\s*$',
    'sk-[0-9A-Za-z]{20,}',
    'authorization\s*:\s*bearer\s+[A-Za-z0-9._~+/\-=]{12,}',
    '-----BEGIN [A-Z ]*PRIVATE KEY-----',
    'ghp_[A-Za-z0-9]{20,}'
)

$safeEnvLinePatterns = @(
    '^\s*$',
    '^\s*#',
    '^\s*[A-Z0-9_]+\s*=\s*$',
    '^\s*[A-Z0-9_]+\s*=\s*(''''|""|<set-me>|<set-a-long-random-secret>|<redacted>|changeme|change_me|replace_me|\$\{[A-Z0-9_]+\})\s*$'
)

$excludedDirectoryNames = @(
    "bin",
    "obj",
    "node_modules",
    "dist",
    ".vs",
    "temp",
    "artifacts",
    "sandbox",
    "coverage"
)

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$Path
    )

    $resolvedBase = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $baseUri = New-Object System.Uri($resolvedBase)
    $pathUri = New-Object System.Uri($resolvedPath)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

$hits = [System.Collections.Generic.List[object]]::new()
function Test-IsAllowlistedHit([string]$filePath, [string]$pattern, [string]$lineText) {
    $normalizedFile = $filePath.Replace('/', '\')
    if ($normalizedFile.EndsWith("scripts\secret_scan.ps1", [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    if ($normalizedFile -like "*test\Helper.Runtime.Tests\TemplateCertificationServiceTests.cs" -and $pattern -eq 'sk-[0-9A-Za-z]{20,}') {
        if ($lineText -match 'public const string ApiKey = "sk-[0-9A-Za-z]{20,}"') {
            return $true
        }
    }

    if ($normalizedFile -like "*test\Helper.Runtime.Tests\RuntimeSafetyTests.cs" -and $pattern -eq 'authorization\s*:\s*bearer\s+[A-Za-z0-9._~+/\-=]{12,}') {
        if ($lineText -match 'Authorization:\s+Bearer\s+[A-Za-z0-9._-]{12,}') {
            return $true
        }
    }

    return $false
}

function Test-IsAllowlistedEnvHit([string]$filePath, [string]$lineText) {
    $fileName = [System.IO.Path]::GetFileName($filePath)
    if (-not ($fileName -like ".env*")) {
        return $false
    }

    foreach ($pattern in $safeEnvLinePatterns) {
        if ($lineText -match $pattern) {
            return $true
        }
    }

    return $false
}

function Get-RepoTrackedFiles {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        throw "[SecretScan] git is required for repo scan mode."
    }

    $trackedFiles = @(
        @(& git -C $resolvedWorkspaceRoot -c core.quotePath=false ls-files --cached --deduplicate 2>$null) |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                ($_ -replace '/', '\') -ne 'scripts\secret_scan.ps1'
            } |
            ForEach-Object { Join-Path $resolvedWorkspaceRoot $_ }
    )

    if ($trackedFiles.Count -eq 0) {
        return @()
    }

    return $trackedFiles
}

function Get-WorkspaceFiles {
    $files = Get-ChildItem -LiteralPath $resolvedWorkspaceRoot -Recurse -Force -File -ErrorAction SilentlyContinue
    return @($files | Where-Object {
            $relativePath = Get-RelativePath -BasePath $resolvedWorkspaceRoot -Path $_.FullName
            if ($relativePath -eq 'scripts\secret_scan.ps1') {
                return $false
            }

            foreach ($directoryName in $excludedDirectoryNames) {
                if ($relativePath -like "*\${directoryName}\*") {
                    return $false
                }
            }

            return $true
        } | ForEach-Object { $_.FullName })
}

$candidateFiles = @(
if ($ScanMode -eq "repo") {
    Get-RepoTrackedFiles
}
else {
    Get-WorkspaceFiles
}
)

if ($candidateFiles.Count -eq 0) {
    $patterns = @()
}

foreach ($pattern in $patterns) {
    $output = @(Select-String -Path $candidateFiles -Pattern $pattern -ErrorAction SilentlyContinue)
    if ($output.Count -eq 0) {
        continue
    }

    foreach ($line in $output) {
        $file = Get-RelativePath -BasePath $resolvedWorkspaceRoot -Path ([string]$line.Path)
        $lineNumber = [int]$line.LineNumber
        $content = ([string]$line.Line).TrimEnd("`r", "`n")

        if (Test-IsAllowlistedEnvHit $file $content) {
            continue
        }
        if (Test-IsAllowlistedHit $file $pattern $content) {
            continue
        }

        $normalizedFile = $file -replace '/', '\'
        $hits.Add([PSCustomObject]@{
            File = $normalizedFile
            Line = $lineNumber
            Pattern = $pattern
            Snippet = if ($content.Length -le 180) { $content } else { $content.Substring(0, 180) + "..." }
        })
    }
}

$report = [PSCustomObject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    scanMode = $ScanMode
    workspaceRoot = $resolvedWorkspaceRoot
    hits = @($hits | Sort-Object File, Line, Pattern -Unique)
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportDirectory = Split-Path -Parent $ReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }

    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding UTF8
}

if ($report.hits.Count -gt 0) {
    Write-Host "[SecretScan] Potential first-party secret leaks found (mode=$ScanMode):" -ForegroundColor Red
    $report.hits | Select-Object -First 50 File, Line, Pattern, Snippet | Format-Table -AutoSize
    exit 1
}

Write-Host "[SecretScan] No known secret patterns found (mode=$ScanMode)." -ForegroundColor Green

