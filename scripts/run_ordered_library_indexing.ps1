[CmdletBinding()]
param(
    [string]$WorkspaceRoot = ".",
    [string]$ApiBaseUrl = "http://127.0.0.1:5000",
    [string]$ApiKey = "",
    [string]$PipelineVersion = "",
    [int]$PollIntervalSec = 15,
    [int]$StallThresholdSec = 600,
    [string]$ReportPath = "",
    [bool]$AppendDiscoveredDomains = $true,
    [string[]]$DomainOrder = @(
        "social_sciences",
        "analysis_strategy",
        "art_culture",
        "medicine",
        "philosophy",
        "programming",
        "math",
        "physics",
        "biology",
        "anatomy",
        "neuro",
        "chemistry",
        "robotics",
        "geology",
        "virology",
        "entomology",
        "history",
        "encyclopedias",
        "russian_lang_lit",
        "english_lang_lit",
        "sci_fi_concepts"
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

$pathConfig = Get-HelperPathConfig -WorkspaceRoot $WorkspaceRoot
$helperRoot = $pathConfig.HelperRoot
$libraryDocsRoot = Join-Path $pathConfig.LibraryRoot "docs"
$queuePath = Join-Path $pathConfig.DataRoot "indexing_queue.json"

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = $env:HELPER_API_KEY
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "[OrderedIndex] HELPER_API_KEY is required."
}

if ([string]::IsNullOrWhiteSpace($PipelineVersion)) {
    $PipelineVersion = $env:HELPER_INDEX_PIPELINE_VERSION
}

if ([string]::IsNullOrWhiteSpace($PipelineVersion)) {
    $PipelineVersion = "v2"
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $ReportPath = Join-Path $pathConfig.LogsRoot ("ordered_indexing_" + $stamp + ".md")
}

function Resolve-IndexableExtensions {
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

    return @($extensions | Where-Object { -not $excluded.ContainsKey($_.ToLowerInvariant()) })
}

function Resolve-ExcludedFilePaths {
    param(
        [Parameter(Mandatory = $true)][string]$LibraryRoot
    )

    $paths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($token in @(($env:HELPER_INDEX_EXCLUDED_FILES -split '[;\r\n]') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $candidate = $token.Trim().Trim('"')
        if (-not [System.IO.Path]::IsPathRooted($candidate)) {
            $candidate = Join-Path $LibraryRoot $candidate
        }

        [void]$paths.Add([System.IO.Path]::GetFullPath($candidate))
    }

    return $paths
}

$indexableExtensions = Resolve-IndexableExtensions
$excludedFilePaths = Resolve-ExcludedFilePaths -LibraryRoot $pathConfig.LibraryRoot

if ($AppendDiscoveredDomains -and (Test-Path -LiteralPath $libraryDocsRoot)) {
    $discoveredDomains = @(Get-ChildItem -LiteralPath $libraryDocsRoot -Directory | Sort-Object Name | ForEach-Object { $_.Name })
    $mergedDomainOrder = New-Object System.Collections.Generic.List[string]
    foreach ($domain in @($DomainOrder) + $discoveredDomains) {
        if ([string]::IsNullOrWhiteSpace($domain)) {
            continue
        }

        if (-not $mergedDomainOrder.Contains($domain)) {
            $mergedDomainOrder.Add($domain)
        }
    }

    $DomainOrder = $mergedDomainOrder.ToArray()
}

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Ordered Library Indexing")
$reportLines.Add("")
$reportLines.Add(('- StartedAt: `{0}`' -f (Get-Date).ToString("s")))
$reportLines.Add(('- ApiBaseUrl: `{0}`' -f $ApiBaseUrl))
$reportLines.Add(('- PipelineVersion: `{0}`' -f $PipelineVersion))
$reportLines.Add(('- LibraryDocsRoot: `{0}`' -f $libraryDocsRoot))
$reportLines.Add("")
$reportLines.Add("| Domain | Files | Pipeline | Schema | TargetCollection | Strategy | Result | Done | Errors | DurationSec | Note |")
$reportLines.Add("| --- | ---: | --- | --- | --- | --- | --- | ---: | ---: | ---: | --- |")
Set-Content -Path $ReportPath -Value ($reportLines -join "`r`n") -Encoding UTF8

function Write-ReportRow {
    param(
        [Parameter(Mandatory = $true)][string]$Domain,
        [Parameter(Mandatory = $true)][int]$Files,
        [Parameter(Mandatory = $true)][string]$Pipeline,
        [Parameter(Mandatory = $true)][string]$Schema,
        [Parameter(Mandatory = $true)][string]$TargetCollection,
        [Parameter(Mandatory = $true)][string]$Strategy,
        [Parameter(Mandatory = $true)][string]$Result,
        [Parameter(Mandatory = $true)][int]$Done,
        [Parameter(Mandatory = $true)][int]$Errors,
        [Parameter(Mandatory = $true)][double]$DurationSec,
        [Parameter(Mandatory = $true)][string]$Note
    )

    Add-Content -Path $ReportPath -Value ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} |" -f $Domain, $Files, $Pipeline, $Schema, $TargetCollection, $Strategy, $Result, $Done, $Errors, [math]::Round($DurationSec, 1), $Note) -Encoding UTF8
}

function Get-SchemaVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Pipeline
    )

    if ($Pipeline -eq "v2") {
        return "v2"
    }

    return "v1"
}

