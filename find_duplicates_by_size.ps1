Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "scripts\common\Resolve-HelperPaths.ps1")
$libraryPath = Join-Path (Get-HelperPathConfig -WorkspaceRoot $PSScriptRoot).LibraryRoot "docs\programming"
$files = Get-ChildItem -Path $libraryPath -Recurse -File
$sizeMap = @{}
$duplicates = @()

foreach ($file in $files) {
    if ($sizeMap.ContainsKey($file.Length)) {
        $duplicates += [PSCustomObject]@{
            Size = $file.Length
            Path1 = $sizeMap[$file.Length]
            Path2 = $file.FullName
        }
    } else {
        $sizeMap[$file.Length] = $file.FullName
    }
}

if ($duplicates.Count -gt 0) {
    $duplicates | Format-Table -AutoSize
} else {
    Write-Host "No files with same size found."
}
