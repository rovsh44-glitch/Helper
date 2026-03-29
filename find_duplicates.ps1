Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "scripts\common\Resolve-HelperPaths.ps1")
$libraryPath = Join-Path (Get-HelperPathConfig -WorkspaceRoot $PSScriptRoot).LibraryRoot "docs\programming"
$logFile = "duplicates_found.log"
$hashes = @{}
$duplicateCount = 0
if (Test-Path $logFile) { Remove-Item $logFile }
Write-Host "Scanning for duplicates in $libraryPath..."
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
            $msg = "DUPLICATE: $($file.FullName) == $($hashes[$hash])"
            Write-Host $msg
            $msg | Out-File -FilePath $logFile -Append
        } else {
            $hashes[$hash] = $file.FullName
        }
    } catch {
        Write-Host "Error processing $($file.FullName): $($_.Exception.Message)"
    }
}
$sha256.Dispose()
Write-Host "Duplicates found: $duplicateCount"
Write-Host "Scan Done"
