param(
    [string]$LogPath = "temp/verification/frontend_build.log",
    [string]$SummaryPath = "temp/verification/frontend_build_summary.json",
    [switch]$RequireRebuild,
    [switch]$DisableTranscript
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-FrontendVerificationSummary {
    param(
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Mode,
        [Parameter(Mandatory = $true)][string]$Details,
        [Parameter(Mandatory = $true)][string]$SummaryPath
    )

    $payload = [ordered]@{
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        status = $Status
        mode = $Mode
        details = $Details
    }

    Set-Content -Path $SummaryPath -Value ($payload | ConvertTo-Json -Depth 4) -Encoding UTF8
}

function Get-LatestSourceWriteTimeUtc {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $roots = @(
        "components",
        "contexts",
        "hooks",
        "services"
    )

    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    foreach ($root in $roots) {
        $resolvedRoot = Join-Path $RepoRoot $root
        if (Test-Path $resolvedRoot) {
            foreach ($file in Get-ChildItem -Path $resolvedRoot -Recurse -File) {
                $files.Add($file)
            }
        }
    }

    foreach ($singleFile in @(
        "App.tsx",
        "types.ts",
        "package.json",
        "vite.config.ts",
        "vite.shared.config.mjs"
    )) {
        $resolvedFile = Join-Path $RepoRoot $singleFile
        if (Test-Path $resolvedFile) {
            $files.Add((Get-Item $resolvedFile))
        }
    }

    if ($files.Count -eq 0) {
        return $null
    }

    return ($files | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
}

function Test-FreshDistArtifact {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $distIndex = Join-Path $RepoRoot "dist\index.html"
    if (-not (Test-Path $distIndex)) {
        return $false
    }

    $distWriteTimeUtc = (Get-Item $distIndex).LastWriteTimeUtc
    $latestSourceWriteTimeUtc = Get-LatestSourceWriteTimeUtc -RepoRoot $RepoRoot
    if ($null -eq $latestSourceWriteTimeUtc) {
        return $true
    }

    return $distWriteTimeUtc -ge $latestSourceWriteTimeUtc
}

function Get-FrontendBuildCommand {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $npmCommand = Get-Command npm.cmd -ErrorAction SilentlyContinue
    if ($null -eq $npmCommand) {
        $npmCommand = Get-Command npm -ErrorAction SilentlyContinue
    }

    if ($null -ne $npmCommand) {
        return [ordered]@{
            FileName = $npmCommand.Source
            Arguments = @("run", "build")
            Display = "npm run build"
        }
    }

    throw "[FrontendBuild] Could not resolve npm."
}

function Invoke-FrontendBuildCommand {
    param([Parameter(Mandatory = $true)]$Command)

    Write-Host ("[FrontendBuild][START] {0}" -f $Command.Display) -ForegroundColor Cyan
    Push-Location $repoRoot
    try {
        $global:LASTEXITCODE = 0
        & $Command.FileName @($Command.Arguments)
        if ($LASTEXITCODE -ne 0) {
            throw "[FrontendBuild] Vite CLI exited with code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedLogPath = if ([System.IO.Path]::IsPathRooted($LogPath)) {
    $LogPath
}
else {
    Join-Path $repoRoot $LogPath
}
$resolvedSummaryPath = if ([System.IO.Path]::IsPathRooted($SummaryPath)) {
    $SummaryPath
}
else {
    Join-Path $repoRoot $SummaryPath
}

$logDirectory = Split-Path -Parent $resolvedLogPath
if (-not [string]::IsNullOrWhiteSpace($logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
}
$summaryDirectory = Split-Path -Parent $resolvedSummaryPath
if (-not [string]::IsNullOrWhiteSpace($summaryDirectory)) {
    New-Item -ItemType Directory -Path $summaryDirectory -Force | Out-Null
}

$transcriptStarted = $false
if (-not $DisableTranscript) {
    try {
        Start-Transcript -Path $resolvedLogPath -Force | Out-Null
        $transcriptStarted = $true
    }
    catch {
        Write-Host "[FrontendBuild] Transcript unavailable: $($_.Exception.Message)" -ForegroundColor DarkYellow
    }
}

try {
    if (Test-FreshDistArtifact -RepoRoot $repoRoot) {
        Write-FrontendVerificationSummary -Status "pass" -Mode "reused_dist" -Details "Reused a fresh dist artifact that was produced by the canonical frontend build entrypoint." -SummaryPath $resolvedSummaryPath
        Write-Host "[FrontendBuild][PASS] reused fresh dist artifact" -ForegroundColor Yellow
        return
    }

    try {
        $command = Get-FrontendBuildCommand -RepoRoot $repoRoot
        Invoke-FrontendBuildCommand -Command $command
        if (-not (Test-FreshDistArtifact -RepoRoot $repoRoot)) {
            throw "[FrontendBuild] Build completed, but the dist artifact is still stale."
        }

        Write-FrontendVerificationSummary -Status "pass" -Mode "rebuilt" -Details ("Frontend verification performed a real rebuild via {0}." -f $command.Display) -SummaryPath $resolvedSummaryPath
        Write-Host "[FrontendBuild][PASS] frontend verification build" -ForegroundColor Green
    }
    catch {
        Write-FrontendVerificationSummary -Status "fail" -Mode "build_failed" -Details $_.Exception.Message -SummaryPath $resolvedSummaryPath
        throw
    }
}
finally {
    if ($transcriptStarted) {
        try {
            Stop-Transcript | Out-Null
        }
        catch {
            Write-Host "[FrontendBuild] Transcript stop unavailable: $($_.Exception.Message)" -ForegroundColor DarkYellow
        }
    }
}
