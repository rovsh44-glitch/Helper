Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "scripts\common\Resolve-HelperPaths.ps1")
$libraryPath = Join-Path (Get-HelperPathConfig -WorkspaceRoot $PSScriptRoot).LibraryRoot "docs\programming"
$dirs = Get-ChildItem -Path $libraryPath -Directory
foreach ($dir in $dirs) {
    $files = Get-ChildItem -Path $dir.FullName -File
    if ($files.Count -eq 1) {
        $file = $files[0]
        if ($file.BaseName -eq $dir.Name) {
            Write-Host "Potential redundant folder: $($dir.FullName) contains $($file.Name)"
        }
    }
}
