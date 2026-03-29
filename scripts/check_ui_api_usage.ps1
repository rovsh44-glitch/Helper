$ErrorActionPreference = "Stop"

$allowedTransportModules = @(
    "services\\httpClient.ts",
    "services\\generatedApiClient.ts"
)

$allowlistedFiles = @(
    '^services\\generatedApiClient\.ts:\d+:',
    '^services\\httpClient\.ts:\d+:'
)

$matches = rg -n "fetch\(|fetchWithTimeout\(" App.tsx components services -g "*.ts" -g "*.tsx"
$forbidden = @()

foreach ($match in $matches) {
    if ($allowlistedFiles | Where-Object { $match -match $_ }) {
        continue
    }

    # Allowed exception: session bootstrap call in apiConfig.ts
    if ($match -match "^services\\apiConfig.ts:\d+:(.+)$") {
        $line = $Matches[1]
        if ($line -match "/auth/session") {
            continue
        }
    }

    $forbidden += $match
}

if ($forbidden) {
    Write-Host "[APIGate] Forbidden transport usage detected outside sanctioned API modules:"
    $forbidden | ForEach-Object { Write-Host "  $_" }
    $allowedText = $allowedTransportModules -join ", "
    throw "Browser transport must stay inside: $allowedText. Session bootstrap is the only allowed exception in services/apiConfig.ts."
}

 $generatedClientImports = rg -n "generatedApiClient" components hooks contexts -g "*.ts" -g "*.tsx"
if ($generatedClientImports) {
    Write-Host "[APIGate] Generated client boundary violated in UI composition layers:"
    $generatedClientImports | ForEach-Object { Write-Host "  $_" }
    throw "Components/hooks/contexts must consume handwritten service wrappers instead of importing generatedApiClient directly."
}

 $directHelperApiUsage = rg -n "helperApi\." components hooks contexts -g "*.ts" -g "*.tsx"
if ($directHelperApiUsage) {
    Write-Host "[APIGate] Direct helperApi usage detected outside services:"
    $directHelperApiUsage | ForEach-Object { Write-Host "  $_" }
    throw "Direct helperApi usage is restricted to services/* wrappers."
}

Write-Host "[APIGate] Passed. Browser transport stays inside sanctioned API modules."
