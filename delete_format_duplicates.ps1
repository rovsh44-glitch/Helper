Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "scripts\common\Resolve-HelperPaths.ps1")
$libraryPath = Join-Path (Get-HelperPathConfig -WorkspaceRoot $PSScriptRoot).LibraryRoot "docs\programming"
$files = Get-ChildItem -Path $libraryPath -Recurse -File
$groups = $files | Group-Object BaseName

foreach ($group in $groups) {
    if ($group.Count -gt 1) {
        $hasPdf = $group.Group | Where-Object { $_.Extension -eq ".pdf" }
        if ($hasPdf) {
            foreach ($file in $group.Group) {
                if ($file.Extension -ne ".pdf") {
                    Write-Host "Deleting duplicate format: $($file.FullName) (keeping PDF)"
                    Remove-Item $file.FullName -Force
                }
            }
        }
    }
}

# Also cleanup redundant folders containing only one file that is now potentially a duplicate
$dirs = Get-ChildItem -Path $libraryPath -Directory
foreach ($dir in $dirs) {
    $remainingFiles = Get-ChildItem -Path $dir.FullName -File
    if ($remainingFiles.Count -eq 0) {
        Write-Host "Deleting empty folder: $($dir.FullName)"
        Remove-Item $dir.FullName -Recurse -Force
    }
}
