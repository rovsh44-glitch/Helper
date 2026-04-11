param(
    [Alias("Solution", "Target")]
    [string]$ProjectPath = "test\Helper.Runtime.Certification.Tests\Helper.Runtime.Certification.Tests.csproj",
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "common\StrictDotnetFilteredTest.ps1")

Write-Host "[ToolBenchmark] Running tool-call correctness benchmark..."
$result = Invoke-StrictDotnetFilteredTest `
    -ProjectPath $ProjectPath `
    -Filter "Category=ToolBenchmark" `
    -Configuration $Configuration `
    -NoBuild:$NoBuild `
    -NoRestore:$NoRestore

Assert-StrictDotnetFilteredTestSucceeded -Result $result -FailurePrefix "[ToolBenchmark]"
Write-Host "[ToolBenchmark] Passed."

