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
$logsRoot = $pathConfig.LogsRoot
$queuePath = Join-Path $pathConfig.DataRoot "indexing_queue.json"
$apiReadyScript = Join-Path $helperRoot "scripts\common\Ensure-HelperApiReady.ps1"
$orderedIndexScript = Join-Path $helperRoot "scripts\run_ordered_library_indexing.ps1"
$supervisorScript = Join-Path $helperRoot "scripts\monitor_library_indexing_supervisor.ps1"

Import-HelperEnvFile -Path (Join-Path $helperRoot ".env.local")

if ([string]::IsNullOrWhiteSpace($env:HELPER_API_KEY)) {
    throw "HELPER_API_KEY is not configured."
}

function Stop-ProcessesMatching {
    param(
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    $stopped = New-Object System.Collections.Generic.List[string]
    $processes = @(Get-CimInstance Win32_Process | Where-Object {
        $commandLine = [string]$_.CommandLine
        foreach ($pattern in $Patterns) {
            if (-not [string]::IsNullOrWhiteSpace($pattern) -and $commandLine -match $pattern) {
                return $true
            }
        }

        return $false
    })

    foreach ($process in $processes) {
        try {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
            $stopped.Add(("{0}:{1}" -f $process.Name, $process.ProcessId)) | Out-Null
        }
        catch {
            Write-Warning ("Failed to stop process {0} ({1}): {2}" -f $process.Name, $process.ProcessId, $_.Exception.Message)
        }
    }

    return @($stopped)
}

function Read-Queue {
    if (-not (Test-Path -LiteralPath $queuePath)) {
        return @{}
    }

    $raw = (Get-Content -LiteralPath $queuePath -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return @{}
    }

    return ($raw | ConvertFrom-Json -AsHashtable)
}

function Write-Queue {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Queue
    )

    $json = $Queue | ConvertTo-Json -Depth 8
    Set-Content -Path $queuePath -Value $json -Encoding UTF8
}

function Requeue-Errors {
    $queue = Read-Queue
    $count = 0
    foreach ($key in @($queue.Keys)) {
        if ([string]$queue[$key] -like "Error*") {
            $queue[$key] = "Pending"
            $count++
        }
    }

    if ($count -gt 0) {
        Write-Queue -Queue $queue
    }

    return $count
}

$pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
if ($null -eq $pwsh) {
    $pwsh = Get-Command powershell -ErrorAction Stop
}

$stoppedSupervisor = Stop-ProcessesMatching -Patterns @(
    "monitor_library_indexing_supervisor\.ps1"
)

$stoppedBatch = Stop-ProcessesMatching -Patterns @(
    "run_ordered_library_indexing\.ps1"
)

$stoppedApi = Stop-ProcessesMatching -Patterns @(
    "dotnet run --project src/Helper\.Api",
    "run --project src/Helper\.Api"
)

Push-Location $helperRoot
try {
    dotnet build src\Helper.Api\Helper.Api.csproj -c Debug | Out-Host
}
finally {
    Pop-Location
}

powershell -ExecutionPolicy Bypass -File $apiReadyScript -ApiBase $ApiBaseUrl -TimeoutSec 180 -PollIntervalMs 1000 -StartLocalApiIfUnavailable -NoBuild | Out-Host

$requeuedErrors = Requeue-Errors

$timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$batchReportPath = Join-Path $logsRoot ("ordered_indexing_batch_resume_" + $timestamp + ".md")
$batchStdoutPath = Join-Path $logsRoot ("ordered_indexing_batch_resume_" + $timestamp + "_stdout.log")
$batchStderrPath = Join-Path $logsRoot ("ordered_indexing_batch_resume_" + $timestamp + "_stderr.log")
$supervisorStdoutPath = Join-Path $logsRoot "indexing_supervisor_stdout.log"
$supervisorStderrPath = Join-Path $logsRoot "indexing_supervisor_stderr.log"

$batchProcess = Start-Process `
    -FilePath $pwsh.Source `
    -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $orderedIndexScript,
        "-WorkspaceRoot",
        $helperRoot,
        "-ApiBaseUrl",
        $ApiBaseUrl,
        "-ApiKey",
        $env:HELPER_API_KEY,
        "-ReportPath",
        $batchReportPath
    ) `
    -WorkingDirectory $helperRoot `
    -RedirectStandardOutput $batchStdoutPath `
    -RedirectStandardError $batchStderrPath `
    -PassThru

$supervisorProcess = Start-Process `
    -FilePath $pwsh.Source `
    -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $supervisorScript,
        "-WorkspaceRoot",
        $helperRoot,
        "-ApiBaseUrl",
        $ApiBaseUrl
    ) `
    -WorkingDirectory $helperRoot `
    -RedirectStandardOutput $supervisorStdoutPath `
    -RedirectStandardError $supervisorStderrPath `
    -PassThru

[pscustomobject]@{
    HelperRoot = $helperRoot
    QueuePath = $queuePath
    RequeuedErrors = $requeuedErrors
    StoppedSupervisor = @($stoppedSupervisor)
    StoppedBatch = @($stoppedBatch)
    StoppedApi = @($stoppedApi)
    BatchPid = $batchProcess.Id
    BatchReportPath = $batchReportPath
    BatchStdoutPath = $batchStdoutPath
    BatchStderrPath = $batchStderrPath
    SupervisorPid = $supervisorProcess.Id
} | ConvertTo-Json -Depth 6

