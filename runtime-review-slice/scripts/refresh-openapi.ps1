$ErrorActionPreference = 'Stop'

$sliceRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$outputPath = Join-Path $sliceRoot 'openapi\runtime-review-openapi.json'

Push-Location $sliceRoot

try {
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5076'
    $payload = dotnet run --project src\Helper.RuntimeSlice.Api\Helper.RuntimeSlice.Api.csproj -- --print-openapi
    if (-not $payload) {
        throw 'OpenAPI generation returned empty output.'
    }

    Set-Content -Path $outputPath -Value $payload -Encoding UTF8
}
finally {
    Pop-Location
}
