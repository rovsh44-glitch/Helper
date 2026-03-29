[CmdletBinding()]
param(
    [string]$WorkspaceRoot = ".",
    [string]$ApiBaseUrl = "http://127.0.0.1:5000",
    [string]$ApiKey = "",
    [string]$FastQueuePath = "",
    [string]$ComplexQueuePath = "",
    [int]$PollIntervalSec = 10,
    [int]$StallThresholdSec = 900,
    [int]$ApiReadyTimeoutSec = 240,
    [string]$ReportPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

$pathConfig = Get-HelperPathConfig -WorkspaceRoot $WorkspaceRoot
$helperRoot = $pathConfig.HelperRoot
$queuePath = Join-Path $pathConfig.DataRoot "indexing_queue.json"
$apiReadyScript = Join-Path $helperRoot "scripts\common\Ensure-HelperApiReady.ps1"

Import-HelperEnvFile -Path (Join-Path $helperRoot ".env.local")

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = $env:HELPER_API_KEY
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "[ClassifiedIndex] HELPER_API_KEY is required."
}

function Resolve-LatestPreflightQueueFile {
    param(
        [Parameter(Mandatory = $true)][object]$PathConfig,
        [Parameter(Mandatory = $true)][string]$QueueFileName
    )

    $candidateDirectory = Get-ChildItem -LiteralPath $PathConfig.OperatorRuntimeRoot -Directory -Filter "library_preflight_queues_*" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $candidateDirectory) {
        return $null
    }

    $candidatePath = Join-Path $candidateDirectory.FullName $QueueFileName
    if (Test-Path -LiteralPath $candidatePath) {
        return $candidatePath
    }

    return $null
}

if ([string]::IsNullOrWhiteSpace($FastQueuePath)) {
    $FastQueuePath = Resolve-LatestPreflightQueueFile -PathConfig $pathConfig -QueueFileName "queue_text_fast.json"
}

if ([string]::IsNullOrWhiteSpace($ComplexQueuePath)) {
    $ComplexQueuePath = Resolve-LatestPreflightQueueFile -PathConfig $pathConfig -QueueFileName "queue_text_complex.json"
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $ReportPath = Join-Path $pathConfig.LogsRoot ("classified_indexing_" + $stamp + ".md")
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

function Get-StatusLabel {
    param(
        [AllowNull()][object]$Status
    )

    if ($null -eq $Status) {
        return ""
    }

    return [string]$Status
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

function Get-ApiStatusSafe {
    try {
        return Invoke-RestMethod -Method Get -Uri ($ApiBaseUrl.TrimEnd("/") + "/api/evolution/status") -Headers @{ "X-API-KEY" = $ApiKey } -TimeoutSec 30
    }
    catch {
        return $null
    }
}

function Ensure-ApiReady {
    $pwsh = Get-Command pwsh -ErrorAction Stop
    & $pwsh.Source -NoProfile -ExecutionPolicy Bypass -File $apiReadyScript -ApiBase $ApiBaseUrl -TimeoutSec $ApiReadyTimeoutSec -PollIntervalMs 1000 -StartLocalApiIfUnavailable -NoBuild | Out-Null
    $status = Get-ApiStatusSafe
    if ($null -eq $status) {
        throw "[ClassifiedIndex] API is not reachable after readiness check."
    }
}

function Pause-Indexing {
    try {
        [void](Invoke-ApiPost -RelativePath "/api/indexing/pause")
    }
    catch {
        Write-Warning ("[ClassifiedIndex] indexing/pause failed: {0}" -f $_.Exception.Message)
    }
}

function Stop-Learning {
    try {
        [void](Invoke-ApiPost -RelativePath "/api/evolution/stop")
    }
    catch {
        Write-Warning ("[ClassifiedIndex] evolution/stop failed: {0}" -f $_.Exception.Message)
    }
}

function Recover-StuckProcessing {
    $queue = Read-Queue
    $changed = $false
    $requeued = 0

    foreach ($key in @($queue.Keys)) {
        if ([string]$queue[$key] -eq "Processing") {
            $queue[$key] = "Pending"
            $changed = $true
            $requeued++
        }
    }

    if ($changed) {
        Write-Queue -Queue $queue
    }

    return $requeued
}

function Ensure-QueueEntry {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath
    )

    $queue = Read-Queue
    if (-not $queue.ContainsKey($FilePath)) {
        $queue[$FilePath] = "Pending"
        Write-Queue -Queue $queue
        return "added_pending"
    }

    if ([string]$queue[$FilePath] -eq "Processing") {
        $queue[$FilePath] = "Pending"
        Write-Queue -Queue $queue
        return "requeued_processing"
    }

    return [string]$queue[$FilePath]
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

function Load-ClassFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "[ClassifiedIndex] Queue file not found: $Path"
    }

    $data = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    return @($data.Files | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
}

function Initialize-Report {
    $reportDir = Split-Path -Parent $ReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDir)) {
        New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
    }

    $lines = @(
        "# Classified Library Indexing",
        "",
        ('- StartedAt: {0}' -f (Get-Date).ToString("s")),
        ('- ApiBaseUrl: {0}' -f $ApiBaseUrl),
        ('- QueuePath: {0}' -f $queuePath),
        ('- FastQueuePath: {0}' -f $FastQueuePath),
        ('- ComplexQueuePath: {0}' -f $ComplexQueuePath),
        "",
        "| Phase | File | InitialStatus | Result | FinalStatus | DurationSec | Note |",
        "| --- | --- | --- | --- | --- | ---: | --- |"
    )

    Set-Content -Path $ReportPath -Value ($lines -join "`r`n") -Encoding UTF8
}

