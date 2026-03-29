$ErrorActionPreference = "Stop"
Write-Host "[LoadChaos] Running load/chaos suite..."
powershell -ExecutionPolicy Bypass -File scripts/load_streaming_chaos.ps1
Write-Host "[LoadChaos] Passed."
