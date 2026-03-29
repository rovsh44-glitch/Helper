param(
    [string]$WorkspaceRoot = ".",
    [string]$ArchiveRoot = "doc/pre_certification/archive",
    [bool]$ResetRootRunLog = $true,
    [bool]$ResetProjectRunLogs = $true,
    [bool]$ResetParityNightly = $true,
    [bool]$ResetCertificationArtifacts = $false,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath($WorkspaceRoot)
if (-not (Test-Path $root)) {
    throw "[PreCertReset] Workspace root not found: $root"
}

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")
$pathConfig = Get-HelperPathConfig -WorkspaceRoot $root
$legacyProjectsDir = Join-Path $root "PROJECTS"

$stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$archiveBase = Join-Path $root $ArchiveRoot
$archiveDir = Join-Path $archiveBase ("reset_" + $stamp)
$archiveFiles = Join-Path $archiveDir "files"

if (-not $DryRun.IsPresent) {
    New-Item -ItemType Directory -Force -Path $archiveFiles | Out-Null
}

$moved = New-Object System.Collections.Generic.List[string]
$createdEmpty = New-Object System.Collections.Generic.List[string]
$planned = New-Object System.Collections.Generic.List[string]

function Get-ArchiveRelativePath {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    $baseNormalized = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/')
    $targetNormalized = [System.IO.Path]::GetFullPath($TargetPath)
    $baseWithSeparator = $baseNormalized + [System.IO.Path]::DirectorySeparatorChar

    if ($targetNormalized.StartsWith($baseWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $targetNormalized.Substring($baseWithSeparator.Length)
    }

    $targetRoot = [System.IO.Path]::GetPathRoot($targetNormalized)
    $driveSegment = ""
    if (-not [string]::IsNullOrWhiteSpace($targetRoot)) {
        $driveSegment = $targetRoot.TrimEnd('\', '/').TrimEnd(':')
    }

    $tail = $targetNormalized
    if (-not [string]::IsNullOrWhiteSpace($targetRoot) -and $targetNormalized.Length -gt $targetRoot.Length) {
        $tail = $targetNormalized.Substring($targetRoot.Length).TrimStart('\', '/')
    }

    if ([string]::IsNullOrWhiteSpace($driveSegment)) {
        if ([string]::IsNullOrWhiteSpace($tail)) {
            return "external"
        }

        return Join-Path "external" $tail
    }

    if ([string]::IsNullOrWhiteSpace($tail)) {
        return Join-Path "external" $driveSegment
    }

    return Join-Path (Join-Path "external" $driveSegment) $tail
}

function Archive-File {
    param([string]$FilePath)

    if (-not (Test-Path -LiteralPath $FilePath)) {
        return
    }

    $full = [System.IO.Path]::GetFullPath($FilePath)
    $relative = Get-ArchiveRelativePath -BasePath $root -TargetPath $full
    $target = Join-Path $archiveFiles $relative

    if ($DryRun.IsPresent) {
        $planned.Add($relative) | Out-Null
        return
    }

    $targetDir = Split-Path -Parent $target
    if (-not [string]::IsNullOrWhiteSpace($targetDir)) {
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    }

    Move-Item -LiteralPath $full -Destination $target -Force
    $moved.Add($relative) | Out-Null
}

function Ensure-EmptyFile {
    param([string]$FilePath)

    $full = [System.IO.Path]::GetFullPath($FilePath)
    $dir = Split-Path -Parent $full
    if (-not [string]::IsNullOrWhiteSpace($dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }

    Set-Content -Path $full -Value "" -Encoding UTF8
    $relative = Get-ArchiveRelativePath -BasePath $root -TargetPath $full
    $createdEmpty.Add($relative) | Out-Null
}

if ($ResetRootRunLog) {
    $rootRunCandidates = @(
        (Join-Path $root "generation_runs.jsonl"),
        (Join-Path $pathConfig.DataRoot "generation_runs.jsonl"),
        (Join-Path $pathConfig.ProjectsRoot "generation_runs.jsonl")
    ) | Select-Object -Unique

    foreach ($rootRun in $rootRunCandidates) {
        if (Test-Path -LiteralPath $rootRun) {
            Archive-File -FilePath $rootRun
            if (-not $DryRun.IsPresent) {
                Ensure-EmptyFile -FilePath $rootRun
            }
        }
    }
}

if ($ResetProjectRunLogs) {
    $projectDirs = @($pathConfig.ProjectsRoot, $legacyProjectsDir) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) } |
        Select-Object -Unique

    foreach ($projectsDir in $projectDirs) {
        $projectRuns = Get-ChildItem -Path $projectsDir -Recurse -Filter "generation_runs.jsonl" -File -ErrorAction SilentlyContinue
        foreach ($log in $projectRuns) {
            Archive-File -FilePath $log.FullName
            if (-not $DryRun.IsPresent) {
                Ensure-EmptyFile -FilePath $log.FullName
            }
        }
    }
}

if ($ResetParityNightly) {
    $parityDir = Join-Path $root "doc/parity_nightly"
    if (Test-Path -LiteralPath $parityDir) {
        $parityFiles = Get-ChildItem -Path $parityDir -Recurse -File -ErrorAction SilentlyContinue
        foreach ($file in $parityFiles) {
            Archive-File -FilePath $file.FullName
        }
    }

    if (-not $DryRun.IsPresent) {
        New-Item -ItemType Directory -Force -Path (Join-Path $root "doc/parity_nightly/daily") | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $root "doc/parity_nightly/history") | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $root "doc/parity_nightly/backfill") | Out-Null
    }
}

if ($ResetCertificationArtifacts) {
    $certDir = Join-Path $root "doc/certification_2026-03-15"
    if (Test-Path -LiteralPath $certDir) {
        $certFiles = Get-ChildItem -Path $certDir -Recurse -File -ErrorAction SilentlyContinue
        foreach ($file in $certFiles) {
            Archive-File -FilePath $file.FullName
        }
    }
}

$summary = @()
$summary += "# Pre-Cert Reset Summary"
$summary += "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K')"
$summary += "Workspace: $root"
$summary += "DryRun: $($DryRun.IsPresent)"
$summary += ""
$summary += "## Scope"
$summary += "- ResetRootRunLog: $ResetRootRunLog"
$summary += "- ResetProjectRunLogs: $ResetProjectRunLogs"
$summary += "- ResetParityNightly: $ResetParityNightly"
$summary += "- ResetCertificationArtifacts: $ResetCertificationArtifacts"
$summary += ""

if ($DryRun.IsPresent) {
    $summary += "## Planned Archives"
    $summary += "- Count: $($planned.Count)"
    foreach ($item in $planned) {
        $summary += "- $item"
    }
}
else {
    $summary += "## Archived Files"
    $summary += "- ArchiveDir: $archiveDir"
    $summary += "- Count: $($moved.Count)"
    foreach ($item in $moved) {
        $summary += "- $item"
    }

    $summary += ""
    $summary += "## Recreated Empty Logs"
    $summary += "- Count: $($createdEmpty.Count)"
    foreach ($item in $createdEmpty) {
        $summary += "- $item"
    }
}

if (-not $DryRun.IsPresent) {
    $summaryPath = Join-Path $archiveDir "RESET_SUMMARY.md"
    Set-Content -Path $summaryPath -Value ($summary -join "`r`n") -Encoding UTF8
    Write-Host "[PreCertReset] Completed. Archived: $($moved.Count). Summary: $summaryPath" -ForegroundColor Green
}
else {
    $previewPath = Join-Path $archiveBase ("RESET_DRY_RUN_" + $stamp + ".md")
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $previewPath) | Out-Null
    Set-Content -Path $previewPath -Value ($summary -join "`r`n") -Encoding UTF8
    Write-Host "[PreCertReset] Dry run completed. Planned: $($planned.Count). Preview: $previewPath" -ForegroundColor Yellow
}
