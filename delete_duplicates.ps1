Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "scripts\common\Resolve-HelperPaths.ps1")
$libraryPath = Join-Path (Get-HelperPathConfig -WorkspaceRoot $PSScriptRoot).LibraryRoot "docs\programming"
$logFile = "deleted_duplicates.log"
$hashes = @{}
$duplicateCount = 0
$totalSavedSpace = 0

$files = Get-ChildItem -Path $libraryPath -Recurse -File
$sha256 = [System.Security.Cryptography.SHA256]::Create()

foreach ($file in $files) {
    try {
        $stream = [System.IO.File]::OpenRead($file.FullName)
        $hashBytes = $sha256.ComputeHash($stream)
        $stream.Close()
        $hash = [System.BitConverter]::ToString($hashBytes).Replace("-", "")
        
        if ($hashes.ContainsKey($hash)) {
            $duplicateCount++
            $totalSavedSpace += $file.Length
            Remove-Item -Path $file.FullName -Force
            "DELETED: $($file.FullName)" | Out-File -FilePath $logFile -Append
        } else {
            $hashes[$hash] = $file.FullName
        }
    } catch {}
}
$sha256.Dispose()
$totalSizeGB = [math]::Round($totalSavedSpace / 1GB, 2)
Write-Host "Duplicates deleted: $duplicateCount. Space saved: $totalSizeGB GB."
