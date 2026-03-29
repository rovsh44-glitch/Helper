param(
    [Alias("Solution")]
    [string]$TestTarget = "test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj",
    [string]$HumanLikePackageRoot = "eval/human_like_communication",
    [string]$HumanLikeOutputRoot = "artifacts/eval/human_like_communication",
    [int]$HumanLikePreparedRuns = 240,
    [string]$WebResearchPackageRoot = "eval/web_research_parity",
    [string]$WebResearchOutputRoot = "artifacts/eval/web_research_parity",
    [int]$WebResearchPreparedRuns = 240
)

$ErrorActionPreference = "Stop"

Write-Host "[EvalRunnerV2] Scope=product_quality_closure_only. Model-side experiments are governed separately under doc/research/* and are out of scope for this runner."

Write-Host "[EvalRunnerV2] Building eval targets..."
dotnet build $TestTarget -c Debug -m:1
if ($LASTEXITCODE -ne 0) {
    throw "[EvalRunnerV2] Test target build failed."
}

dotnet build src\Helper.Api\Helper.Api.csproj -c Debug -m:1
if ($LASTEXITCODE -ne 0) {
    throw "[EvalRunnerV2] API export target build failed."
}

Write-Host "[EvalRunnerV2] Running data-driven corpus preparation tests against $TestTarget..."
dotnet test $TestTarget --no-build --filter "Category=EvalV2"
if ($LASTEXITCODE -ne 0) {
    throw "[EvalRunnerV2] Gate failed."
}

Write-Host "[EvalRunnerV2] Passed."

Write-Host "[EvalRunnerV2] Exporting human-like communication eval package..."
dotnet run --project src\Helper.Api\Helper.Api.csproj --no-build --no-restore --no-launch-profile -- --export-human-like-eval $HumanLikePackageRoot $HumanLikeOutputRoot $HumanLikePreparedRuns
if ($LASTEXITCODE -ne 0) {
    throw "[EvalRunnerV2] Human-like communication eval export failed."
}

Write-Host "[EvalRunnerV2] Human-like communication export passed."

Write-Host "[EvalRunnerV2] Exporting web-research parity eval package..."
dotnet run --project src\Helper.Api\Helper.Api.csproj --no-build --no-restore --no-launch-profile -- --export-web-research-eval $WebResearchPackageRoot $WebResearchOutputRoot $WebResearchPreparedRuns
if ($LASTEXITCODE -ne 0) {
    throw "[EvalRunnerV2] Web-research parity eval export failed."
}

Write-Host "[EvalRunnerV2] Web-research parity export passed."

