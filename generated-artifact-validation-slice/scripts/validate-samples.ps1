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

$sliceRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$cliProject = Join-Path $sliceRoot 'src\Helper.GeneratedArtifactValidation.Cli\Helper.GeneratedArtifactValidation.Cli.csproj'

Push-Location $sliceRoot

try {
    Assert-CommandAvailable -Name 'dotnet' -FailureMessage '.NET 9 SDK is required for the generated-artifact-validation-slice sample validation path.'

    if (-not (Test-Path $cliProject)) {
        throw 'The generated-artifact-validation-slice CLI project file is missing.'
    }

    dotnet build $cliProject -c Debug -m:1
    dotnet run --project $cliProject -c Debug --no-build -- samples --root $sliceRoot
}
finally {
    Pop-Location
}
