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

$packageRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$contractsProject = Join-Path $packageRoot 'src\Helper.Generation.Contracts\Helper.Generation.Contracts.csproj'
$testProject = Join-Path $packageRoot 'test\Helper.Generation.Contracts.Tests\Helper.Generation.Contracts.Tests.csproj'

Push-Location $packageRoot

try {
    Assert-CommandAvailable -Name 'dotnet' -FailureMessage '.NET 9 SDK is required for the helper-generation-contracts test path.'
    Assert-PathExists -Path $contractsProject -FailureMessage 'The shared Helper.Generation.Contracts project file is missing.'
    Assert-PathExists -Path $testProject -FailureMessage 'The Helper.Generation.Contracts test project file is missing.'

    Invoke-Step -Name 'Restore Stage 3 test dependencies' -FailureHint 'Restore the shared contracts test project before building or testing the package.' -Action {
        dotnet restore $testProject
    }

    Invoke-Step -Name 'Build Stage 3 shared contracts test project' -FailureHint 'The shared contracts package and test assembly must build cleanly before the Stage 3 proof path can continue.' -Action {
        dotnet build $testProject -c Debug -m:1 --no-restore
    }

    Invoke-Step -Name 'Run Stage 3 shared contracts tests' -FailureHint 'The Stage 3 tests lock the published contract family, JSON shape expectations, and converter behavior.' -Action {
        dotnet test $testProject -c Debug --no-build --logger 'console;verbosity=minimal'
    }

    Write-Host ""
    Write-Host 'Stage 3 helper-generation-contracts test path completed successfully.'
}
finally {
    Pop-Location
}
