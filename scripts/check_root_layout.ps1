param(
    [string]$ReportPath = "",
    [string]$WorkspaceRoot = "",
    [string]$BudgetsPath = ""
)

$ErrorActionPreference = "Stop"

$root = if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
} else {
    (Resolve-Path $WorkspaceRoot).Path
}
$effectiveBudgetsPath = if ([string]::IsNullOrWhiteSpace($BudgetsPath)) {
    Join-Path $PSScriptRoot "performance_budgets.json"
} else {
    (Resolve-Path $BudgetsPath).Path
}
$budgets = Get-Content $effectiveBudgetsPath -Raw | ConvertFrom-Json
$rootForbiddenDirs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@($budgets.root.forbiddenDirs) | ForEach-Object { [void]$rootForbiddenDirs.Add([string]$_) }
$accidentalDirPrefixes = @($budgets.root.accidentalDirPrefixes)
$sourceForbiddenDirNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@($budgets.root.sourceForbiddenDirNames) | ForEach-Object { [void]$sourceForbiddenDirNames.Add([string]$_) }

$sourceSurfaceRoots = @(
    [PSCustomObject]@{
        Path = Join-Path $root "src\Helper.Api"
        WarningDirs = @("LOG", "logs", "library", "sandbox", "PROJECTS", "PROJECTS ", "generated_projects", "output_projects", "tmp_tpl_build")
        FatalFiles = @("auth_keys.json")
        WarningFiles = @("API_PORT.txt", "LOG\conversation_store.json")
    }
)

$rootViolations = New-Object System.Collections.Generic.List[object]
$sourceFatalViolations = New-Object System.Collections.Generic.List[object]
$sourceWarnings = New-Object System.Collections.Generic.List[object]
$scriptPolicyViolations = New-Object System.Collections.Generic.List[object]
$artifactScanMode = "fallback_pattern"
$gitMetadataAvailable = $false

$rootDirectories = @(Get-ChildItem -LiteralPath $root -Force -Directory -ErrorAction Stop)
foreach ($directory in $rootDirectories) {
    if ($rootForbiddenDirs.Contains($directory.Name)) {
        $rootViolations.Add([PSCustomObject]@{
            category = "root_forbidden_directory"
            path = $directory.FullName
            detail = "Forbidden directory is present at repository root."
        })
    }

    $matchesAccidentalPrefix = $false
    foreach ($prefix in $accidentalDirPrefixes) {
        if (-not [string]::IsNullOrWhiteSpace($prefix) -and $directory.Name.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $matchesAccidentalPrefix = $true
            break
        }
    }

    if ($matchesAccidentalPrefix) {
        $rootViolations.Add([PSCustomObject]@{
            category = "root_accidental_directory"
            path = $directory.FullName
            detail = "Accidental ad-hoc directory is present at repository root."
        })
    }
}

$sourceRoot = Join-Path $root "src"
if (Test-Path -LiteralPath $sourceRoot) {
    $gitMetadataPath = Join-Path $root ".git"
    $gitCommand = Get-Command git -ErrorAction SilentlyContinue
    if ($null -ne $gitCommand -and (Test-Path -LiteralPath $gitMetadataPath)) {
        $gitMetadataAvailable = $true
        $artifactScanMode = "git_tracked"
        $trackedArtifacts = @(& git -C $root ls-files src 2>$null) |
            Where-Object {
                $_ -match '(^|/)(bin|obj)(/|$)' -or
                $_ -match '(^|/)\.playwright(/|$)'
            } |
            Sort-Object -Unique

        foreach ($relativePath in $trackedArtifacts) {
            $sourceFatalViolations.Add([PSCustomObject]@{
                category = "source_tracked_build_artifact"
                path = (Join-Path $root $relativePath)
                detail = "Tracked build/transient artifact may not live under src/."
            })
        }
    }

    $unexpectedTransientDirectories = Get-ChildItem -LiteralPath $sourceRoot -Force -Directory -Recurse -ErrorAction Stop |
        Where-Object {
            $_.Name -eq ".playwright" -and
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        }

    foreach ($directory in $unexpectedTransientDirectories) {
        $sourceFatalViolations.Add([PSCustomObject]@{
            category = "source_build_artifact_directory"
            path = $directory.FullName
            detail = "Transient artifact directory may not live under src/ outside local build outputs."
        })
    }

    if (-not $gitMetadataAvailable) {
        $fallbackArtifactDirectories = Get-ChildItem -LiteralPath $sourceRoot -Force -Directory -Recurse -ErrorAction Stop |
            Where-Object { $sourceForbiddenDirNames.Contains($_.Name) }

        foreach ($directory in $fallbackArtifactDirectories) {
            if ($directory.Name -eq ".playwright" -and $directory.FullName -notmatch '[\\/](bin|obj)[\\/]') {
                continue
            }

            $sourceWarnings.Add([PSCustomObject]@{
                category = "source_build_artifact_directory_fallback"
                path = $directory.FullName
                detail = "Build output directory detected under src/ while .git metadata is unavailable; tracked-artifact status could not be verified."
            })
        }

        $fallbackArtifactFiles = Get-ChildItem -LiteralPath $sourceRoot -Force -File -Recurse -ErrorAction Stop |
            Where-Object {
                $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and (
                    $_.Extension -in @('.dll', '.exe', '.pdb') -or
                    $_.Name -like '*.deps.json' -or
                    $_.Name -like '*.runtimeconfig.json'
                )
            }

        foreach ($file in $fallbackArtifactFiles) {
            $sourceFatalViolations.Add([PSCustomObject]@{
                category = "source_build_artifact_file_fallback"
                path = $file.FullName
                detail = "Potential build artifact file detected under src/ outside bin/obj while .git metadata is unavailable."
            })
        }
    }
}

