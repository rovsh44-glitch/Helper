$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
Push-Location $repoRoot

try {
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5076'
    npm run runtime-slice:build
    dotnet run --project src\Helper.RuntimeSlice.Api\Helper.RuntimeSlice.Api.csproj
}
finally {
    Pop-Location
}
