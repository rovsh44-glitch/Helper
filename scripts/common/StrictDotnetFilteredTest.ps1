$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-HelperRepoRootFromCommonScript {
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
}

function Resolve-HelperRepoPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-HelperRepoRootFromCommonScript) $Path))
}

function Invoke-StrictDotnetFilteredTest {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Filter,
        [string]$Configuration = "Debug",
        [switch]$NoBuild,
        [switch]$NoRestore,
        [string[]]$AdditionalArgs = @()
    )

    $resolvedProjectPath = Resolve-HelperRepoPath -Path $ProjectPath
    if (-not (Test-Path -LiteralPath $resolvedProjectPath)) {
        throw "[StrictDotnetTest] Project not found: $resolvedProjectPath"
    }

    $arguments = @(
        "test",
        $resolvedProjectPath,
        "-c", $Configuration,
        "-m:1",
        "--filter", $Filter,
        "-v", "minimal"
    )

    if ($NoBuild) {
        $arguments += "--no-build"
    }

    if ($NoRestore) {
        $arguments += "--no-restore"
    }

    if ($AdditionalArgs.Count -gt 0) {
        $arguments += $AdditionalArgs
    }

    $commandSegments = @(
        $arguments | ForEach-Object {
            if ([string]$_ -match '\s') {
                '"' + $_ + '"'
            }
            else {
                [string]$_
            }
        }
    )
    $commandDisplay = "dotnet " + ($commandSegments -join " ")

    Write-Host ("[StrictDotnetTest] {0}" -f $commandDisplay)
    $output = @(& dotnet @arguments 2>&1)
    if ($output.Count -gt 0) {
        $output | Out-Host
    }

    $exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
    $outputText = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    $noTestsMatched = $outputText -match "No test matches the given testcase filter"
    $missingTestSource = $outputText -match "The test source file .* was not found"

    return [pscustomobject]@{
        ProjectPath = $resolvedProjectPath
        Filter = $Filter
        Configuration = $Configuration
        CommandDisplay = $commandDisplay
        ExitCode = $exitCode
        OutputText = $outputText
        NoTestsMatched = $noTestsMatched
        MissingTestSource = $missingTestSource
        Succeeded = ($exitCode -eq 0) -and (-not $noTestsMatched) -and (-not $missingTestSource)
    }
}

function Assert-StrictDotnetFilteredTestSucceeded {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [string]$FailurePrefix = "[StrictDotnetTest]"
    )

    if ($Result.MissingTestSource) {
        throw ("{0} Test source was not built or was missing for project '{1}'." -f $FailurePrefix, $Result.ProjectPath)
    }

    if ($Result.NoTestsMatched) {
        throw ("{0} Filter '{1}' matched no tests in '{2}'." -f $FailurePrefix, $Result.Filter, $Result.ProjectPath)
    }

    if ($Result.ExitCode -ne 0) {
        throw ("{0} dotnet test failed with exit code {1}." -f $FailurePrefix, $Result.ExitCode)
    }
}
