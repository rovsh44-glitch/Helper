param(
    [Alias("Solution")]
    [string]$Target = "test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj",
    [switch]$UpdateSnapshot
)

$ErrorActionPreference = "Stop"

if ($UpdateSnapshot) {
    $env:HELPER_UPDATE_OPENAPI_SNAPSHOT = "true"
    Write-Host "[OpenApiGate] Snapshot update mode enabled."
}

Write-Host ("[OpenApiGate] Running contract tests on {0}..." -f $Target)

$arguments = @(
    "test",
    $Target,
    "--no-build",
    "--filter",
    "Category=Contract",
    "--logger",
    "console;verbosity=minimal"
)

dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "[OpenApiGate] Contract gate failed."
}

Write-Host "[OpenApiGate] Passed."

