[CmdletBinding()]
param(
    [string]$WorkspaceRoot = ".",
    [string]$ApiBaseUrl = "http://127.0.0.1:5000",
    [string]$ApiKey = "",
    [int]$CycleMinutes = 20,
    [int]$MaxUnknownErrorRetries = 2,
    [string]$ReportPath = "",
    [string]$StatePath = ""
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
$supervisorPidPath = Join-Path $logsRoot "ordered_indexing_supervisor.pid"
$pipelineVersion = if ([string]::IsNullOrWhiteSpace($env:HELPER_INDEX_PIPELINE_VERSION)) { "v2" } else { $env:HELPER_INDEX_PIPELINE_VERSION }

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = $env:HELPER_API_KEY
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "[IndexSupervisor] HELPER_API_KEY is required."
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $logsRoot ("indexing_supervisor_" + (Get-Date -Format "yyyy-MM-dd") + ".md")
}

if ([string]::IsNullOrWhiteSpace($StatePath)) {
    $StatePath = Join-Path $logsRoot "indexing_supervisor_state.json"
}

New-Item -ItemType Directory -Force -Path $logsRoot | Out-Null
Set-Content -Path $supervisorPidPath -Value $PID -Encoding UTF8

if (-not (Test-Path -LiteralPath $ReportPath)) {
    $reportLines = @(
        "# Indexing Supervisor",
        "",
        ('- StartedAt: `{0}`' -f (Get-Date).ToString("s")),
        ('- ApiBaseUrl: `{0}`' -f $ApiBaseUrl),
        ('- QueuePath: `{0}`' -f $queuePath),
        ""
    )
    Set-Content -Path $ReportPath -Value ($reportLines -join "`r`n") -Encoding UTF8
}

function Write-ReportSection {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][object[]]$Lines
    )

    Add-Content -Path $ReportPath -Value "" -Encoding UTF8
    Add-Content -Path $ReportPath -Value ("## {0}" -f $Title) -Encoding UTF8
    foreach ($line in $Lines) {
        Add-Content -Path $ReportPath -Value $line -Encoding UTF8
    }
}

function Load-State {
    if (-not (Test-Path -LiteralPath $StatePath)) {
        return @{
            RetryCounts = @{}
            LastBatchReportPath = ""
        }
    }

    $raw = (Get-Content -LiteralPath $StatePath -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return @{
            RetryCounts = @{}
            LastBatchReportPath = ""
        }
    }

    $state = $raw | ConvertFrom-Json -AsHashtable
    if (-not $state.ContainsKey("RetryCounts") -or $null -eq $state.RetryCounts) {
        $state.RetryCounts = @{}
    }
    if (-not $state.ContainsKey("LastBatchReportPath")) {
        $state.LastBatchReportPath = ""
    }

    return $state
}

function Save-State {
    param(
        [Parameter(Mandatory = $true)][hashtable]$State
    )

    $json = $State | ConvertTo-Json -Depth 8
    Set-Content -Path $StatePath -Value $json -Encoding UTF8
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

function Get-QueueSummary {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Queue
    )

    $processing = @($Queue.GetEnumerator() | Where-Object { $_.Value -eq "Processing" })
    $errors = @($Queue.GetEnumerator() | Where-Object { [string]$_.Value -like "Error*" })
    $pending = @($Queue.GetEnumerator() | Where-Object { $_.Value -eq "Pending" })
    $done = @($Queue.GetEnumerator() | Where-Object { $_.Value -eq "Done" })

    return [PSCustomObject]@{
        Processing = $processing
        Errors = $errors
        Pending = $pending
        Done = $done
    }
}

function Get-ApiStatusSafe {
    try {
        return Invoke-RestMethod -Method Get -Uri ($ApiBaseUrl.TrimEnd("/") + "/api/evolution/status") -Headers @{ "X-API-KEY" = $ApiKey } -TimeoutSec 30
    }
    catch {
        return $null
    }
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

function Ensure-ApiReady {
    $status = Get-ApiStatusSafe
    if ($null -ne $status) {
        return [PSCustomObject]@{
            Restarted = $false
            Status = $status
            Note = "api reachable"
        }
    }

    powershell -ExecutionPolicy Bypass -File $apiReadyScript -ApiBase $ApiBaseUrl -TimeoutSec 180 -PollIntervalMs 1000 -StartLocalApiIfUnavailable -NoBuild | Out-Null
    $status = Get-ApiStatusSafe
    if ($null -eq $status) {
        throw "[IndexSupervisor] API restart failed."
    }

    return [PSCustomObject]@{
        Restarted = $true
        Status = $status
        Note = "api restarted"
    }
}

function Get-QdrantCollections {
    try {
        $response = Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:6333/collections" -TimeoutSec 30
        return @($response.result.collections.name)
    }
    catch {
        Write-Warning ("[IndexSupervisor] Failed to list Qdrant collections: {0}" -f $_.Exception.Message)
        return @()
    }
}

function Remove-FileArtifactsByHash {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Collections
    )

    if (-not (Test-Path -LiteralPath $FilePath)) {
        return "missing file"
    }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $FilePath).Hash.ToLowerInvariant()
    if (@($Collections).Count -eq 0) {
        return ("no collections; hash={0}" -f $hash)
    }

    $payload = @{
        filter = @{
            must = @(
                @{
                    key = "file_hash"
                    match = @{
                        value = $hash
                    }
                }
            )
        }
    } | ConvertTo-Json -Depth 8

    foreach ($collection in $Collections) {
        try {
            Invoke-RestMethod -Method Post -Uri ("http://127.0.0.1:6333/collections/{0}/points/delete" -f $collection) -Body $payload -ContentType "application/json" -TimeoutSec 30 | Out-Null
        }
        catch {
            Write-Warning ("[IndexSupervisor] Delete by hash failed for {0} in {1}: {2}" -f $FilePath, $collection, $_.Exception.Message)
        }
    }

    return ("cleaned hash={0}" -f $hash)
}

