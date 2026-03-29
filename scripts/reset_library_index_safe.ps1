[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string]$WorkspaceRoot = ".",
    [string]$ApiBaseUrl = "",
    [string]$QdrantBaseUrl = "http://127.0.0.1:6333",
    [string]$ApiKey = "",
    [string]$TargetDomain = "",
    [string]$TargetPath = "",
    [switch]$SkipStart,
    [switch]$OfflineReset,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

function Resolve-ApiBaseUrl {
    param(
        [Parameter(Mandatory = $true)][psobject]$PathConfig,
        [string]$ConfiguredApiBaseUrl
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredApiBaseUrl)) {
        return $ConfiguredApiBaseUrl.TrimEnd("/")
    }

    if (-not [string]::IsNullOrWhiteSpace($env:HELPER_RUNTIME_SMOKE_API_BASE)) {
        return $env:HELPER_RUNTIME_SMOKE_API_BASE.TrimEnd("/")
    }

    if (Test-Path -LiteralPath $PathConfig.ApiPortFile) {
        $portText = (Get-Content -LiteralPath $PathConfig.ApiPortFile -Raw).Trim()
        if ($portText -match '^\d+$') {
            return ("http://127.0.0.1:{0}" -f $portText)
        }
    }

    return "http://127.0.0.1:5000"
}

function Get-IndexableFiles {
    param(
        [Parameter(Mandatory = $true)][string]$LibraryDocsRoot
    )

    if (-not (Test-Path -LiteralPath $LibraryDocsRoot)) {
        throw "[LibraryIndex] Library docs root not found: $LibraryDocsRoot"
    }

    $extensions = @(
        ".pdf",
        ".epub",
        ".html",
        ".htm",
        ".docx",
        ".fb2",
        ".md",
        ".markdown",
        ".djvu",
        ".chm",
        ".zim"
    )
    $excluded = @{}
    foreach ($token in @(($env:HELPER_INDEX_EXCLUDED_EXTENSIONS -split '[,; ]') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $normalized = if ($token.StartsWith(".")) { $token.ToLowerInvariant() } else { "." + $token.ToLowerInvariant() }
        $excluded[$normalized] = $true
    }
    $extensions = @($extensions | Where-Object { -not $excluded.ContainsKey($_.ToLowerInvariant()) })

    $excludedPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($token in @(($env:HELPER_INDEX_EXCLUDED_FILES -split '[;\r\n]') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $candidate = $token.Trim().Trim('"')
        if (-not [System.IO.Path]::IsPathRooted($candidate)) {
            $candidate = Join-Path (Split-Path -Parent $LibraryDocsRoot) $candidate
        }

        [void]$excludedPaths.Add([System.IO.Path]::GetFullPath($candidate))
    }

    return @(Get-ChildItem -LiteralPath $LibraryDocsRoot -Recurse -File |
        Where-Object { $_.Extension -in $extensions -and -not $excludedPaths.Contains($_.FullName) } |
        Sort-Object FullName)
}

function Get-QueueSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$QueuePath,
        [Parameter(Mandatory = $true)][string]$LibraryDocsRoot,
        [Parameter(Mandatory = $true)][System.IO.FileInfo[]]$IndexableFiles
    )

    $result = [ordered]@{
        QueueCount = 0
        Done = 0
        Processing = 0
        Errors = 0
        StaleEntries = 0
        MissingFromQueue = 0
    }

    $indexableFileCount = @($IndexableFiles).Count

    if (-not (Test-Path -LiteralPath $QueuePath)) {
        $result.MissingFromQueue = $indexableFileCount
        return [PSCustomObject]$result
    }

    $raw = (Get-Content -LiteralPath $QueuePath -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($raw)) {
        $result.MissingFromQueue = $indexableFileCount
        return [PSCustomObject]$result
    }

    $queue = $raw | ConvertFrom-Json -AsHashtable
    if ($null -eq $queue) {
        $queue = @{}
    }

    $result.QueueCount = @($queue.Keys).Count

    $livePaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $IndexableFiles) {
        [void]$livePaths.Add($file.FullName)
    }

    $docsRootPrefix = [System.IO.Path]::GetFullPath($LibraryDocsRoot).TrimEnd('\') + '\'

    foreach ($entry in $queue.GetEnumerator()) {
        $key = [System.IO.Path]::GetFullPath([string]$entry.Key)
        $status = [string]$entry.Value

        if ($status -eq "Done") {
            $result.Done++
        }
        elseif ($status -eq "Processing") {
            $result.Processing++
        }
        elseif ($status -like "Error*") {
            $result.Errors++
        }

        if ($key.StartsWith($docsRootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and -not $livePaths.Contains($key)) {
            $result.StaleEntries++
        }
    }

    foreach ($file in $IndexableFiles) {
        if (-not $queue.ContainsKey($file.FullName)) {
            $result.MissingFromQueue++
        }
    }

    return [PSCustomObject]$result
}

function Get-ActivePreCertProcesses {
    $pattern = @(
        "run_precert_counted_day\.ps1",
        "run_precert_preview_day\.ps1",
        "run_precert_preview_sweep\.ps1",
        "run_day1_ordered_certification\.ps1",
        "run_eval_real_model\.ps1",
        "run_eval_gate\.ps1",
        "run_runtime_smoke\.ps1",
        "run_closed_loop_predictability\.ps1",
        "run_smoke_generation_compile_pass\.ps1",
        "certify_parity_14d\.ps1"
    ) -join "|"

    return @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -match $pattern } |
        Select-Object ProcessId, Name, CommandLine)
}

