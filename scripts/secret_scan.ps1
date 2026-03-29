param(
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

$patterns = @(
    'AIza[0-9A-Za-z_-]{35}',
    'helper_secure_vault_2026_!X',
    'HELPER_API_KEY\s*=\s*[''"][^''"]+[''"]',
    'sk-[0-9A-Za-z]{20,}',
    'authorization\s*:\s*bearer\s+[A-Za-z0-9._~+/\-=]{12,}',
    '-----BEGIN [A-Z ]*PRIVATE KEY-----',
    'ghp_[A-Za-z0-9]{20,}'
)

$safeEnvLinePatterns = @(
    '^\s*$',
    '^\s*#',
    '^\s*[A-Z0-9_]+\s*=\s*(''''|""|<set-me>|<redacted>|changeme|change_me|replace_me|\$\{[A-Z0-9_]+\})\s*$'
)

$rg = Get-Command rg -ErrorAction SilentlyContinue
if (-not $rg) {
    throw "[SecretScan] ripgrep (rg) is required for fast scanning."
}

$rgExcludes = @(
    "--glob", "!**/bin/**",
    "--glob", "!**/obj/**",
    "--glob", "!**/node_modules/**",
    "--glob", "!**/dist/**",
    "--glob", "!**/.vs/**",
    "--glob", "!**/temp/**",
    "--glob", "!**/artifacts/**",
    "--glob", "!**/sandbox/**",
    "--glob", "!**/coverage/**",
    "--glob", "!scripts/secret_scan.ps1"
)

$hits = @()
function Test-IsAllowlistedHit([string]$filePath, [string]$pattern, [string]$lineText) {
    $normalizedFile = $filePath.Replace('/', '\')
    if ($normalizedFile -like "*test\Helper.Runtime.Tests\TemplateCertificationServiceTests.cs" -and $pattern -eq 'sk-[0-9A-Za-z]{20,}') {
        if ($lineText -match 'public const string ApiKey = "sk-[0-9A-Za-z]{20,}"') {
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

foreach ($pattern in $patterns) {
    $output = & rg --json -n --hidden @rgExcludes -e $pattern . 2>$null
    if (-not $output) {
        continue
    }

    foreach ($line in $output) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $event = $line | ConvertFrom-Json
        if ($event.type -ne "match") {
            continue
        }

        $file = [string]$event.data.path.text
        $lineNumber = [int]$event.data.line_number
        $content = ([string]$event.data.lines.text).TrimEnd("`r", "`n")
        if (Test-IsAllowlistedEnvHit $file $content) {
            continue
        }
        if (Test-IsAllowlistedHit $file $pattern $content) {
            continue
        }

        $hits += [PSCustomObject]@{
            File = $file
            Line = $lineNumber
            Pattern = $pattern
            Snippet = if ($content.Length -le 180) { $content } else { $content.Substring(0, 180) + "..." }
        }
    }
}

$report = [PSCustomObject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
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
    Write-Host "[SecretScan] Potential first-party secret leaks found:" -ForegroundColor Red
    $report.hits | Select-Object -First 50 File, Line, Pattern, Snippet | Format-Table -AutoSize
    exit 1
}

Write-Host "[SecretScan] No known secret patterns found in the first-party source surface." -ForegroundColor Green