function Write-ReportRow {
    param(
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][string]$File,
        [Parameter(Mandatory = $true)][string]$InitialStatus,
        [Parameter(Mandatory = $true)][string]$Result,
        [Parameter(Mandatory = $true)][string]$FinalStatus,
        [Parameter(Mandatory = $true)][double]$DurationSec,
        [Parameter(Mandatory = $true)][string]$Note
    )

    $safeNote = [string]$Note -replace "\|", "/"
    Add-Content -Path $ReportPath -Value ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} |" -f $Phase, [System.IO.Path]::GetFileName($File), $InitialStatus, $Result, $FinalStatus, [math]::Round($DurationSec, 1), $safeNote) -Encoding UTF8
}

function Wait-ForTargetResult {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$Phase
    )

    $startedAt = Get-Date
    $lastChangeAt = $startedAt
    $lastSignature = ""

    while ($true) {
        Start-Sleep -Seconds $PollIntervalSec

        $queueStatus = Get-QueueStatus -FilePath $FilePath
        $apiStatus = Get-ApiStatusSafe
        $activeTask = if ($null -eq $apiStatus) { "" } else { [string]$apiStatus.activeTask }
        $fileProgress = if ($null -eq $apiStatus -or $null -eq $apiStatus.fileProgress) { 0 } else { [double]$apiStatus.fileProgress }
        $isIndexing = if ($null -eq $apiStatus) { $false } else { [bool]$apiStatus.isIndexing }
        $signature = "{0}|{1}|{2}|{3}" -f $queueStatus, $activeTask, $fileProgress, $isIndexing

        if ($signature -ne $lastSignature) {
            $lastSignature = $signature
            $lastChangeAt = Get-Date
        }

        Write-Host ("[ClassifiedIndex] {0}: {1} | queue={2} | active={3} | progress={4:N1}%" -f $Phase, [System.IO.Path]::GetFileName($FilePath), $queueStatus, $activeTask, $fileProgress)

        if ($queueStatus -eq "Done") {
            return [pscustomobject]@{
                Result = "done"
                FinalStatus = $queueStatus
                DurationSec = ((Get-Date) - $startedAt).TotalSeconds
                Note = "indexed successfully"
            }
        }

        if ($queueStatus -like "Error*") {
            return [pscustomobject]@{
                Result = "error"
                FinalStatus = $queueStatus
                DurationSec = ((Get-Date) - $startedAt).TotalSeconds
                Note = $queueStatus
            }
        }

        $stalledFor = ((Get-Date) - $lastChangeAt).TotalSeconds
        if ($stalledFor -ge $StallThresholdSec) {
            Pause-Indexing
            Stop-Learning
            Start-Sleep -Seconds ([Math]::Min($PollIntervalSec, 5))

            $afterStop = Get-QueueStatus -FilePath $FilePath
            if ($afterStop -eq "Processing") {
                $queue = Read-Queue
                if ($queue.ContainsKey($FilePath)) {
                    $queue[$FilePath] = "Pending"
                    Write-Queue -Queue $queue
                }
                $afterStop = "Pending"
            }

            return [pscustomobject]@{
                Result = "stalled"
                FinalStatus = $afterStop
                DurationSec = ((Get-Date) - $startedAt).TotalSeconds
                Note = ("stalled for {0} sec" -f [int]$stalledFor)
            }
        }
    }
}

