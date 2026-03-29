[CmdletBinding()]
param(
    [string]$WorkspaceRoot = ".",
    [string]$ApiBaseUrl = "http://127.0.0.1:5000",
    [string]$ApiKey = "",
    [int]$PollIntervalSec = 10,
    [int]$PerFileTimeoutSec = 7200,
    [string]$QueuePath = "",
    [string]$ReportPath = "",
    [string]$ActivePidPath = "",
    [Parameter(Mandatory = $true)][string]$ExcludePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

$pathConfig = Get-HelperPathConfig -WorkspaceRoot $WorkspaceRoot
$helperRoot = $pathConfig.HelperRoot

Import-HelperEnvFile -Path (Join-Path $helperRoot ".env.local")

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = $env:HELPER_API_KEY
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "[PendingExceptBatch] HELPER_API_KEY is required."
}

if ([string]::IsNullOrWhiteSpace($QueuePath)) {
    $QueuePath = Join-Path $pathConfig.DataRoot "indexing_queue.json"
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $ReportPath = Join-Path $pathConfig.OperatorRuntimeRoot ("pending_except_batch_" + $stamp + ".md")
}

if ([string]::IsNullOrWhiteSpace($ActivePidPath)) {
    $ActivePidPath = Join-Path $pathConfig.OperatorRuntimeRoot "pending_except_batch_active.pid"
}

function Read-Queue {
    if (-not (Test-Path -LiteralPath $QueuePath)) {
        return @{}
    }

    return (Get-Content -LiteralPath $QueuePath -Raw | ConvertFrom-Json -AsHashtable)
}

function Get-QueueStatus {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath
    )

    $queue = Read-Queue
    if ($queue.ContainsKey($FilePath)) {
        return [string]$queue[$FilePath]
    }

    return "Missing"
}

function Get-PendingFiles {
    $queue = Read-Queue
    return @(
        $queue.GetEnumerator() |
            Where-Object { [string]$_.Value -eq "Pending" -and [string]$_.Key -ne $ExcludePath } |
            ForEach-Object { [string]$_.Key }
    )
}

function Invoke-ApiPost {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [hashtable]$Body = @{}
    )

    $uri = $ApiBaseUrl.TrimEnd("/") + $RelativePath
    $payload = if (@($Body.Keys).Count -gt 0) { $Body | ConvertTo-Json -Depth 8 } else { "{}" }
    return Invoke-RestMethod -Method Post -Uri $uri -Headers @{ "X-API-KEY" = $ApiKey } -Body $payload -ContentType "application/json" -TimeoutSec 60
}

function Wait-ForTerminalStatus {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath
    )

    $started = Get-Date
    while (((Get-Date) - $started).TotalSeconds -lt $PerFileTimeoutSec) {
        Start-Sleep -Seconds $PollIntervalSec
        $queueStatus = Get-QueueStatus -FilePath $FilePath
        if ($queueStatus -eq "Done" -or $queueStatus -like "Error*" -or $queueStatus -eq "Pending") {
            return [pscustomobject]@{
                FinalStatus = $queueStatus
                DurationSec = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
            }
        }
    }

    return [pscustomobject]@{
        FinalStatus = "Timeout"
        DurationSec = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
    }
}

function Initialize-Report {
    $reportDir = Split-Path -Parent $ReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDir)) {
        New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
    }

    $lines = @(
        "# Pending File Batch With Exclusion",
        "",
        ('- StartedAt: {0}' -f (Get-Date).ToString("s")),
        ('- QueuePath: {0}' -f $QueuePath),
        ('- ExcludedPath: {0}' -f $ExcludePath),
        "",
        "| File | Result | DurationSec | Note |",
        "| --- | --- | ---: | --- |"
    )

    Set-Content -Path $ReportPath -Value ($lines -join "`r`n") -Encoding UTF8
}

function Add-ReportRow {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$Result,
        [Parameter(Mandatory = $true)][double]$DurationSec,
        [Parameter(Mandatory = $true)][string]$Note
    )

    $safeNote = [string]$Note -replace "\|", "/"
    Add-Content -Path $ReportPath -Value ("| {0} | {1} | {2} | {3} |" -f [System.IO.Path]::GetFileName($FilePath), $Result, [math]::Round($DurationSec, 1), $safeNote) -Encoding UTF8
}

Initialize-Report
Set-Content -Path $ActivePidPath -Value $PID -Encoding UTF8

$files = Get-PendingFiles
Add-Content -Path $ReportPath -Value ("- PendingFilesAtStartExcludingTarget: {0}" -f @($files).Count) -Encoding UTF8

$done = 0
$errors = 0
$returnedPending = 0
$timeouts = 0

foreach ($file in $files) {
    [void](Invoke-ApiPost -RelativePath "/api/indexing/start" -Body @{
        targetPath = $file
        singleFileOnly = $true
    })

    $result = Wait-ForTerminalStatus -FilePath $file
    $note = $result.FinalStatus
    if ($result.FinalStatus -eq "Done") {
        $done++
        $note = "indexed successfully"
    }
    elseif ($result.FinalStatus -eq "Timeout") {
        $timeouts++
    }
    elseif ($result.FinalStatus -eq "Pending") {
        $returnedPending++
        $note = "returned to pending without terminal result"
    }
    else {
        $errors++
    }

    Add-ReportRow -FilePath $file -Result $result.FinalStatus -DurationSec $result.DurationSec -Note $note
}

Add-Content -Path $ReportPath -Value "" -Encoding UTF8
Add-Content -Path $ReportPath -Value "## Summary" -Encoding UTF8
Add-Content -Path $ReportPath -Value ("- Done: {0}" -f $done) -Encoding UTF8
Add-Content -Path $ReportPath -Value ("- Error: {0}" -f $errors) -Encoding UTF8
Add-Content -Path $ReportPath -Value ("- ReturnedPending: {0}" -f $returnedPending) -Encoding UTF8
Add-Content -Path $ReportPath -Value ("- Timeout: {0}" -f $timeouts) -Encoding UTF8
