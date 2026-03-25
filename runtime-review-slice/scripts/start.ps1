$ErrorActionPreference = 'Stop'

$sliceRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $sliceRoot

try {
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5076'
    npm run build
    dotnet run --project src\Helper.RuntimeSlice.Api\Helper.RuntimeSlice.Api.csproj
}
finally {
    Pop-Location
}
