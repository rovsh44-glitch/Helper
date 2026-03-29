param(
    [string]$TestProject = "test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj"
)

$ErrorActionPreference = "Stop"
$env:HELPER_UPDATE_OPENAPI_SNAPSHOT = "true"

Write-Host "[OpenApiRefresh] Updating committed OpenAPI snapshot from runtime source-of-truth..."
dotnet test $TestProject --filter "FullyQualifiedName~ApiSchemaTests"
if ($LASTEXITCODE -ne 0) {
    throw "[OpenApiRefresh] Snapshot refresh failed."
}

Write-Host "[OpenApiRefresh] Snapshot updated."

