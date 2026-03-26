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
        & $Action
    }
    catch {
        throw "$Name failed. $FailureHint Inner error: $($_.Exception.Message)"
    }
}

$sliceRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$testProject = Join-Path $sliceRoot 'test\Helper.GeneratedArtifactValidation.Tests\Helper.GeneratedArtifactValidation.Tests.csproj'
$sampleFixtureRoot = Join-Path $sliceRoot 'sample_fixtures'

Push-Location $sliceRoot

try {
    Assert-CommandAvailable -Name 'dotnet' -FailureMessage '.NET 9 SDK is required for the generated-artifact-validation-slice test path.'
    Assert-PathExists -Path $testProject -FailureMessage 'The generated-artifact-validation-slice test project file is missing.'
    Assert-PathExists -Path $sampleFixtureRoot -FailureMessage 'The generated-artifact-validation-slice package requires the checked-in sample_fixtures/ directory.'

    Invoke-Step -Name 'Restore .NET test dependencies' -FailureHint 'Restore the Stage 2 test project before building or running the slice tests.' -Action {
        dotnet restore $testProject
    }

    Invoke-Step -Name 'Build Stage 2 test project' -FailureHint 'The Stage 2 slice test assembly must build cleanly before tests can run.' -Action {
        dotnet build $testProject -c Debug -m:1 --no-restore
    }

    Invoke-Step -Name 'Run xUnit slice tests' -FailureHint 'The Stage 2 slice tests cover path, signature, blueprint, artifact, compile-gate, and CLI sample validation workflows.' -Action {
        dotnet test $testProject -c Debug --no-build --logger 'console;verbosity=minimal'
    }

    Invoke-Step -Name 'Run sample validation sweep' -FailureHint 'The checked-in good and bad fixture sets must keep producing the expected PASS/FAIL outcomes for the public slice.' -Action {
        powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'validate-samples.ps1')
    }

    Write-Host ""
    Write-Host 'Stage 2 generated-artifact-validation-slice test path completed successfully.'
}
finally {
    Pop-Location
}
