$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true

function Assert-CommandAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw $FailureMessage
    }
}

function Assert-PathExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    if (-not (Test-Path $Path)) {
        throw $FailureMessage
    }
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$FailureHint,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Name"

    try {
        $global:LASTEXITCODE = 0
        & $Action
        if ($global:LASTEXITCODE -ne 0) {
            throw "Native command exited with code $global:LASTEXITCODE."
        }
    }
    catch {
        throw "$Name failed. $FailureHint Inner error: $($_.Exception.Message)"
    }
}

$sliceRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$apiProject = Join-Path $sliceRoot 'src\Helper.RuntimeSlice.Api\Helper.RuntimeSlice.Api.csproj'
$packageLock = Join-Path $sliceRoot 'package-lock.json'
$sampleDataRoot = Join-Path $sliceRoot 'sample_data'
$sampleLogsRoot = Join-Path $sampleDataRoot 'logs'
$nodeModules = Join-Path $sliceRoot 'node_modules'

Push-Location $sliceRoot

try {
    Assert-CommandAvailable -Name 'npm' -FailureMessage 'npm is required for the public runtime-review-slice start path. Install Node.js and npm, then rerun scripts/start.ps1.'
    Assert-CommandAvailable -Name 'dotnet' -FailureMessage '.NET 9 SDK is required for the public runtime-review-slice start path. Install the .NET 9 SDK, then rerun scripts/start.ps1.'

    Assert-PathExists -Path $packageLock -FailureMessage 'package-lock.json is required for deterministic Stage 1 frontend dependency installation.'
    Assert-PathExists -Path $sampleDataRoot -FailureMessage 'The public runtime-review-slice host is fixture-backed and requires the checked-in sample_data/ directory.'
    Assert-PathExists -Path $sampleLogsRoot -FailureMessage 'The public runtime-review-slice host expects sanitized log fixtures under sample_data/logs/.'
    Assert-PathExists -Path $apiProject -FailureMessage 'The runtime-review-slice API project file is missing.'

    if (-not (Test-Path $nodeModules)) {
        Invoke-Step -Name 'Install locked frontend dependencies' -FailureHint 'scripts/start.ps1 bootstraps the public frontend dependency tree on a clean machine. Verify npm registry access and the committed lockfile.' -Action {
            npm ci
        }
    }

    Invoke-Step -Name 'Build frontend bundle' -FailureHint 'The Vite build must succeed before the public runtime-review-slice host starts.' -Action {
        npm run build
    }

    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5076'

    Invoke-Step -Name 'Start slice API host' -FailureHint 'The runtime-review-slice API host must start after the frontend bundle has been built successfully.' -Action {
        dotnet run --project $apiProject
    }
}
finally {
    Pop-Location
}
