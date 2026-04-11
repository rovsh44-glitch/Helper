$ErrorActionPreference = "Stop"

$allowedTransportModules = @(
    "services\\httpClient.ts",
    "services\\generatedApiClient.ts"
)

$allowlistedFiles = @(
    '^services\\generatedApiClient\.ts:\d+:',
    '^services\\httpClient\.ts:\d+:'
)

function Find-UiCodeMatches {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Pattern,

        [Parameter(Mandatory = $true)]
        [string[]]$Paths
    )

    $rg = Get-Command rg -ErrorAction SilentlyContinue
    if ($null -ne $rg) {
        return @(rg -n $Pattern @Paths -g "*.ts" -g "*.tsx")
    }

    $files = foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        $item = Get-Item -LiteralPath $path
        if ($item.PSIsContainer) {
            Get-ChildItem -LiteralPath $path -Recurse -File -Include *.ts,*.tsx
        }
        else {
            $item
        }
    }

    return @(
        $files |
            Select-String -Pattern $Pattern |
            ForEach-Object {
                $relativePath = Resolve-Path -LiteralPath $_.Path -Relative
                if ($relativePath.StartsWith(".\")) {
                    $relativePath = $relativePath.Substring(2)
                }

                "{0}:{1}:{2}" -f $relativePath, $_.LineNumber, $_.Line.Trim()
            }
    )
}

$matches = Find-UiCodeMatches -Pattern "fetch\(|fetchWithTimeout\(" -Paths @("App.tsx", "components", "services")
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

$generatedClientImports = Find-UiCodeMatches -Pattern "generatedApiClient" -Paths @("components", "hooks", "contexts")
if ($generatedClientImports) {
    Write-Host "[APIGate] Generated client boundary violated in UI composition layers:"
    $generatedClientImports | ForEach-Object { Write-Host "  $_" }
    throw "Components/hooks/contexts must consume handwritten service wrappers instead of importing generatedApiClient directly."
}

$directHelperApiUsage = Find-UiCodeMatches -Pattern "helperApi\." -Paths @("components", "hooks", "contexts")
if ($directHelperApiUsage) {
    Write-Host "[APIGate] Direct helperApi usage detected outside services:"
    $directHelperApiUsage | ForEach-Object { Write-Host "  $_" }
    throw "Direct helperApi usage is restricted to services/* wrappers."
}

Write-Host "[APIGate] Passed. Browser transport stays inside sanctioned API modules."
