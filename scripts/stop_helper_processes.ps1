param(
    [switch]$SkipCli,
    [switch]$SkipApi,
    [switch]$SkipUi,
    [int]$WaitTimeoutSec = 15,
    [int]$PollIntervalMs = 200
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetPids = New-Object System.Collections.Generic.HashSet[int]

function Add-TargetPid {
    param([int]$ProcessId)

    if ($ProcessId -gt 0) {
        [void]$targetPids.Add($ProcessId)
    }
}

function Add-TargetsByProcessName {
    param([string]$ProcessName)

    Get-Process $ProcessName -ErrorAction SilentlyContinue |
        ForEach-Object { Add-TargetPid -ProcessId $_.Id }
}

function Add-TargetsByCommandLine {
    param(
        [string]$ImageName,
        [string]$Pattern
    )

    try {
        Get-CimInstance Win32_Process -Filter ("Name = '{0}'" -f $ImageName) -ErrorAction Stop |
            Where-Object { $_.CommandLine -match $Pattern } |
            ForEach-Object { Add-TargetPid -ProcessId ([int]$_.ProcessId) }
    }
    catch {
        # Fall back to image-name detection only when CIM is unavailable.
    }
}

function Stop-TargetProcesses {
    param(
        [System.Collections.Generic.HashSet[int]]$ProcessIds,
        [int]$StopWaitTimeoutSec,
        [int]$StopPollIntervalMs
    )

    if ($ProcessIds.Count -eq 0) {
        return
    }

    foreach ($processId in @($ProcessIds)) {
        try {
            & taskkill /F /T /PID $processId *> $null
            $taskkillExit = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }
            if ($taskkillExit -ne 0 -and (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
                throw "[StopHelperProcesses] taskkill failed for PID $processId with exit code $taskkillExit."
            }
        }
        catch {
            if (Get-Process -Id $processId -ErrorAction SilentlyContinue) {
                throw
            }
        }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds([Math]::Max(1, $StopWaitTimeoutSec))
    while ([DateTime]::UtcNow -lt $deadline) {
        $alive = @($ProcessIds | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
        if ($alive.Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds ([Math]::Max(50, $StopPollIntervalMs))
    }

    $remaining = @($ProcessIds | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
    if ($remaining.Count -gt 0) {
        throw "[StopHelperProcesses] Failed to stop Helper processes: $($remaining -join ', ')"
    }
}

if (-not $SkipCli) {
    $stopHelperRuntimeCliScript = Join-Path $scriptRoot "stop_running_helper_runtime_cli.ps1"
    if (Test-Path $stopHelperRuntimeCliScript) {
        & $stopHelperRuntimeCliScript -WaitTimeoutSec $WaitTimeoutSec -PollIntervalMs $PollIntervalMs
    }
}

if (-not $SkipApi) {
    Add-TargetsByProcessName -ProcessName "Helper.Api"
    Add-TargetsByCommandLine -ImageName "dotnet.exe" -Pattern '(?i)Helper\.Api(?:\.(?:dll|exe|csproj))?'
}

if (-not $SkipUi) {
    Add-TargetsByCommandLine -ImageName "node.exe" -Pattern '(?i)\bvite\b'
    Add-TargetsByCommandLine -ImageName "cmd.exe" -Pattern '(?i)npm(?:\.cmd)?\s+run\s+dev\b.*--strictPort'
}

Stop-TargetProcesses -ProcessIds $targetPids -StopWaitTimeoutSec $WaitTimeoutSec -StopPollIntervalMs $PollIntervalMs

exit 0

