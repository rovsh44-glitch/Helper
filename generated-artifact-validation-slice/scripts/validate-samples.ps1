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
$cliProject = Join-Path $sliceRoot 'src\Helper.GeneratedArtifactValidation.Cli\Helper.GeneratedArtifactValidation.Cli.csproj'

Push-Location $sliceRoot

try {
    Assert-CommandAvailable -Name 'dotnet' -FailureMessage '.NET 9 SDK is required for the generated-artifact-validation-slice sample validation path.'

    if (-not (Test-Path $cliProject)) {
        throw 'The generated-artifact-validation-slice CLI project file is missing.'
    }

    Invoke-Step -Name 'Build Stage 2 CLI project' -FailureHint 'The sample-validation sweep requires a successful CLI build before the checked-in fixtures can be validated.' -Action {
        dotnet build $cliProject -c Debug -m:1
    }

    Invoke-Step -Name 'Run Stage 2 sample validation sweep' -FailureHint 'The CLI sample-validation command must complete successfully against the checked-in public fixture families.' -Action {
        dotnet run --project $cliProject -c Debug --no-build -- samples --root $sliceRoot
    }
}
finally {
    Pop-Location
}
