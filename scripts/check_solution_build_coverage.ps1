param(
    [string]$SolutionPath = "Helper.sln",
    [string]$ReportPath = "",
    [string[]]$RequiredConfigurations = @("Debug|Any CPU", "Release|Any CPU"),
    [string]$ExclusionsPath = "scripts/config/solution-project-exclusions.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedSolutionPath = if ([System.IO.Path]::IsPathRooted($SolutionPath)) {
    $SolutionPath
}
else {
    Join-Path $repoRoot $SolutionPath
}
$workspaceRoot = Split-Path -Parent $resolvedSolutionPath

if (-not (Test-Path -LiteralPath $resolvedSolutionPath)) {
    throw "[SolutionCoverage] Solution not found: $SolutionPath"
}

function Normalize-RelativeProjectPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    return $Path.Replace('\', '/').TrimStart([char[]]@('.', '/'))
}

function Resolve-OptionalRepoPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $workspaceRoot $Path
}

$solutionLines = Get-Content -Path $resolvedSolutionPath
$projects = New-Object System.Collections.Generic.List[object]
$buildCoverage = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.HashSet[string]]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($line in $solutionLines) {
    if ($line -match '^Project\("\{[^}]+\}"\) = "([^"]+)", "([^"]+\.csproj)", "\{([^}]+)\}"') {
        $projects.Add([PSCustomObject]@{
                Name = $matches[1]
                RelativePath = $matches[2].Replace('\', '/')
                Guid = $matches[3].ToUpperInvariant()
            })
        continue
    }

    if ($line -match '^\s*\{([^}]+)\}\.([^\.]+)\.Build\.0 = ') {
        $guid = $matches[1].ToUpperInvariant()
        $configuration = $matches[2]
        if (-not $buildCoverage.ContainsKey($guid)) {
            $buildCoverage[$guid] = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        }

        [void]$buildCoverage[$guid].Add($configuration)
    }
}

$repositoryProjects = New-Object System.Collections.Generic.List[string]
foreach ($projectRoot in @("src", "test")) {
    $resolvedProjectRoot = Join-Path $workspaceRoot $projectRoot
    if (-not (Test-Path -LiteralPath $resolvedProjectRoot)) {
        continue
    }

    Get-ChildItem -Path $resolvedProjectRoot -Filter *.csproj -Recurse -File |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($workspaceRoot.Length).TrimStart([char[]]@('\', '/'))
            $repositoryProjects.Add((Normalize-RelativeProjectPath -Path $relativePath))
        }
}

$resolvedExclusionsPath = Resolve-OptionalRepoPath -Path $ExclusionsPath
$excludedProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
if ($null -ne $resolvedExclusionsPath -and (Test-Path -LiteralPath $resolvedExclusionsPath)) {
    $rawExclusions = Get-Content -Path $resolvedExclusionsPath -Raw
    if (-not [string]::IsNullOrWhiteSpace($rawExclusions)) {
        $parsedExclusions = $rawExclusions | ConvertFrom-Json
        foreach ($entry in @($parsedExclusions.excludedProjects)) {
            $entryPath = $entry.path
            if (-not [string]::IsNullOrWhiteSpace($entryPath)) {
                [void]$excludedProjects.Add((Normalize-RelativeProjectPath -Path $entryPath))
            }
        }
    }
}

$solutionProjectPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($project in $projects) {
    [void]$solutionProjectPaths.Add((Normalize-RelativeProjectPath -Path $project.RelativePath))
}

$uncoveredProjects = New-Object System.Collections.Generic.List[string]
$uniqueRepositoryProjects = @($repositoryProjects | Sort-Object -Unique)
foreach ($repositoryProject in $uniqueRepositoryProjects) {
    if ($solutionProjectPaths.Contains($repositoryProject) -or $excludedProjects.Contains($repositoryProject)) {
        continue
    }

    $uncoveredProjects.Add($repositoryProject)
}

$missingCoverage = New-Object System.Collections.Generic.List[object]
foreach ($project in $projects) {
    foreach ($configuration in $RequiredConfigurations) {
        $hasCoverage = $buildCoverage.ContainsKey($project.Guid) -and $buildCoverage[$project.Guid].Contains($configuration)
        if (-not $hasCoverage) {
            $missingCoverage.Add([PSCustomObject]@{
                    Name = $project.Name
                    Path = $project.RelativePath
                    Guid = $project.Guid
                    Configuration = $configuration
                })
        }
    }
}

$missingCoverageArray = @($missingCoverage | ForEach-Object { $_ })
$report = New-Object psobject -Property @{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    solutionPath = $resolvedSolutionPath
    exclusionsPath = $resolvedExclusionsPath
    repositoryProjectCount = [int]$uniqueRepositoryProjects.Count
    projectCount = [int]$projects.Count
    excludedProjectCount = [int]$excludedProjects.Count
    requiredConfigurations = [string[]]$RequiredConfigurations
    uncoveredProjects = [string[]]@($uncoveredProjects)
    uncoveredProjectCount = [int]$uncoveredProjects.Count
    missingCoverage = [object[]]$missingCoverageArray
    missingCoverageCount = [int]$missingCoverage.Count
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $resolvedReportPath = if ([System.IO.Path]::IsPathRooted($ReportPath)) {
        $ReportPath
    }
    else {
        Join-Path $workspaceRoot $ReportPath
    }

    $reportDirectory = Split-Path -Parent $resolvedReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }

    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $resolvedReportPath -Encoding UTF8
}

if ($uncoveredProjects.Count -gt 0) {
    Write-Host "[SolutionCoverage] Repository projects missing from solution or explicit exclusion policy:" -ForegroundColor Red
    $uncoveredProjects |
        Sort-Object |
        ForEach-Object { Write-Host (" - {0}" -f $_) -ForegroundColor Red }
}

if ($missingCoverage.Count -gt 0) {
    Write-Host "[SolutionCoverage] Missing solution build coverage:" -ForegroundColor Red
    $missingCoverage |
        Sort-Object Name, Configuration |
        ForEach-Object { Write-Host (" - {0} [{1}] missing Build.0 for {2}" -f $_.Path, $_.Guid, $_.Configuration) -ForegroundColor Red }
}

if ($uncoveredProjects.Count -gt 0 -or $missingCoverage.Count -gt 0) {
    exit 1
}

Write-Host ("[SolutionCoverage] Passed. {0} solution projects cover {1} repository projects with Build.0 entries for {2}." -f $projects.Count, $uniqueRepositoryProjects.Count, ($RequiredConfigurations -join ", ")) -ForegroundColor Green
