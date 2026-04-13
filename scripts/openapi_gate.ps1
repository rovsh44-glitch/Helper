param(
    [Alias("Solution")]
    [string]$Target = "test/Helper.Runtime.Api.Tests/Helper.Runtime.Api.Tests.csproj",
    [switch]$UpdateSnapshot,
    [string]$SimulatedOutputPath,
    [int]$SimulatedExitCode = 0
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common/StrictDotnetFilteredTest.ps1")

if ($UpdateSnapshot) {
    $env:HELPER_UPDATE_OPENAPI_SNAPSHOT = "true"
    Write-Host "[OpenApiGate] Snapshot update mode enabled."
}

Write-Host ("[OpenApiGate] Running contract tests on {0}..." -f $Target)

if ([string]::IsNullOrWhiteSpace($SimulatedOutputPath)) {
    $result = Invoke-StrictDotnetFilteredTest `
        -ProjectPath $Target `
        -Filter "Category=Contract" `
        -NoBuild `
        -AdditionalArgs @(
            "--logger",
            "console;verbosity=minimal"
        )
}
else {
    $resolvedProjectPath = Resolve-HelperRepoPath -Path $Target
    if (-not (Test-Path -LiteralPath $resolvedProjectPath)) {
        throw "[OpenApiGate] Project not found: $resolvedProjectPath"
    }

    $resolvedOutputPath = Resolve-HelperRepoPath -Path $SimulatedOutputPath
    if (-not (Test-Path -LiteralPath $resolvedOutputPath)) {
        throw "[OpenApiGate] Simulated output file not found: $resolvedOutputPath"
    }

    $outputText = [System.IO.File]::ReadAllText($resolvedOutputPath)
    Write-Host ("[OpenApiGate] Using simulated output from {0}." -f $resolvedOutputPath)

    $result = [pscustomobject]@{
        ProjectPath = $resolvedProjectPath
        Filter = "Category=Contract"
        Configuration = "Debug"
        CommandDisplay = "dotnet test (simulated)"
        ExitCode = $SimulatedExitCode
        OutputText = $outputText
        NoTestsMatched = ($outputText -match "No test matches the given testcase filter")
        MissingTestSource = ($outputText -match "The test source file .* was not found")
        Succeeded = ($SimulatedExitCode -eq 0) -and
            (-not ($outputText -match "No test matches the given testcase filter")) -and
            (-not ($outputText -match "The test source file .* was not found"))
    }
}

Assert-StrictDotnetFilteredTestSucceeded -Result $result -FailurePrefix "[OpenApiGate]"

Write-Host "[OpenApiGate] Passed."

