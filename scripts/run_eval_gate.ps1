param(
    [Alias("Solution")]
    [string]$Target = "test\\Helper.Runtime.Tests\\Helper.Runtime.Tests.csproj"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $Target)) {
    throw "[EvalGate] Test target not found: $Target"
}

function Invoke-EvalCategory {
    param(
        [string]$Category,
        [string]$FailureMessage
    )

    $global:LASTEXITCODE = 0
    dotnet test $Target --no-build --filter "Category=$Category" -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

Write-Host "[EvalGate] Running evaluation tests on $Target..."
Invoke-EvalCategory -Category "Eval" -FailureMessage "[EvalGate] Evaluation gate failed."

Write-Host "[EvalGate] Running data-driven EvalRunnerV2 tests..."
Invoke-EvalCategory -Category "EvalV2" -FailureMessage "[EvalGate] EvalRunnerV2 gate failed."

Write-Host "[EvalGate] Passed."