function Recover-OrphanedProcessing {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Queue,
        [Parameter(Mandatory = $true)][object[]]$ProcessingRows,
        [Parameter(Mandatory = $true)][string[]]$Collections
    )

    $actions = New-Object System.Collections.Generic.List[string]
    foreach ($row in $ProcessingRows) {
        $cleanupNote = Remove-FileArtifactsByHash -FilePath $row.Key -Collections $Collections
        $Queue[$row.Key] = "Pending"
        $actions.Add(("requeued orphaned processing: {0} ({1})" -f $row.Key, $cleanupNote)) | Out-Null
    }

    return $actions
}

function Recover-ErrorRows {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Queue,
        [Parameter(Mandatory = $true)][object[]]$ErrorRows,
        [Parameter(Mandatory = $true)][hashtable]$State,
        [Parameter(Mandatory = $true)][string[]]$Collections
    )

    $actions = New-Object System.Collections.Generic.List[string]
    foreach ($row in $ErrorRows) {
        $filePath = [string]$row.Key
        $status = [string]$row.Value
        if (-not $State.RetryCounts.ContainsKey($filePath)) {
            $State.RetryCounts[$filePath] = 0
        }

        $isCancellation = $status -match "task was canceled|operation was canceled|cancelled"
        $retryCount = [int]$State.RetryCounts[$filePath]
        if ($isCancellation -or $retryCount -lt $MaxUnknownErrorRetries) {
            $cleanupNote = Remove-FileArtifactsByHash -FilePath $filePath -Collections $Collections
            $Queue[$filePath] = "Pending"
            $State.RetryCounts[$filePath] = $retryCount + 1
            $actions.Add(("requeued error: {0} ({1}; previousStatus={2})" -f $filePath, $cleanupNote, $status)) | Out-Null
            continue
        }

        $actions.Add(("manual review required: {0} ({1}; retries={2})" -f $filePath, $status, $retryCount)) | Out-Null
    }

    return $actions
}

function Get-ActiveBatchProcesses {
    $pidFiles = @(Get-ChildItem -LiteralPath $logsRoot -Filter "ordered_indexing_batch*.pid" -ErrorAction SilentlyContinue)
    $processes = foreach ($pidFile in $pidFiles) {
        $rawPid = (Get-Content -LiteralPath $pidFile.FullName -ErrorAction SilentlyContinue | Select-Object -First 1)
        $parsedPid = 0
        if (-not [int]::TryParse([string]$rawPid, [ref]$parsedPid)) {
            continue
        }

        $process = Get-Process -Id $parsedPid -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            continue
        }

        [PSCustomObject]@{
            PidFile = $pidFile.FullName
            Process = $process
        }
    }

    return @($processes)
}

