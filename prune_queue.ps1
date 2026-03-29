Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "scripts\common\Resolve-HelperPaths.ps1")
$queuePath = (Join-Path (Get-HelperPathConfig -WorkspaceRoot $PSScriptRoot).DataRoot "indexing_queue.json")
if (Test-Path $queuePath) {
    $queue = Get-Content $queuePath -Raw | ConvertFrom-Json
    $newQueue = @{}
    $removedCount = 0
    
    foreach ($property in $queue.PSObject.Properties) {
        if (Test-Path $property.Name) {
            $newQueue[$property.Name] = $property.Value
        } else {
            $removedCount++
        }
    }
    
    $newQueue | ConvertTo-Json -Depth 100 | Out-File $queuePath -Encoding UTF8
    Write-Host "Removed $removedCount missing entries from queue."
}
