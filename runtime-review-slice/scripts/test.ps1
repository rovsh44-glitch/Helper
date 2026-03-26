$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest

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
        & $Action
    }
    catch {
        throw "$Name failed. $FailureHint Inner error: $($_.Exception.Message)"
    }
}

$sliceRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$apiProject = Join-Path $sliceRoot 'src\Helper.RuntimeSlice.Api\Helper.RuntimeSlice.Api.csproj'
$testProject = Join-Path $sliceRoot 'test\Helper.RuntimeSlice.Api.Tests\Helper.RuntimeSlice.Api.Tests.csproj'
$packageLock = Join-Path $sliceRoot 'package-lock.json'
$sampleDataRoot = Join-Path $sliceRoot 'sample_data'
$sampleLogsRoot = Join-Path $sampleDataRoot 'logs'

Push-Location $sliceRoot

try {
    Assert-CommandAvailable -Name 'npm' -FailureMessage 'npm is required for the public runtime-review-slice test path. Install Node.js and npm, then rerun scripts/test.ps1.'
    Assert-CommandAvailable -Name 'dotnet' -FailureMessage '.NET 9 SDK is required for the public runtime-review-slice test path. Install the .NET 9 SDK, then rerun scripts/test.ps1.'

    Assert-PathExists -Path $packageLock -FailureMessage 'package-lock.json is required for deterministic Stage 1 frontend dependency installation. Restore or regenerate the lockfile before running scripts/test.ps1.'
    Assert-PathExists -Path $sampleDataRoot -FailureMessage 'The public runtime-review-slice tests are fixture-backed and require the checked-in sample_data/ directory.'
    Assert-PathExists -Path $sampleLogsRoot -FailureMessage 'The public runtime-review-slice tests expect sanitized log fixtures under sample_data/logs/.'
    Assert-PathExists -Path $apiProject -FailureMessage 'The runtime-review-slice API project file is missing.'
    Assert-PathExists -Path $testProject -FailureMessage 'The runtime-review-slice test project file is missing.'

    Invoke-Step -Name 'Install locked frontend dependencies' -FailureHint 'scripts/test.ps1 uses npm ci so the Stage 1 frontend dependency tree matches package-lock.json. Verify npm registry access and the committed lockfile.' -Action {
        npm ci
    }

    Invoke-Step -Name 'Restore .NET test dependencies' -FailureHint 'The public test path restores the xUnit test project first so referenced slice projects resolve cleanly on a new machine.' -Action {
        dotnet restore $testProject
    }

    Invoke-Step -Name 'Build frontend bundle' -FailureHint 'The Vite build should succeed against the locked dependency tree. If it fails, review the local Node.js toolchain and frontend source errors.' -Action {
        npm run build
    }

    Invoke-Step -Name 'Build API project' -FailureHint 'The public slice API must compile before the Stage 1 proof path can continue.' -Action {
        dotnet build $apiProject -c Debug -m:1 --no-restore
    }

    Invoke-Step -Name 'Build slice test project' -FailureHint 'The test assembly must be built on the current machine before dotnet test --no-build can run deterministically.' -Action {
        dotnet build $testProject -c Debug -m:1 --no-restore
    }

    Invoke-Step -Name 'Run xUnit slice tests' -FailureHint 'The public tests are fixture-backed and depend on the checked-in sanitized sample_data/ tree. Review sample_data/ if this step fails because fixtures are missing or invalid.' -Action {
        dotnet test $testProject -c Debug --no-build --logger 'console;verbosity=minimal'
    }

    Write-Host ""
    Write-Host 'Stage 1 runtime-review-slice test path completed successfully.'
    Write-Host 'Fixture assumption: this test path uses the checked-in sanitized sample_data/ tree under runtime-review-slice/.'
}
finally {
    Pop-Location
}
