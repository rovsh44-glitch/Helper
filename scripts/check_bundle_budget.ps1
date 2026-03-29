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

if ($null -eq $mainJs) {
    throw "Bundle gate could not find main JS chunk."
}

if ($mainJs.Length -gt [int]$budgets.bundle.mainJsBytes) {
    throw "Main JS chunk exceeds budget: $($mainJs.Length) bytes > $($budgets.bundle.mainJsBytes)."
}

if ($null -ne $mainCss -and $mainCss.Length -gt [int]$budgets.bundle.mainCssBytes) {
    throw "Main CSS chunk exceeds budget: $($mainCss.Length) bytes > $($budgets.bundle.mainCssBytes)."
}

$oversizedLazy = @($lazyChunks | Where-Object { $_.Length -gt [int]$budgets.bundle.lazyChunkBytes })
if ($oversizedLazy.Count -gt 0) {
    $details = $oversizedLazy | ForEach-Object { "$($_.Name)=$($_.Length)" }
    throw "Lazy chunk budget exceeded: $($details -join ', ')."
}

Write-Host "[CI Gate] Bundle budget passed."