function Start-OrderedBatch {
    param(
        [Parameter(Mandatory = $true)][hashtable]$State
    )

    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $stdoutPath = Join-Path $logsRoot ("ordered_indexing_batch_supervised_{0}_stdout.log" -f $stamp)
    $stderrPath = Join-Path $logsRoot ("ordered_indexing_batch_supervised_{0}_stderr.log" -f $stamp)
    $reportPath = Join-Path $logsRoot ("ordered_indexing_batch_supervised_{0}.md" -f $stamp)
    $pidPath = Join-Path $logsRoot "ordered_indexing_batch_supervised.pid"
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source

    $process = Start-Process -FilePath $pwsh `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $orderedIndexScript, "-WorkspaceRoot", $helperRoot, "-ApiBaseUrl", $ApiBaseUrl, "-ApiKey", $ApiKey, "-PipelineVersion", $pipelineVersion, "-ReportPath", $reportPath) `
        -WorkingDirectory $helperRoot `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru

    Set-Content -Path $pidPath -Value $process.Id -Encoding UTF8
    $State.LastBatchReportPath = $reportPath

    return [PSCustomObject]@{
        ProcessId = $process.Id
        ReportPath = $reportPath
        StdoutPath = $stdoutPath
        StderrPath = $stderrPath
        PidPath = $pidPath
    }
}

while ($true) {
    $cycleStarted = Get-Date
    $reportLines = New-Object System.Collections.Generic.List[string]
    $reportLines.Add(('- StartedAt: `{0}`' -f $cycleStarted.ToString("s"))) | Out-Null

    try {
        $state = Load-State
        $api = Ensure-ApiReady
        $status = $api.Status
        $queue = Read-Queue
        $summary = Get-QueueSummary -Queue $queue
        $collections = Get-QdrantCollections
        $actions = New-Object System.Collections.Generic.List[string]

        if ($api.Restarted) {
            $actions.Add("api restarted after unreachable probe") | Out-Null
        }

        if ($summary.Done.Count -gt 0) {
            foreach ($doneRow in $summary.Done) {
                if ($state.RetryCounts.ContainsKey($doneRow.Key)) {
                    $state.RetryCounts.Remove($doneRow.Key)
                }
            }
        }

        if ($summary.Processing.Count -gt 0 -and (-not $status.isIndexing)) {
            foreach ($action in (Recover-OrphanedProcessing -Queue $queue -ProcessingRows $summary.Processing -Collections $collections)) {
                $actions.Add($action) | Out-Null
            }
        }

        if ($summary.Errors.Count -gt 0) {
            foreach ($action in (Recover-ErrorRows -Queue $queue -ErrorRows $summary.Errors -State $state -Collections $collections)) {
                $actions.Add($action) | Out-Null
            }
        }

        if ($actions.Count -gt 0) {
            Write-Queue -Queue $queue
            $summary = Get-QueueSummary -Queue $queue
        }

        $activeBatches = Get-ActiveBatchProcesses
        if (@($activeBatches).Count -eq 0 -and $summary.Pending.Count -gt 0) {
            if ($status.isEvolution) {
                try {
                    [void](Invoke-ApiPost -RelativePath "/api/evolution/stop")
                    $actions.Add("stopped evolution before restarting indexing batch") | Out-Null
                }
                catch {
                    $actions.Add(("failed to stop evolution: {0}" -f $_.Exception.Message)) | Out-Null
                }
            }

            $launch = Start-OrderedBatch -State $state
            $actions.Add(("started ordered indexing batch pid={0}" -f $launch.ProcessId)) | Out-Null
            $actions.Add(("batch report: {0}" -f $launch.ReportPath)) | Out-Null
            $activeBatches = Get-ActiveBatchProcesses
        }

        Save-State -State $state

        $reportLines.Add(('- ApiPhase: `{0}`' -f $status.currentPhase)) | Out-Null
        $reportLines.Add(('- ApiIsIndexing: `{0}`' -f [bool]$status.isIndexing)) | Out-Null
        $reportLines.Add(('- ApiIsEvolution: `{0}`' -f [bool]$status.isEvolution)) | Out-Null
        $reportLines.Add(('- PipelineVersion: `{0}`' -f [string]$status.pipelineVersion)) | Out-Null
        $reportLines.Add(('- ChunkingStrategy: `{0}`' -f [string]$status.chunkingStrategy)) | Out-Null
        $reportLines.Add(('- CurrentSection: `{0}`' -f [string]$status.currentSection)) | Out-Null
        $reportLines.Add(('- ActiveTask: `{0}`' -f [string]$status.activeTask)) | Out-Null
        $reportLines.Add(('- QueueDone: `{0}`' -f @($summary.Done).Count)) | Out-Null
        $reportLines.Add(('- QueuePending: `{0}`' -f @($summary.Pending).Count)) | Out-Null
        $reportLines.Add(('- QueueProcessing: `{0}`' -f @($summary.Processing).Count)) | Out-Null
        $reportLines.Add(('- QueueErrors: `{0}`' -f @($summary.Errors).Count)) | Out-Null
        $reportLines.Add(('- ActiveBatches: `{0}`' -f @($activeBatches).Count)) | Out-Null

        $reportLines.Add("") | Out-Null
        $reportLines.Add("### Actions") | Out-Null
        if ($actions.Count -eq 0) {
            $reportLines.Add("- none") | Out-Null
        }
        else {
            foreach ($action in $actions) {
                $reportLines.Add(("- {0}" -f $action)) | Out-Null
            }
        }

        if (@($summary.Errors).Count -gt 0) {
            $reportLines.Add("") | Out-Null
            $reportLines.Add("### Remaining Errors") | Out-Null
            foreach ($row in $summary.Errors) {
                $reportLines.Add(('- `{0}` => `{1}`' -f $row.Key, $row.Value)) | Out-Null
            }
        }
    }
    catch {
        $reportLines.Add(('- SupervisorError: `{0}`' -f $_.Exception.Message)) | Out-Null
    }

    Write-ReportSection -Title $cycleStarted.ToString("yyyy-MM-dd HH:mm:ss") -Lines $reportLines
    Start-Sleep -Seconds ([Math]::Max(60, $CycleMinutes * 60))
}
