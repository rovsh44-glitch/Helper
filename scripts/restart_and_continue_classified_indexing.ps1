[CmdletBinding()]
param(
    [string]$WorkspaceRoot = ".",
    [string]$ApiBaseUrl = "http://127.0.0.1:5000"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

$pathConfig = Get-HelperPathConfig -WorkspaceRoot $WorkspaceRoot
$helperRoot = $pathConfig.HelperRoot
$runtimeRoot = $pathConfig.LogsRoot
$classifiedScript = Join-Path $helperRoot "scripts\run_classified_library_indexing.ps1"
$apiReadyScript = Join-Path $helperRoot "scripts\common\Ensure-HelperApiReady.ps1"
$activePidPath = Join-Path $runtimeRoot "classified_indexing_active.pid"

Import-HelperEnvFile -Path (Join-Path $helperRoot ".env.local")

$apiKey = $env:HELPER_API_KEY
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw "HELPER_API_KEY is not configured."
}

function Stop-ProcessesMatching {
    param(
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    $stopped = @()
    $processes = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -eq "pwsh.exe" -and [string]$_.CommandLine -match $Pattern
    })

    foreach ($process in $processes) {
        try {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
            $stopped += ("{0}:{1}" -f $process.Name, $process.ProcessId)
        }
        catch {
            $stopped += ("{0}:{1}:stop_failed:{2}" -f $process.Name, $process.ProcessId, $_.Exception.Message)
        }
    }

    return $stopped
}

function Stop-HelperApiRuntime {
    $stopped = @()
    $apiProcess = Get-CimInstance Win32_Process | Where-Object { $_.Name -eq "Helper.Api.exe" } | Select-Object -First 1
    if ($null -eq $apiProcess) {
        return $stopped
    }

    $related = @($apiProcess)
    if ($apiProcess.ParentProcessId -gt 0) {
        $parent = Get-CimInstance Win32_Process -Filter ("ProcessId = {0}" -f $apiProcess.ParentProcessId) -ErrorAction SilentlyContinue
        if ($null -ne $parent) {
            $related += $parent
        }
    }

    foreach ($process in ($related | Sort-Object ProcessId -Descending)) {
        try {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
            $stopped += ("{0}:{1}" -f $process.Name, $process.ProcessId)
        }
        catch {
            $stopped += ("{0}:{1}:stop_failed:{2}" -f $process.Name, $process.ProcessId, $_.Exception.Message)
        }
    }

    $relatedIds = @($related | ForEach-Object { $_.ProcessId })
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $alive = Get-Process Helper.Api,dotnet -ErrorAction SilentlyContinue | Where-Object { $_.Id -in $relatedIds }
        if (@($alive).Count -eq 0) {
            break
        }

        Start-Sleep -Milliseconds 500
    }

    return $stopped
}

$stoppedClassified = Stop-ProcessesMatching -Pattern "run_classified_library_indexing\.ps1"
$stoppedApi = Stop-HelperApiRuntime

$pwsh = Get-Command pwsh -ErrorAction Stop
& dotnet build src\Helper.Api\Helper.Api.csproj | Out-Host

& $pwsh.Source -NoProfile -ExecutionPolicy Bypass -File $apiReadyScript -ApiBase $ApiBaseUrl -TimeoutSec 180 -PollIntervalMs 1000 -StartLocalApiIfUnavailable -NoBuild | Out-Host

$stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$reportPath = Join-Path $runtimeRoot ("classified_indexing_" + $stamp + ".md")
$stdoutPath = Join-Path $runtimeRoot ("classified_indexing_" + $stamp + "_stdout.log")
$stderrPath = Join-Path $runtimeRoot ("classified_indexing_" + $stamp + "_stderr.log")

$batchProcess = Start-Process `
    -FilePath $pwsh.Source `
    -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $classifiedScript,
        "-WorkspaceRoot",
        $helperRoot,
        "-ApiBaseUrl",
        $ApiBaseUrl,
        "-ApiKey",
        $apiKey,
        "-ReportPath",
        $reportPath
    ) `
    -WorkingDirectory $helperRoot `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath `
    -PassThru

Set-Content -Path $activePidPath -Value $batchProcess.Id -Encoding UTF8

[pscustomobject]@{
    StoppedClassified = @($stoppedClassified)
    StoppedApi = @($stoppedApi)
    BatchPid = $batchProcess.Id
    ActivePidPath = $activePidPath
    ReportPath = $reportPath
    StdoutPath = $stdoutPath
    StderrPath = $stderrPath
} | ConvertTo-Json -Depth 6