function Test-HttpJson {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [hashtable]$Headers = @{},
        [int]$TimeoutSec = 10
    )

    try {
        $response = Invoke-RestMethod -Method Get -Uri $Uri -Headers $Headers -TimeoutSec $TimeoutSec
        return [PSCustomObject]@{
            Reachable = $true
            Body = $response
            Error = ""
        }
    }
    catch {
        return [PSCustomObject]@{
            Reachable = $false
            Body = $null
            Error = $_.Exception.Message
        }
    }
}

function Backup-QueueFile {
    param(
        [Parameter(Mandatory = $true)][string]$QueuePath,
        [Parameter(Mandatory = $true)][string]$BackupRoot
    )

    if (-not (Test-Path -LiteralPath $QueuePath)) {
        return ""
    }

    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $backupPath = Join-Path $BackupRoot ("indexing_queue_" + $stamp + ".json")
    $backupDir = Split-Path -Parent $backupPath
    if (-not [string]::IsNullOrWhiteSpace($backupDir)) {
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    }

    if ($PSCmdlet.ShouldProcess($QueuePath, "Create queue backup at $backupPath")) {
        Copy-Item -LiteralPath $QueuePath -Destination $backupPath -Force
    }

    return $backupPath
}

function Invoke-ApiJsonPost {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [hashtable]$Headers = @{},
        [hashtable]$Body = @{},
        [int]$TimeoutSec = 60
    )

    $payload = if (@($Body.Keys).Count -gt 0) {
        $Body | ConvertTo-Json -Depth 8
    }
    else {
        "{}"
    }

    return Invoke-RestMethod -Method Post -Uri $Uri -Headers $Headers -Body $payload -ContentType "application/json" -TimeoutSec $TimeoutSec
}

function Resolve-TargetPath {
    param(
        [Parameter(Mandatory = $true)][string]$LibraryDocsRoot,
        [string]$ConfiguredTargetPath
    )

    if ([string]::IsNullOrWhiteSpace($ConfiguredTargetPath)) {
        return ""
    }

    $candidate = $ConfiguredTargetPath
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $LibraryDocsRoot $candidate
    }

    $full = [System.IO.Path]::GetFullPath($candidate)
    if (-not (Test-Path -LiteralPath $full)) {
        throw "[LibraryIndex] TargetPath not found: $full"
    }

    return $full
}

$pathConfig = Get-HelperPathConfig -WorkspaceRoot $WorkspaceRoot
$helperRoot = $pathConfig.HelperRoot
$libraryDocsRoot = Join-Path $pathConfig.LibraryRoot "docs"
$queuePath = Join-Path $pathConfig.DataRoot "indexing_queue.json"
$backupRoot = Join-Path $pathConfig.DataRoot "queue_backups"
$resolvedApiBaseUrl = Resolve-ApiBaseUrl -PathConfig $pathConfig -ConfiguredApiBaseUrl $ApiBaseUrl

$envFile = Join-Path $helperRoot ".env.local"
Import-HelperEnvFile -Path $envFile

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = $env:HELPER_API_KEY
}

$resolvedTargetPath = Resolve-TargetPath -LibraryDocsRoot $libraryDocsRoot -ConfiguredTargetPath $TargetPath

if (-not [string]::IsNullOrWhiteSpace($TargetDomain)) {
    $targetDomainPath = Join-Path $libraryDocsRoot $TargetDomain
    if (-not (Test-Path -LiteralPath $targetDomainPath)) {
        throw "[LibraryIndex] TargetDomain folder not found: $targetDomainPath"
    }
}

$activePreCert = Get-ActivePreCertProcesses
if (@($activePreCert).Count -gt 0 -and -not $Force.IsPresent) {
    $processList = ($activePreCert | ForEach-Object { ("PID {0} {1}" -f $_.ProcessId, $_.Name) }) -join "; "
    throw "[LibraryIndex] Active pre-cert related processes detected. Stop them first or rerun with -Force. Active: $processList"
}

$indexableFiles = Get-IndexableFiles -LibraryDocsRoot $libraryDocsRoot
if (@($indexableFiles).Count -eq 0) {
    throw "[LibraryIndex] No indexable files found under $libraryDocsRoot"
}

$queueSnapshot = Get-QueueSnapshot -QueuePath $queuePath -LibraryDocsRoot $libraryDocsRoot -IndexableFiles $indexableFiles