function Get-TargetCollection {
    param(
        [Parameter(Mandatory = $true)][string]$Domain,
        [Parameter(Mandatory = $true)][string]$Pipeline
    )

    if ([string]::IsNullOrWhiteSpace($Domain)) {
        return "-"
    }

    if ($Pipeline -eq "v2") {
        return "knowledge_{0}_v2" -f $Domain
    }

    return "knowledge_{0}" -f $Domain
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

function Get-DomainFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Domain
    )

    $domainPath = Join-Path $libraryDocsRoot $Domain
    if (-not (Test-Path -LiteralPath $domainPath)) {
        return @()
    }

    $matches = @(Get-ChildItem -LiteralPath $domainPath -Recurse -File |
        Where-Object { $_.Extension -in $indexableExtensions -and -not $excludedFilePaths.Contains($_.FullName) } |
        Sort-Object FullName)
    return ,$matches
}

function Get-DomainSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$Domain,
        [Parameter(Mandatory = $true)][System.IO.FileInfo[]]$Files,
        [Parameter(Mandatory = $true)][hashtable]$Queue
    )

    $rows = foreach ($file in $Files) {
        $status = if ($Queue.ContainsKey($file.FullName)) { [string]$Queue[$file.FullName] } else { "Missing" }
        [PSCustomObject]@{
            Path = $file.FullName
            Name = $file.Name
            Status = $status
        }
    }

    $done = @($rows | Where-Object { $_.Status -eq "Done" }).Count
    $pending = @($rows | Where-Object { $_.Status -eq "Pending" }).Count
    $processing = @($rows | Where-Object { $_.Status -eq "Processing" }).Count
    $errors = @($rows | Where-Object { $_.Status -like "Error*" }).Count
    $missing = @($rows | Where-Object { $_.Status -eq "Missing" }).Count
    $errorRows = @($rows | Where-Object { $_.Status -like "Error*" })

    return [PSCustomObject]@{
        Domain = $Domain
        Total = $Files.Count
        Done = $done
        Pending = $pending
        Processing = $processing
        Errors = $errors
        Missing = $missing
        ErrorRows = $errorRows
        Rows = $rows
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

function Get-ApiStatus {
    $uri = $ApiBaseUrl.TrimEnd("/") + "/api/evolution/status"
    return Invoke-RestMethod -Method Get -Uri $uri -Headers @{ "X-API-KEY" = $ApiKey } -TimeoutSec 30
}

function Force-Idle {
    try {
        [void](Invoke-ApiPost -RelativePath "/api/indexing/pause")
    }
    catch {
        Write-Warning ("[OrderedIndex] indexing/pause failed: {0}" -f $_.Exception.Message)
    }

    try {
        [void](Invoke-ApiPost -RelativePath "/api/evolution/stop")
    }
    catch {
        Write-Warning ("[OrderedIndex] evolution/stop failed: {0}" -f $_.Exception.Message)
    }
}

function Wait-ForDomainQuiescence {
    param(
        [Parameter(Mandatory = $true)][string]$Domain,
        [Parameter(Mandatory = $true)][System.IO.FileInfo[]]$Files,
        [int]$TimeoutSec = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $snapshot = Get-DomainSnapshot -Domain $Domain -Files $Files -Queue (Read-Queue)
        if ($snapshot.Processing -eq 0) {
            return $snapshot
        }

        Start-Sleep -Seconds ([Math]::Min([Math]::Max($PollIntervalSec, 1), 5))
    } while ((Get-Date) -lt $deadline)

    return Get-DomainSnapshot -Domain $Domain -Files $Files -Queue (Read-Queue)
}

$results = New-Object System.Collections.Generic.List[object]

foreach ($domain in $DomainOrder) {
    $files = Get-DomainFiles -Domain $domain
    if (@($files).Count -eq 0) {
        Write-Host ("[OrderedIndex] {0}: empty or missing, skipping." -f $domain)
        $result = [PSCustomObject]@{
            Domain = $domain
            Files = 0
            Pipeline = $PipelineVersion
            Schema = Get-SchemaVersion -Pipeline $PipelineVersion
            TargetCollection = "-"
            Strategy = "-"
            Result = "skipped_empty"
            Done = 0
            Errors = 0
            DurationSec = 0
            Note = "no indexable files"
        }
        $results.Add($result) | Out-Null
        Write-ReportRow -Domain $result.Domain -Files $result.Files -Pipeline $result.Pipeline -Schema $result.Schema -TargetCollection $result.TargetCollection -Strategy $result.Strategy -Result $result.Result -Done $result.Done -Errors $result.Errors -DurationSec $result.DurationSec -Note $result.Note
        continue
    }

    $initialSnapshot = Get-DomainSnapshot -Domain $domain -Files $files -Queue (Read-Queue)
    if ($initialSnapshot.Done -eq $initialSnapshot.Total -and $initialSnapshot.Errors -eq 0) {
        Write-Host ("[OrderedIndex] {0}: already done ({1}/{2})." -f $domain, $initialSnapshot.Done, $initialSnapshot.Total)
        $result = [PSCustomObject]@{
            Domain = $domain
            Files = $initialSnapshot.Total
            Pipeline = $PipelineVersion
            Schema = Get-SchemaVersion -Pipeline $PipelineVersion
            TargetCollection = Get-TargetCollection -Domain $domain -Pipeline $PipelineVersion
            Strategy = "-"
            Result = "already_done"
            Done = $initialSnapshot.Done
            Errors = 0
            DurationSec = 0
            Note = "queue already marked done"
        }
        $results.Add($result) | Out-Null
        Write-ReportRow -Domain $result.Domain -Files $result.Files -Pipeline $result.Pipeline -Schema $result.Schema -TargetCollection $result.TargetCollection -Strategy $result.Strategy -Result $result.Result -Done $result.Done -Errors $result.Errors -DurationSec $result.DurationSec -Note $result.Note
        continue
    }

    Write-Host ("[OrderedIndex] Starting domain {0} ({1} files)..." -f $domain, @($files).Count)
    [void](Invoke-ApiPost -RelativePath "/api/indexing/start" -Body @{ targetDomain = $domain })

    $startedAt = Get-Date
    $lastChangedAt = $startedAt
    $lastSignature = ""
    $stallDetected = $false
    $lastObservedPipeline = $PipelineVersion
    $lastObservedStrategy = "-"

    while ($true) {
        Start-Sleep -Seconds $PollIntervalSec

        $snapshot = Get-DomainSnapshot -Domain $domain -Files $files -Queue (Read-Queue)
        $status = Get-ApiStatus
        if (-not [string]::IsNullOrWhiteSpace([string]$status.pipelineVersion)) {
            $lastObservedPipeline = [string]$status.pipelineVersion
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$status.chunkingStrategy)) {
            $lastObservedStrategy = [string]$status.chunkingStrategy
        }
        $signature = "{0}|{1}|{2}|{3}|{4}|{5}" -f $snapshot.Done, $snapshot.Pending, $snapshot.Processing, $snapshot.Errors, $status.activeTask, $status.fileProgress
        if ($signature -ne $lastSignature) {
            $lastSignature = $signature
            $lastChangedAt = Get-Date
        }

        Write-Host ("[OrderedIndex] {0}: done={1}/{2}, processing={3}, errors={4}, active={5}, progress={6:N1}%" -f $domain, $snapshot.Done, $snapshot.Total, $snapshot.Processing, $snapshot.Errors, $status.activeTask, [double]$status.fileProgress)

        $isComplete = ($snapshot.Done + $snapshot.Errors -eq $snapshot.Total) -and $snapshot.Pending -eq 0 -and $snapshot.Processing -eq 0 -and $snapshot.Missing -eq 0
        if ($isComplete) {
            break
        }

        $stalledFor = ((Get-Date) - $lastChangedAt).TotalSeconds
        if ($stalledFor -ge $StallThresholdSec) {
            Write-Warning ("[OrderedIndex] {0}: stalled for {1} sec, stopping this domain." -f $domain, [int]$stalledFor)
            $stallDetected = $true
            break
        }
    }

    Force-Idle
    $finalSnapshot = Wait-ForDomainQuiescence -Domain $domain -Files $files -TimeoutSec 90
    $durationSec = ((Get-Date) - $startedAt).TotalSeconds

    $resultCode = "done"
    $note = "completed without queue errors"
    if ($finalSnapshot.Errors -gt 0) {
        $resultCode = "error"
        $errorSummary = @($finalSnapshot.ErrorRows | ForEach-Object { "{0}: {1}" -f $_.Name, $_.Status }) -join " || "
        $note = if ([string]::IsNullOrWhiteSpace($errorSummary)) { "file errors recorded" } else { $errorSummary }
    }
    elseif ($stallDetected -or $finalSnapshot.Done -lt $finalSnapshot.Total) {
        $resultCode = "incomplete"
        $note = if ($stallDetected) { "stopped after stall threshold; unfinished files remain pending" } else { "domain did not finish before stop" }
    }

    $result = [PSCustomObject]@{
        Domain = $domain
        Files = $finalSnapshot.Total
        Pipeline = $lastObservedPipeline
        Schema = Get-SchemaVersion -Pipeline $lastObservedPipeline
        TargetCollection = Get-TargetCollection -Domain $domain -Pipeline $lastObservedPipeline
        Strategy = $lastObservedStrategy
        Result = $resultCode
        Done = $finalSnapshot.Done
        Errors = $finalSnapshot.Errors
        DurationSec = [math]::Round($durationSec, 1)
        Note = $note
    }
    $results.Add($result) | Out-Null
    Write-ReportRow -Domain $result.Domain -Files $result.Files -Pipeline $result.Pipeline -Schema $result.Schema -TargetCollection $result.TargetCollection -Strategy $result.Strategy -Result $result.Result -Done $result.Done -Errors $result.Errors -DurationSec $result.DurationSec -Note $result.Note

    if ($resultCode -eq "error") {
        Write-Warning ("[OrderedIndex] {0}: errors detected." -f $domain)
    }
}

Add-Content -Path $ReportPath -Value "" -Encoding UTF8
Add-Content -Path $ReportPath -Value "## Final Summary" -Encoding UTF8
foreach ($result in $results) {
    Add-Content -Path $ReportPath -Value ("- {0}: {1} ({2}/{3}, errors={4})" -f $result.Domain, $result.Result, $result.Done, $result.Files, $result.Errors) -Encoding UTF8
}

$results
