param(
    [Parameter(Mandatory = $true)]
    [int[]]$ProcessIds,
    [datetime]$RequireStartAfter = [datetime]::MinValue,
    [switch]$Preview
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$taskkill = Get-Command taskkill.exe -ErrorAction SilentlyContinue
if ($null -eq $taskkill) {
    throw "[CiGateCleanup] taskkill.exe not found."
}

$candidates = New-Object System.Collections.Generic.List[object]
$skipped = New-Object System.Collections.Generic.List[string]

foreach ($processId in @($ProcessIds | Sort-Object -Unique)) {
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        $skipped.Add(("PID {0}: not running" -f $processId))
        continue
    }

    if (-not [string]::Equals($process.ProcessName, "dotnet", [System.StringComparison]::OrdinalIgnoreCase)) {
        $skipped.Add(("PID {0}: skipped because ProcessName={1}" -f $processId, $process.ProcessName))
        continue
    }

    if ($RequireStartAfter -gt [datetime]::MinValue -and $process.StartTime -lt $RequireStartAfter) {
        $skipped.Add(("PID {0}: skipped because StartTime {1:o} is before RequireStartAfter {2:o}" -f $processId, $process.StartTime, $RequireStartAfter))
        continue
    }

    $candidates.Add([pscustomobject]@{
        Id = $process.Id
        ProcessName = $process.ProcessName
        StartTime = $process.StartTime
        CPU = $process.CPU
        WorkingSet64 = $process.WorkingSet64
    })
}

if ($candidates.Count -eq 0) {
    Write-Host "[CiGateCleanup] No matching orphaned dotnet processes found."
    foreach ($line in $skipped) {
        Write-Host ("[CiGateCleanup] {0}" -f $line) -ForegroundColor DarkYellow
    }
    exit 0
}

Write-Host "[CiGateCleanup] Candidate processes:"
$candidates | Sort-Object StartTime, Id | Format-Table -AutoSize | Out-Host

foreach ($line in $skipped) {
    Write-Host ("[CiGateCleanup] {0}" -f $line) -ForegroundColor DarkYellow
}

if ($Preview) {
    Write-Host "[CiGateCleanup] Preview mode only. No processes were stopped." -ForegroundColor DarkCyan
    exit 0
}

foreach ($candidate in $candidates) {
    & $taskkill.Source /PID $candidate.Id /T /F *> $null
    $exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0 -and (Get-Process -Id $candidate.Id -ErrorAction SilentlyContinue)) {
        throw "[CiGateCleanup] taskkill failed for PID $($candidate.Id) with exit code $exitCode."
    }
}

$remaining = @($candidates | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue })
if ($remaining.Count -gt 0) {
    throw "[CiGateCleanup] Some orphaned dotnet processes are still alive: $($remaining.Id -join ', ')"
}

Write-Host ("[CiGateCleanup] Stopped {0} orphaned dotnet process(es)." -f $candidates.Count) -ForegroundColor Green
