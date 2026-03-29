$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
Push-Location $repoRoot

try {
    npm run runtime-slice:build
    dotnet build src\Helper.RuntimeSlice.Api\Helper.RuntimeSlice.Api.csproj -c Debug -m:1
    dotnet test test\Helper.RuntimeSlice.Api.Tests\Helper.RuntimeSlice.Api.Tests.csproj -c Debug --no-build
}
finally {
    Pop-Location
}