function Invoke-FileIndexing {
    param(
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][string]$FilePath
    )

    $initialStatus = Get-QueueStatus -FilePath $FilePath
    if ($initialStatus -eq "Done") {
        Write-Host ("[ClassifiedIndex] {0}: skipping already Done {1}" -f $Phase, [System.IO.Path]::GetFileName($FilePath))
        return [pscustomobject]@{
            InitialStatus = $initialStatus
            Result = "skipped_done"
            FinalStatus = $initialStatus
            DurationSec = 0
            Note = "already indexed"
        }
    }

    $ensureState = Ensure-QueueEntry -FilePath $FilePath
    if ($initialStatus -eq "Missing") {
        $initialStatus = $ensureState
    }

    Pause-Indexing
    Stop-Learning
    Ensure-ApiReady

    [void](Invoke-ApiPost -RelativePath "/api/indexing/start" -Body @{
        targetPath = $FilePath
        singleFileOnly = $true
    })

    return Wait-ForTargetResult -FilePath $FilePath -Phase $Phase
}

function Invoke-Phase {
    param(
        [Parameter(Mandatory = $true)][string]$PhaseName,
        [Parameter(Mandatory = $true)][string[]]$Files
    )

    $phaseRows = @()
    $errorRows = @()
    foreach ($file in $Files) {
        $initialStatus = Get-QueueStatus -FilePath $file
        $result = Invoke-FileIndexing -Phase $PhaseName -FilePath $file
        $row = [pscustomobject]@{
            Phase = $PhaseName
            File = $file
            InitialStatus = $initialStatus
            Result = $result.Result
            FinalStatus = $result.FinalStatus
            DurationSec = $result.DurationSec
            Note = $result.Note
        }
        $phaseRows += $row

        Write-ReportRow -Phase $PhaseName -File $file -InitialStatus $initialStatus -Result $result.Result -FinalStatus $result.FinalStatus -DurationSec $result.DurationSec -Note $result.Note

        if ($result.Result -eq "error") {
            $errorRows += $row
            continue
        }

        if ($result.Result -notin @("done", "skipped_done")) {
            return [pscustomobject]@{
                Completed = $false
                HadErrors = @($errorRows).Count -gt 0
                Rows = $phaseRows
                ErrorRows = $errorRows
                FailedFile = $file
                FailureResult = $result.Result
                FailureStatus = $result.FinalStatus
            }
        }
    }

    return [pscustomobject]@{
        Completed = $true
        HadErrors = @($errorRows).Count -gt 0
        Rows = $phaseRows
        ErrorRows = $errorRows
        FailedFile = ""
        FailureResult = ""
        FailureStatus = ""
    }
}

Initialize-Report
Ensure-ApiReady
Pause-Indexing
Stop-Learning
Start-Sleep -Seconds 2
$requeued = Recover-StuckProcessing

$fastFiles = Load-ClassFiles -Path $FastQueuePath
$complexFiles = Load-ClassFiles -Path $ComplexQueuePath
$fastCount = @($fastFiles).Count
$complexCount = @($complexFiles).Count

