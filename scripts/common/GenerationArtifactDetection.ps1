Set-StrictMode -Version Latest

function Get-GenerationArtifactProjectRoots {
    param(
        [Parameter(Mandatory = $true)][string]$WorkspaceRoot,
        [Parameter(Mandatory = $true)]$PathConfig,
        [switch]$IncludeLegacyProjectsRoot
    )

    $roots = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($PathConfig.ProjectsRoot)) {
        $roots.Add($PathConfig.ProjectsRoot) | Out-Null
    }

    if ($IncludeLegacyProjectsRoot.IsPresent) {
        $legacyProjectsRoot = Join-Path $WorkspaceRoot "PROJECTS"
        if (-not [string]::IsNullOrWhiteSpace($legacyProjectsRoot)) {
            $roots.Add($legacyProjectsRoot) | Out-Null
        }
    }

    return @($roots | Select-Object -Unique)
}

function Find-LatestValidationReport {
    param([Parameter(Mandatory = $true)][string[]]$ProjectsRoots)

    return $ProjectsRoots |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } |
        ForEach-Object { Get-ChildItem -Path $_ -Filter "validation_report.json" -Recurse -File } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Find-ValidationReportForRun {
    param(
        [Parameter(Mandatory = $true)][string[]]$ProjectsRoots,
        [Parameter(Mandatory = $true)][datetime]$RunStartedUtc,
        [string]$PreviousLatestPath
    )

    $newerCandidates = $ProjectsRoots |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } |
        ForEach-Object { Get-ChildItem -Path $_ -Filter "validation_report.json" -Recurse -File } |
        Where-Object { $_.LastWriteTimeUtc -ge $RunStartedUtc.AddSeconds(-1) } |
        Sort-Object LastWriteTimeUtc -Descending
    if ($newerCandidates) {
        return $newerCandidates | Select-Object -First 1
    }

    $latest = Find-LatestValidationReport -ProjectsRoots $ProjectsRoots
    if ($null -eq $latest) {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($PreviousLatestPath) -and
        [string]::Equals($latest.FullName, $PreviousLatestPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    return $latest
}
