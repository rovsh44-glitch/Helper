$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-IsolatedDotnetBuildPlan {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$HostName,
        [string]$Configuration = "Debug"
    )

    $safeHostName = ($HostName -replace '[^A-Za-z0-9_.-]', '_')
    $baseRoot = Join-Path $RepoRoot "temp\$safeHostName"
    $outputRoot = Join-Path $baseRoot ("out\" + $Configuration)

    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

    return [pscustomobject]@{
        HostName = $safeHostName
        BaseRoot = $baseRoot
        OutputRoot = $outputRoot
    }
}

function Invoke-IsolatedDotnetBuild {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$HostName,
        [string]$Configuration = "Debug",
        [string[]]$AdditionalBuildArgs
    )

    $plan = Get-IsolatedDotnetBuildPlan -RepoRoot $RepoRoot -HostName $HostName -Configuration $Configuration
    $arguments = @(
        "build",
        $ProjectPath,
        "-c", $Configuration,
        "-m:1",
        "-clp:ErrorsOnly;Summary",
        "--disable-build-servers",
        "--no-restore",
        "-o", $plan.OutputRoot,
        "-p:NuGetAudit=false"
    )

    if ($AdditionalBuildArgs) {
        $arguments += $AdditionalBuildArgs
    }

    Push-Location $RepoRoot
    try {
        & dotnet @arguments | Out-Host
        $exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    }
    finally {
        Pop-Location
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        OutputRoot = $plan.OutputRoot
    }
}