Add-Content -Path $ReportPath -Value "" -Encoding UTF8
Add-Content -Path $ReportPath -Value ("- RequeuedProcessingAtStart: {0}" -f $requeued) -Encoding UTF8
Add-Content -Path $ReportPath -Value ("- TEXT_FAST total: {0}" -f $fastCount) -Encoding UTF8
Add-Content -Path $ReportPath -Value ("- TEXT_COMPLEX total: {0}" -f $complexCount) -Encoding UTF8

$fastResult = Invoke-Phase -PhaseName "TEXT_FAST" -Files $fastFiles

Add-Content -Path $ReportPath -Value "" -Encoding UTF8
Add-Content -Path $ReportPath -Value "## Final Summary" -Encoding UTF8
Add-Content -Path $ReportPath -Value ("- TEXT_FAST completed: {0}" -f $fastResult.Completed) -Encoding UTF8
Add-Content -Path $ReportPath -Value ("- TEXT_FAST errors: {0}" -f @($fastResult.ErrorRows).Count) -Encoding UTF8

if (-not $fastResult.Completed) {
    Add-Content -Path $ReportPath -Value ("- TEXT_FAST stopped at: {0}" -f $fastResult.FailedFile) -Encoding UTF8
    Add-Content -Path $ReportPath -Value ("- TEXT_FAST stop result: {0}" -f $fastResult.FailureResult) -Encoding UTF8
    Add-Content -Path $ReportPath -Value "- TEXT_COMPLEX completed: false" -Encoding UTF8
    Add-Content -Path $ReportPath -Value "- TEXT_COMPLEX skipped because TEXT_FAST did not complete." -Encoding UTF8
    [pscustomobject]@{
        ReportPath = $ReportPath
        TextFastCompleted = $false
        TextFastErrors = @($fastResult.ErrorRows).Count
        TextFastFailedFile = $fastResult.FailedFile
        TextFastFailureResult = $fastResult.FailureResult
        TextComplexStarted = $false
    } | ConvertTo-Json -Depth 6
    exit 1
}

$complexResult = Invoke-Phase -PhaseName "TEXT_COMPLEX" -Files $complexFiles

Add-Content -Path $ReportPath -Value ("- TEXT_COMPLEX completed: {0}" -f $complexResult.Completed) -Encoding UTF8
Add-Content -Path $ReportPath -Value ("- TEXT_COMPLEX errors: {0}" -f @($complexResult.ErrorRows).Count) -Encoding UTF8
if (-not $complexResult.Completed) {
    Add-Content -Path $ReportPath -Value ("- TEXT_COMPLEX stopped at: {0}" -f $complexResult.FailedFile) -Encoding UTF8
    Add-Content -Path $ReportPath -Value ("- TEXT_COMPLEX stop result: {0}" -f $complexResult.FailureResult) -Encoding UTF8
    [pscustomobject]@{
        ReportPath = $ReportPath
        TextFastCompleted = $true
        TextFastErrors = @($fastResult.ErrorRows).Count
        TextFastFailedFile = ""
        TextFastFailureResult = ""
        TextComplexStarted = $true
        TextComplexCompleted = $false
        TextComplexErrors = @($complexResult.ErrorRows).Count
        TextComplexFailedFile = $complexResult.FailedFile
        TextComplexFailureResult = $complexResult.FailureResult
    } | ConvertTo-Json -Depth 6
    exit 1
}

[pscustomobject]@{
    ReportPath = $ReportPath
    TextFastCompleted = $true
    TextFastErrors = @($fastResult.ErrorRows).Count
    TextFastFailedFile = ""
    TextFastFailureResult = ""
    TextComplexStarted = $true
    TextComplexCompleted = $true
    TextComplexErrors = @($complexResult.ErrorRows).Count
    TextComplexFailedFile = ""
    TextComplexFailureResult = ""
} | ConvertTo-Json -Depth 6
