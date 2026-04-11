param(
    [string]$SolutionPath = "Helper.sln",
    [string]$ReportPath = "",
    [string[]]$RequiredConfigurations = @("Debug|Any CPU", "Release|Any CPU")
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

if (-not (Test-Path -LiteralPath $resolvedSolutionPath)) {
    throw "[SolutionCoverage] Solution not found: $SolutionPath"
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
    projectCount = [int]$projects.Count
    requiredConfigurations = [string[]]$RequiredConfigurations
    missingCoverage = [object[]]$missingCoverageArray
    missingCoverageCount = [int]$missingCoverage.Count
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $resolvedReportPath = if ([System.IO.Path]::IsPathRooted($ReportPath)) {
        $ReportPath
    }
    else {
        Join-Path $repoRoot $ReportPath
    }

    $reportDirectory = Split-Path -Parent $resolvedReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }

    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $resolvedReportPath -Encoding UTF8
}

if ($missingCoverage.Count -gt 0) {
    Write-Host "[SolutionCoverage] Missing solution build coverage:" -ForegroundColor Red
    $missingCoverage |
        Sort-Object Name, Configuration |
        ForEach-Object { Write-Host (" - {0} [{1}] missing Build.0 for {2}" -f $_.Path, $_.Guid, $_.Configuration) -ForegroundColor Red }
    exit 1
}

Write-Host ("[SolutionCoverage] Passed. {0} solution projects have Build.0 coverage for {1}." -f $projects.Count, ($RequiredConfigurations -join ", ")) -ForegroundColor Green
