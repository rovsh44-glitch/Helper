Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "scripts\common\Resolve-HelperPaths.ps1")
$libraryPath = Join-Path (Get-HelperPathConfig -WorkspaceRoot $PSScriptRoot).LibraryRoot "docs\programming"
$files = Get-ChildItem -Path $libraryPath -Recurse -File
$nameMap = @{}
$duplicates = @()

foreach ($file in $files) {
    if ($nameMap.ContainsKey($file.Name)) {
        $duplicates += [PSCustomObject]@{
            Name = $file.Name
            Path1 = $nameMap[$file.Name]
            Path2 = $file.FullName
            Size1 = (Get-Item $nameMap[$file.Name]).Length
            Size2 = $file.Length
        }
    } else {
        $nameMap[$file.Name] = $file.FullName
    }
}

if ($duplicates.Count -gt 0) {
    $duplicates | Format-Table -AutoSize
} else {
    Write-Host "No files with same name found."
}
