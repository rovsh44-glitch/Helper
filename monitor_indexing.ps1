
$logFile = "indexing_monitor.log"
while($true) {
    try {
        $date = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        if (Test-Path "indexing_queue.json") {
            $queue = Get-Content indexing_queue.json -Raw | ConvertFrom-Json
            $done = ($queue.PSObject.Properties | Where-Object { $_.Value -eq 'Done' }).Count
            $processing = ($queue.PSObject.Properties | Where-Object { $_.Value -eq 'Processing' }).Count
            $errors = ($queue.PSObject.Properties | Where-Object { $_.Value -match 'Error' }).Count
        } else {
            $done = $processing = $errors = 0
        }
        
        $vram = (nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits)
        $backendRunning = if (Get-Process -Name "dotnet" -ErrorAction SilentlyContinue) { "Alive" } else { "Down" }
        
        $logMsg = "[$date] Progress: $done | Active: $processing | Errors: $errors | VRAM: $vram MB | Backend: $backendRunning"
        $logMsg | Out-File -FilePath $logFile -Append
    } catch {
        "[$date] Monitor Error: $($_.Exception.Message)" | Out-File -FilePath $logFile -Append
    }
    Start-Sleep -Seconds 600
}
