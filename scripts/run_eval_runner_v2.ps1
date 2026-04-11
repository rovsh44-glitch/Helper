param(
    [Alias("Solution")]
    [string]$TestTarget = "test/Helper.Runtime.Eval.Tests/Helper.Runtime.Eval.Tests.csproj",
    [string]$HumanLikePackageRoot = "eval/human_like_communication",
    [string]$HumanLikeOutputRoot = "artifacts/eval/human_like_communication",
    [int]$HumanLikePreparedRuns = 240,
    [string]$WebResearchPackageRoot = "eval/web_research_parity",
    [string]$WebResearchOutputRoot = "artifacts/eval/web_research_parity",
    [int]$WebResearchPreparedRuns = 240
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "common\StrictDotnetFilteredTest.ps1")

$resolvedTestTarget = Resolve-HelperRepoPath -Path $TestTarget
$resolvedApiProject = Resolve-HelperRepoPath -Path "src\Helper.Api\Helper.Api.csproj"
$resolvedHumanLikePackageRoot = Resolve-HelperRepoPath -Path $HumanLikePackageRoot
$resolvedHumanLikeOutputRoot = Resolve-HelperRepoPath -Path $HumanLikeOutputRoot
$resolvedWebResearchPackageRoot = Resolve-HelperRepoPath -Path $WebResearchPackageRoot
$resolvedWebResearchOutputRoot = Resolve-HelperRepoPath -Path $WebResearchOutputRoot

Write-Host "[EvalRunnerV2] Scope=product_quality_closure_only. Model-side experiments are governed separately under doc/research/* and are out of scope for this runner."

Write-Host "[EvalRunnerV2] Building eval targets..."
dotnet build $resolvedTestTarget -c Debug -m:1
if ($LASTEXITCODE -ne 0) {
    throw "[EvalRunnerV2] Test target build failed."
}

dotnet build $resolvedApiProject -c Debug -m:1
if ($LASTEXITCODE -ne 0) {
    throw "[EvalRunnerV2] API export target build failed."
}

Write-Host "[EvalRunnerV2] Running data-driven corpus preparation tests against $resolvedTestTarget..."
$result = Invoke-StrictDotnetFilteredTest `
    -ProjectPath $resolvedTestTarget `
    -Filter "Category=EvalV2" `
    -Configuration "Debug" `
    -NoBuild `
    -NoRestore
Assert-StrictDotnetFilteredTestSucceeded -Result $result -FailurePrefix "[EvalRunnerV2]"

Write-Host "[EvalRunnerV2] Passed."

Write-Host "[EvalRunnerV2] Exporting human-like communication eval package..."
dotnet run --project $resolvedApiProject --no-build --no-restore --no-launch-profile -- --export-human-like-eval $resolvedHumanLikePackageRoot $resolvedHumanLikeOutputRoot $HumanLikePreparedRuns
if ($LASTEXITCODE -ne 0) {
    throw "[EvalRunnerV2] Human-like communication eval export failed."
}

Write-Host "[EvalRunnerV2] Human-like communication export passed."

Write-Host "[EvalRunnerV2] Exporting web-research parity eval package..."
dotnet run --project $resolvedApiProject --no-build --no-restore --no-launch-profile -- --export-web-research-eval $resolvedWebResearchPackageRoot $resolvedWebResearchOutputRoot $WebResearchPreparedRuns
if ($LASTEXITCODE -ne 0) {
    throw "[EvalRunnerV2] Web-research parity eval export failed."
}

Write-Host "[EvalRunnerV2] Web-research parity export passed."

