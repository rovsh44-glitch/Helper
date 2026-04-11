param(
    [string]$DistPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($DistPath)) {
    $DistPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")) "dist"
}

$budgets = Get-Content (Join-Path $PSScriptRoot "performance_budgets.json") -Raw | ConvertFrom-Json

if (-not (Test-Path $DistPath)) {
    throw "Dist path '$DistPath' does not exist. Run frontend build before bundle gate."
}

$assetsPath = Join-Path $DistPath "assets"
$mainJs = Get-ChildItem $assetsPath -Filter "index-*.js" -File | Sort-Object Length -Descending | Select-Object -First 1
$mainCss = Get-ChildItem $assetsPath -Filter "index-*.css" -File | Sort-Object Length -Descending | Select-Object -First 1
$lazyChunks = Get-ChildItem $assetsPath -Filter "*.js" -File | Where-Object { $_.Name -notlike "index-*.js" }
$appLazyChunkBudget = if ($budgets.bundle.PSObject.Properties.Name -contains "appLazyChunkBytes") {
    [int]$budgets.bundle.appLazyChunkBytes
}
else {
    [int]$budgets.bundle.lazyChunkBytes
}
$vendorChunkBudget = if ($budgets.bundle.PSObject.Properties.Name -contains "vendorChunkBytes") {
    [int]$budgets.bundle.vendorChunkBytes
}
else {
    $appLazyChunkBudget
}

if ($null -eq $mainJs) {
    throw "Bundle gate could not find main JS chunk."
}

if ($mainJs.Length -gt [int]$budgets.bundle.mainJsBytes) {
    throw "Main JS chunk exceeds budget: $($mainJs.Length) bytes > $($budgets.bundle.mainJsBytes)."
}

if ($null -ne $mainCss -and $mainCss.Length -gt [int]$budgets.bundle.mainCssBytes) {
    throw "Main CSS chunk exceeds budget: $($mainCss.Length) bytes > $($budgets.bundle.mainCssBytes)."
}

$vendorLazyChunks = @($lazyChunks | Where-Object { $_.Name -match 'vendor' })
$appLazyChunks = @($lazyChunks | Where-Object { $_.Name -notmatch 'vendor' })
$oversizedAppLazy = @($appLazyChunks | Where-Object { $_.Length -gt $appLazyChunkBudget })
$oversizedVendorLazy = @($vendorLazyChunks | Where-Object { $_.Length -gt $vendorChunkBudget })
if ($oversizedAppLazy.Count -gt 0 -or $oversizedVendorLazy.Count -gt 0) {
    $details = New-Object System.Collections.Generic.List[string]
    foreach ($chunk in $oversizedAppLazy) {
        $details.Add("app:$($chunk.Name)=$($chunk.Length)")
    }

    foreach ($chunk in $oversizedVendorLazy) {
        $details.Add("vendor:$($chunk.Name)=$($chunk.Length)")
    }

    throw "Lazy chunk budget exceeded: $($details -join ', ')."
}

Write-Host "[CI Gate] Bundle budget passed."
