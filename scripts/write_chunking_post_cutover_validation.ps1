[CmdletBinding()]
param(
    [string]$WorkspaceRoot = ".",
    [string]$QdrantBaseUrl = "http://127.0.0.1:6333",
    [string]$OutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $OutputPath = Join-Path $PSScriptRoot ("..\doc\chunking_post_cutover_validation_{0}.md" -f $stamp)
}

$paths = Get-HelperPathConfig -WorkspaceRoot $WorkspaceRoot
$queuePath = Join-Path $paths.DataRoot "indexing_queue.json"
$docsRoot = Join-Path $paths.LibraryRoot "docs"
$envFile = Join-Path $paths.HelperRoot ".env.local"
Import-HelperEnvFile -Path $envFile

$queue = if (Test-Path $queuePath) {
    Get-Content -Raw $queuePath | ConvertFrom-Json -AsHashtable
}
else {
    @{}
}

$total = @($queue.Keys).Count
$done = @($queue.GetEnumerator() | Where-Object { $_.Value -eq "Done" }).Count
$pending = @($queue.GetEnumerator() | Where-Object { $_.Value -eq "Pending" }).Count
$processing = @($queue.GetEnumerator() | Where-Object { $_.Value -eq "Processing" }).Count
$errors = @($queue.GetEnumerator() | Where-Object { [string]$_.Value -like "Error*" }).Count

$response = Invoke-RestMethod -Method Get -Uri ($QdrantBaseUrl.TrimEnd("/") + "/collections") -TimeoutSec 30
$collections = @($response.result.collections | ForEach-Object { [string]$_.name })
$v2Collections = @($collections | Where-Object { $_ -like "knowledge_*_v2" } | Sort-Object)
$v1Collections = @($collections | Where-Object { $_ -like "knowledge_*" -and $_ -notlike "*_v2" } | Sort-Object)
$expectedDomains = if (Test-Path $docsRoot) {
    @(Get-ChildItem -Path $docsRoot -Directory | ForEach-Object { $_.Name.Trim().ToLowerInvariant() } | Sort-Object -Unique)
}
else {
    @()
}

$domainQueueStats = @{}
foreach ($entry in $queue.GetEnumerator()) {
    $pathValue = [string]$entry.Key
    if ([string]::IsNullOrWhiteSpace($pathValue)) {
        continue
    }

    $relativePath = [System.IO.Path]::GetRelativePath($docsRoot, $pathValue)
    $parts = $relativePath -split "[\\/]"
    if ($parts.Length -eq 0 -or [string]::IsNullOrWhiteSpace($parts[0])) {
        continue
    }

    $domain = $parts[0].Trim().ToLowerInvariant()
    if (-not $domainQueueStats.ContainsKey($domain)) {
        $domainQueueStats[$domain] = [ordered]@{
            Total = 0
            Done = 0
            Pending = 0
            Processing = 0
            Errors = 0
        }
    }

    $domainQueueStats[$domain].Total++
    switch -Wildcard ([string]$entry.Value) {
        "Done" { $domainQueueStats[$domain].Done++ }
        "Pending" { $domainQueueStats[$domain].Pending++ }
        "Processing" { $domainQueueStats[$domain].Processing++ }
        "Error*" { $domainQueueStats[$domain].Errors++ }
    }
}

$fallbackRaw = [string]$env:HELPER_RAG_ALLOW_V1_FALLBACK
$fallbackEnabled = $false
[void][bool]::TryParse($fallbackRaw, [ref]$fallbackEnabled)
$fallbackDisabled = -not $fallbackEnabled
$pipelineVersion = if ([string]::IsNullOrWhiteSpace($env:HELPER_INDEX_PIPELINE_VERSION)) { "v1" } else { $env:HELPER_INDEX_PIPELINE_VERSION.Trim().ToLowerInvariant() }

$domainCoverage = foreach ($domain in $expectedDomains) {
    $collection = "knowledge_{0}_v2" -f $domain
    $stats = if ($domainQueueStats.ContainsKey($domain)) {
        $domainQueueStats[$domain]
    }
    else {
        [ordered]@{
            Total = 0
            Done = 0
            Pending = 0
            Processing = 0
            Errors = 0
        }
    }

    [PSCustomObject]@{
        Domain = $domain
        Collection = $collection
        Exists = $v2Collections -contains $collection
        Total = $stats.Total
        Done = $stats.Done
        Pending = $stats.Pending
        Processing = $stats.Processing
        Errors = $stats.Errors
    }
}

$allDomainsCovered = @($domainCoverage | Where-Object { -not $_.Exists }).Count -eq 0
$activeCorpusPass = $total -gt 0 -and
    $done -eq $total -and
    $pending -eq 0 -and
    $processing -eq 0 -and
    $errors -eq 0 -and
    $v2Collections.Count -gt 0 -and
    $pipelineVersion -eq "v2" -and
    $fallbackDisabled -and
    $allDomainsCovered

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Chunking Post-Cutover Validation")
$lines.Add("")
$lines.Add(('- GeneratedAt: `{0}`' -f (Get-Date).ToString("s")))
$lines.Add(('- QueuePath: `{0}`' -f $queuePath))
$lines.Add(('- DocsRoot: `{0}`' -f $docsRoot))
$lines.Add(('- TotalFiles: `{0}`' -f $total))
$lines.Add(('- Done: `{0}`' -f $done))
$lines.Add(('- Pending: `{0}`' -f $pending))
$lines.Add(('- Processing: `{0}`' -f $processing))
$lines.Add(('- Errors: `{0}`' -f $errors))
$lines.Add(('- V2Collections: `{0}`' -f $v2Collections.Count))
$lines.Add(('- V1CollectionsRetainedForRollback: `{0}`' -f $v1Collections.Count))
$lines.Add(('- ExpectedDomains: `{0}`' -f $expectedDomains.Count))
$lines.Add(('- DefaultIndexingPipeline: `{0}`' -f $pipelineVersion))
$lines.Add(('- DefaultRetrievalFallbackToV1: `{0}`' -f ($(if ($fallbackDisabled) { "disabled" } else { "enabled" }))))
$lines.Add("")
$lines.Add("## Validation")
$lines.Add("")
$lines.Add(('- ActiveCorpusUnderV2: `{0}`' -f ($(if ($activeCorpusPass) { "pass" } else { "fail" }))))
$lines.Add(('- DomainCoverage: `{0}`' -f ($(if ($allDomainsCovered) { "pass" } else { "fail" }))))
$lines.Add("")
$lines.Add("## V2 Collections")
$lines.Add("")
$lines.Add("| Collection |")
$lines.Add("| --- |")
foreach ($collection in $v2Collections) {
    $lines.Add(("| {0} |" -f $collection))
}

$lines.Add("")
$lines.Add("## Domain Coverage")
$lines.Add("")
$lines.Add("| Domain | ExpectedCollection | Exists | Total | Done | Pending | Processing | Errors |")
$lines.Add("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |")
foreach ($row in $domainCoverage) {
    $lines.Add(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |" -f $row.Domain, $row.Collection, ($(if ($row.Exists) { "yes" } else { "no" })), $row.Total, $row.Done, $row.Pending, $row.Processing, $row.Errors))
}

Set-Content -Path $OutputPath -Value ($lines -join "`r`n") -Encoding UTF8
[PSCustomObject]@{
    OutputPath = $OutputPath
    Total = $total
    Done = $done
    Errors = $errors
    V2Collections = $v2Collections.Count
    AllDomainsCovered = $allDomainsCovered
}