foreach ($surface in $sourceSurfaceRoots) {
    if (-not (Test-Path -LiteralPath $surface.Path)) {
        continue
    }

    foreach ($file in $surface.FatalFiles) {
        $fullPath = Join-Path $surface.Path $file
        if (Test-Path -LiteralPath $fullPath) {
            $sourceFatalViolations.Add([PSCustomObject]@{
                category = "source_runtime_auth_artifact"
                path = $fullPath
                detail = "Runtime auth artifacts may not live under src/."
            })
        }
    }

    foreach ($file in $surface.WarningFiles) {
        $fullPath = Join-Path $surface.Path $file
        if (Test-Path -LiteralPath $fullPath) {
            $sourceWarnings.Add([PSCustomObject]@{
                category = "source_runtime_file"
                path = $fullPath
                detail = "Machine-local runtime file detected under src/. Move it under HELPER_DATA_ROOT."
            })
        }
    }

    foreach ($dirName in $surface.WarningDirs) {
        $dirPath = Join-Path $surface.Path $dirName
        if (Test-Path -LiteralPath $dirPath) {
            $sourceWarnings.Add([PSCustomObject]@{
                category = "source_runtime_directory"
                path = $dirPath
                detail = "Machine-local runtime directory detected under src/."
            })
        }
    }
}

$scriptPolicyChecks = @(
    [PSCustomObject]@{
        Path = Join-Path $root "scripts\run_ui_workflow_smoke.ps1"
        Validate = {
            param($path)
            Select-String -Path $path -Pattern 'Join-Path \(Join-Path \$PSScriptRoot "\.\."\) "PROJECTS"' -Quiet
        }
        Detail = "UI smoke script may not fall back to repo-root PROJECTS."
    },
    [PSCustomObject]@{
        Path = Join-Path $root "scripts\common\GenerationArtifactDetection.ps1"
        Validate = {
            param($path)
            $containsLegacyPath = Select-String -Path $path -Pattern 'Join-Path \$WorkspaceRoot "PROJECTS"' -Quiet
            $containsOptInGuard = Select-String -Path $path -Pattern 'if \(\$IncludeLegacyProjectsRoot\.IsPresent\)' -Quiet
            return $containsLegacyPath -and -not $containsOptInGuard
        }
        Detail = "Generation artifact discovery may not scan repo-root PROJECTS by default."
    },
    [PSCustomObject]@{
        Path = Join-Path $root "scripts\run_audit_34_generated_projects.ps1"
        Validate = {
            param($path)
            Select-String -Path $path -Pattern 'src/Helper\.Api/bin/Debug/net8\.0/PROJECTS/generation_runs\.jsonl' -Quiet
        }
        Detail = "Generated-project audit may not default to build-output PROJECTS paths."
    }
)

foreach ($check in $scriptPolicyChecks) {
    if (-not (Test-Path -LiteralPath $check.Path)) {
        continue
    }

    if (& $check.Validate $check.Path) {
        $scriptPolicyViolations.Add([PSCustomObject]@{
            category = "script_runtime_root_fallback"
            path = $check.Path
            detail = $check.Detail
        })
    }
}

$report = [PSCustomObject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    rootPath = $root
    budgetsPath = $effectiveBudgetsPath
    gitMetadataAvailable = $gitMetadataAvailable
    artifactScanMode = $artifactScanMode
    rootViolations = @($rootViolations | Sort-Object path -Unique)
    sourceFatalViolations = @($sourceFatalViolations | Sort-Object path -Unique)
    sourceWarnings = @($sourceWarnings | Sort-Object path -Unique)
    scriptPolicyViolations = @($scriptPolicyViolations | Sort-Object path -Unique)
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportDirectory = Split-Path -Parent $ReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }

    $report | ConvertTo-Json -Depth 8 | Set-Content -Path $ReportPath -Encoding UTF8
}

$fatalCount = $report.rootViolations.Count + $report.sourceFatalViolations.Count + $report.scriptPolicyViolations.Count
if ($fatalCount -gt 0) {
    Write-Host "[CI Gate] Root layout violation." -ForegroundColor Red
    foreach ($violation in $report.rootViolations + $report.sourceFatalViolations + $report.scriptPolicyViolations) {
        Write-Host " - $($violation.path) :: $($violation.detail)" -ForegroundColor Red
    }
    exit 1
}

if ($report.sourceWarnings.Count -gt 0) {
    Write-Host "[CI Gate] Root layout is code-centric, but source-surface runtime debris was detected:" -ForegroundColor Yellow
    foreach ($warning in $report.sourceWarnings) {
        Write-Host " - $($warning.path) :: $($warning.detail)" -ForegroundColor Yellow
    }
    exit 0
}

Write-Host "[CI Gate] Root layout is code-centric."