Write-Host ("[LibraryIndex] HelperRoot: {0}" -f $helperRoot)
Write-Host ("[LibraryIndex] LibraryDocsRoot: {0}" -f $libraryDocsRoot)
Write-Host ("[LibraryIndex] QueuePath: {0}" -f $queuePath)
Write-Host ("[LibraryIndex] LiveFiles: {0}" -f @($indexableFiles).Count)
Write-Host ("[LibraryIndex] QueueCount: {0}" -f $queueSnapshot.QueueCount)
Write-Host ("[LibraryIndex] QueueDone: {0}" -f $queueSnapshot.Done)
Write-Host ("[LibraryIndex] QueueProcessing: {0}" -f $queueSnapshot.Processing)
Write-Host ("[LibraryIndex] QueueErrors: {0}" -f $queueSnapshot.Errors)
Write-Host ("[LibraryIndex] QueueStaleEntries: {0}" -f $queueSnapshot.StaleEntries)
Write-Host ("[LibraryIndex] LiveFilesMissingFromQueue: {0}" -f $queueSnapshot.MissingFromQueue)

$backupPath = Backup-QueueFile -QueuePath $queuePath -BackupRoot $backupRoot
if (-not [string]::IsNullOrWhiteSpace($backupPath)) {
    Write-Host ("[LibraryIndex] Queue backup: {0}" -f $backupPath)
}

$apiProbe = Test-HttpJson -Uri ($resolvedApiBaseUrl.TrimEnd("/") + "/api/health")
$qdrantProbe = if ($SkipStart.IsPresent) {
    [PSCustomObject]@{ Reachable = $true; Body = $null; Error = "" }
}
else {
    Test-HttpJson -Uri ($QdrantBaseUrl.TrimEnd("/") + "/collections")
}

if ($apiProbe.Reachable) {
    if ([string]::IsNullOrWhiteSpace($ApiKey)) {
        throw "[LibraryIndex] API is reachable but HELPER_API_KEY is missing."
    }

    $headers = @{ "X-API-KEY" = $ApiKey }

    if ($OfflineReset.IsPresent) {
        Write-Warning "[LibraryIndex] OfflineReset was requested, but API is reachable. Using API reset for safety."
    }

    if ($PSCmdlet.ShouldProcess($resolvedApiBaseUrl, "POST /api/indexing/reset")) {
        [void](Invoke-ApiJsonPost -Uri ($resolvedApiBaseUrl.TrimEnd("/") + "/api/indexing/reset") -Headers $headers)
        Write-Host "[LibraryIndex] Reset completed through API."
    }

    if (-not $SkipStart.IsPresent) {
        if (-not $qdrantProbe.Reachable) {
            throw "[LibraryIndex] Qdrant is not reachable at $QdrantBaseUrl. Error: $($qdrantProbe.Error)"
        }

        $body = @{}
        if (-not [string]::IsNullOrWhiteSpace($TargetDomain)) {
            $body.targetDomain = $TargetDomain
        }
        if (-not [string]::IsNullOrWhiteSpace($resolvedTargetPath)) {
            $body.targetPath = $resolvedTargetPath
        }

        if ($PSCmdlet.ShouldProcess($resolvedApiBaseUrl, "POST /api/indexing/start")) {
            [void](Invoke-ApiJsonPost -Uri ($resolvedApiBaseUrl.TrimEnd("/") + "/api/indexing/start") -Headers $headers -Body $body)
            Start-Sleep -Seconds 2
            $status = Invoke-RestMethod -Method Get -Uri ($resolvedApiBaseUrl.TrimEnd("/") + "/api/evolution/status") -Headers $headers -TimeoutSec 15
            Write-Host ("[LibraryIndex] Start completed. Processed={0}/{1}; Phase={2}" -f $status.processedFiles, $status.totalFiles, $status.currentPhase)
        }
    }
}
else {
    if (-not $SkipStart.IsPresent -and -not $OfflineReset.IsPresent) {
        throw "[LibraryIndex] API is not reachable at $resolvedApiBaseUrl. Start the backend first, or rerun with -SkipStart -OfflineReset for queue-only reset."
    }

    if (-not (Test-Path -LiteralPath $queuePath)) {
        Write-Warning "[LibraryIndex] Queue file does not exist. Offline reset has nothing to delete."
    }
    elseif ($PSCmdlet.ShouldProcess($queuePath, "Delete queue file for offline reset")) {
        Remove-Item -LiteralPath $queuePath -Force
        Write-Host "[LibraryIndex] Offline queue reset completed."
    }

    if (-not $SkipStart.IsPresent) {
        throw "[LibraryIndex] Offline reset completed, but indexing was not started because the API is unavailable."
    }
}

[PSCustomObject]@{
    HelperRoot = $helperRoot
    LibraryDocsRoot = $libraryDocsRoot
    QueuePath = $queuePath
    QueueBackupPath = $backupPath
    ApiBaseUrl = $resolvedApiBaseUrl
    QdrantBaseUrl = $QdrantBaseUrl
    LiveFiles = @($indexableFiles).Count
    QueueCount = $queueSnapshot.QueueCount
    QueueStaleEntries = $queueSnapshot.StaleEntries
    QueueMissingFromLiveFiles = $queueSnapshot.MissingFromQueue
    Mode = if ($apiProbe.Reachable) { "api" } else { "offline" }
    Started = (-not $SkipStart.IsPresent) -and $apiProbe.Reachable
}
