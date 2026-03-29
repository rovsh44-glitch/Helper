param(
    [int]$WaitTimeoutSec = 15,
    [int]$PollIntervalMs = 200
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$targetPids = New-Object System.Collections.Generic.HashSet[int]

Get-Process Helper.Runtime.Cli -ErrorAction SilentlyContinue |
    ForEach-Object { [void]$targetPids.Add($_.Id) }

try {
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction Stop |
        Where-Object { $_.CommandLine -match '(?i)Helper\.Runtime\.Cli\.(dll|exe)' } |
        ForEach-Object { [void]$targetPids.Add([int]$_.ProcessId) }
}
catch {
    # Fallback to image-name detection only when CIM is unavailable.
}

if ($targetPids.Count -eq 0) {
    exit 0
}

foreach ($processId in @($targetPids)) {
    try {
        & taskkill /F /T /PID $processId *> $null
        $taskkillExit = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
        if ($taskkillExit -ne 0 -and (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
            throw "[StopHelperRuntimeCli] taskkill failed for PID $processId with exit code $taskkillExit."
        }
    }
    catch {
        if (Get-Process -Id $processId -ErrorAction SilentlyContinue) {
            throw
        }
    }
}

$deadline = [DateTime]::UtcNow.AddSeconds([Math]::Max(1, $WaitTimeoutSec))
while ([DateTime]::UtcNow -lt $deadline) {
    $alive = @($targetPids | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
    if ($alive.Count -eq 0) {
        exit 0
    }

    Start-Sleep -Milliseconds ([Math]::Max(50, $PollIntervalMs))
}

$remaining = @($targetPids | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
if ($remaining.Count -gt 0) {
    throw "[StopHelperRuntimeCli] Failed to stop running Helper.Runtime.Cli processes: $($remaining -join ', ')"
}

exit 0
