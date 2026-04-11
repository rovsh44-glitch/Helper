param(
    [string]$Configuration = "Debug",
    [string]$EvalTarget = "test\Helper.Runtime.Eval.Tests\Helper.Runtime.Eval.Tests.csproj",
    [string]$EvalV2Target = "test\Helper.Runtime.Eval.Tests\Helper.Runtime.Eval.Tests.csproj",
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "common\StrictDotnetFilteredTest.ps1")

foreach ($target in @($EvalTarget, $EvalV2Target)) {
    if (-not (Test-Path -LiteralPath (Resolve-HelperRepoPath -Path $target))) {
        throw "[EvalGate] Test target not found: $target"
    }
}

function Invoke-EvalCategory {
    param(
        [string]$Target,
        [string]$Category,
        [string]$FailureMessage
    )

    $result = Invoke-StrictDotnetFilteredTest `
        -ProjectPath $Target `
        -Filter ("Category=" + $Category) `
        -Configuration $Configuration `
        -NoBuild:$NoBuild `
        -NoRestore:$NoRestore
    Assert-StrictDotnetFilteredTestSucceeded -Result $result -FailurePrefix $FailureMessage
}

Write-Host "[EvalGate] Running evaluation tests on $EvalTarget..."
Invoke-EvalCategory -Target $EvalTarget -Category "Eval" -FailureMessage "[EvalGate] Evaluation gate failed."

Write-Host "[EvalGate] Running data-driven EvalRunnerV2 tests on $EvalV2Target..."
Invoke-EvalCategory -Target $EvalV2Target -Category "EvalV2" -FailureMessage "[EvalGate] EvalRunnerV2 gate failed."

Write-Host "[EvalGate] Passed."

