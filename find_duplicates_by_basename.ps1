Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "scripts\common\Resolve-HelperPaths.ps1")
$libraryPath = Join-Path (Get-HelperPathConfig -WorkspaceRoot $PSScriptRoot).LibraryRoot "docs\programming"
$files = Get-ChildItem -Path $libraryPath -Recurse -File
$baseNameMap = @{}
$duplicates = @()

foreach ($file in $files) {
    if ($baseNameMap.ContainsKey($file.BaseName)) {
        $duplicates += [PSCustomObject]@{
            BaseName = $file.BaseName
            Ext1 = (Get-Item $baseNameMap[$file.BaseName]).Extension
            Ext2 = $file.Extension
            Path1 = $baseNameMap[$file.BaseName]
            Path2 = $file.FullName
        }
    } else {
        $baseNameMap[$file.BaseName] = $file.FullName
    }
}

if ($duplicates.Count -gt 0) {
    $duplicates | Format-Table -AutoSize
} else {
    Write-Host "No files with same base name found."
}
