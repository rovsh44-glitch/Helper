param(
    [Alias("Solution", "Target")]
    [string]$ProjectPath = "test\Helper.Runtime.Eval.Tests\Helper.Runtime.Eval.Tests.csproj",
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "common\StrictDotnetFilteredTest.ps1")

Write-Host "[OfflineEval] Running extended offline benchmark..."
$result = Invoke-StrictDotnetFilteredTest `
    -ProjectPath $ProjectPath `
    -Filter "Category=EvalOffline" `
    -Configuration $Configuration `
    -NoBuild:$NoBuild `
    -NoRestore:$NoRestore

Assert-StrictDotnetFilteredTestSucceeded -Result $result -FailurePrefix "[OfflineEval]"
Write-Host "[OfflineEval] Passed."

