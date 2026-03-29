[CmdletBinding()]
param(
    [string]$ApiBaseUrl = "http://127.0.0.1:5000",
    [string]$ApiKey = "",
    [string]$QueriesPath = "",
    [string]$PipelineVersion = "",
    [string]$BaselinePipelineVersion = "v1",
    [string]$CandidatePipelineVersion = "v2",
    [int]$Limit = 5,
    [string]$OutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = $env:HELPER_API_KEY
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "HELPER_API_KEY is required."
}

if ([string]::IsNullOrWhiteSpace($QueriesPath)) {
    $QueriesPath = Join-Path $PSScriptRoot "..\doc\chunking_benchmark_queries_2026-03-10.json"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $OutputPath = Join-Path $PSScriptRoot ("..\doc\chunking_evaluation_report_{0}.md" -f $stamp)
}

function Get-RelevanceScore {
    param(
        [Parameter(Mandatory = $true)]$Item,
        [Parameter(Mandatory = $true)]$Query
    )

    $domain = [string]$Item.metadata.domain
    $title = [string]$Item.metadata.title
    $domainMatch = $domain -eq [string]$Query.expectedDomain
    $titleMatch = $title -like ("*" + [string]$Query.expectedTitleHint + "*")

    if ($domainMatch -and $titleMatch) { return 3 }
    if ($domainMatch -or $titleMatch) { return 2 }
    return 0
}

function Get-TraceabilityScore {
    param(
        [Parameter(Mandatory = $true)]$Item
    )

    $sectionPath = [string]$Item.metadata.section_path
    $pageStart = [string]$Item.metadata.page_start
    if (-not [string]::IsNullOrWhiteSpace($sectionPath) -and -not [string]::IsNullOrWhiteSpace($pageStart)) {
        return 1
    }

    return 0
}

function Get-Dcg {
    param(
        [int[]]$Relevances
    )

    $dcg = 0.0
    for ($i = 0; $i -lt $Relevances.Count; $i++) {
        $gain = [math]::Pow(2, $Relevances[$i]) - 1
        $discount = [math]::Log($i + 2, 2)
        $dcg += ($gain / $discount)
    }

    return $dcg
}

function Invoke-PipelineEvaluation {
    param(
        [Parameter(Mandatory = $true)][string]$ResolvedPipelineVersion,
        [Parameter(Mandatory = $true)]$QuerySet
    )

    $queryResults = New-Object System.Collections.Generic.List[object]
    foreach ($query in $QuerySet) {
        $body = @{
            query = $query.query
            limit = $Limit
            domain = $query.domain
            pipelineVersion = $ResolvedPipelineVersion
            includeContext = $true
        } | ConvertTo-Json -Depth 6

        $response = Invoke-RestMethod -Method Post -Uri ($ApiBaseUrl.TrimEnd("/") + "/api/rag/search") -Headers @{ "X-API-KEY" = $ApiKey } -Body $body -ContentType "application/json" -TimeoutSec 120
        $items = @($response)
        $scored = foreach ($item in $items) {
            [PSCustomObject]@{
                Relevance = Get-RelevanceScore -Item $item -Query $query
                Traceability = Get-TraceabilityScore -Item $item
                Item = $item
            }
        }

        $relevances = @($scored | ForEach-Object { [int]$_.Relevance })
        $relevantCount = @($scored | Where-Object { $_.Relevance -gt 0 }).Count
        $firstRelevant = @($scored | Where-Object { $_.Relevance -gt 0 } | Select-Object -First 1)
        $firstRelevantRank = if ($firstRelevant.Count -gt 0) { @($scored).IndexOf($firstRelevant[0]) + 1 } else { 0 }
        $rr = if ($firstRelevantRank -gt 0) { 1.0 / $firstRelevantRank } else { 0.0 }
        $dcg = Get-Dcg -Relevances $relevances
        $ideal = @($relevances | Sort-Object -Descending)
        $idcg = Get-Dcg -Relevances $ideal
        $ndcg = if ($idcg -gt 0) { $dcg / $idcg } else { 0.0 }
        $top1 = if ($items.Count -gt 0) { $items[0] } else { $null }
        $traceabilityAtK = if ($items.Count -gt 0) { (@($scored | Where-Object { $_.Traceability -gt 0 }).Count / [double]$items.Count) } else { 0.0 }

        $queryResults.Add([PSCustomObject]@{
            PipelineVersion = $ResolvedPipelineVersion
            Id = $query.id
            Domain = $query.domain
            Query = $query.query
            Top1Title = if ($null -eq $top1) { "" } else { [string]$top1.metadata.title }
            Top1Domain = if ($null -eq $top1) { "" } else { [string]$top1.metadata.domain }
            PrecisionAtK = if ($items.Count -gt 0) { $relevantCount / [double]$items.Count } else { 0.0 }
            RecallAtK = if ($relevantCount -gt 0) { 1.0 } else { 0.0 }
            MRR = $rr
            NdcgAtK = $ndcg
            TraceabilityAtK = $traceabilityAtK
            DomainHitAtK = @($items | Where-Object { $_.metadata.domain -eq $query.expectedDomain }).Count -gt 0
            TitleHintHitAtK = @($items | Where-Object { [string]$_.metadata.title -like ("*" + $query.expectedTitleHint + "*") }).Count -gt 0
        }) | Out-Null
    }

    return ,$queryResults
}

$queries = Get-Content -Raw -LiteralPath $QueriesPath | ConvertFrom-Json
$pipelineResults = New-Object System.Collections.Generic.List[object]
$pipelines = if (-not [string]::IsNullOrWhiteSpace($PipelineVersion)) {
    @($PipelineVersion)
}
else {
    @($BaselinePipelineVersion, $CandidatePipelineVersion) | Select-Object -Unique
}

foreach ($resolvedPipelineVersion in $pipelines) {
    foreach ($row in (Invoke-PipelineEvaluation -ResolvedPipelineVersion $resolvedPipelineVersion -QuerySet $queries)) {
        $pipelineResults.Add($row) | Out-Null
    }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Chunking Evaluation Report")
$lines.Add("")
$lines.Add(('- GeneratedAt: `{0}`' -f (Get-Date).ToString("s")))
$lines.Add(('- QueriesPath: `{0}`' -f (Resolve-Path $QueriesPath)))
$lines.Add(('- Pipelines: `{0}`' -f ($pipelines -join ", ")))
$lines.Add("")

foreach ($resolvedPipelineVersion in $pipelines) {
    $resultSet = @($pipelineResults | Where-Object { $_.PipelineVersion -eq $resolvedPipelineVersion })
    $summary = [PSCustomObject]@{
        Total = $resultSet.Count
        DomainHitAtK = @($resultSet | Where-Object { $_.DomainHitAtK }).Count
        TitleHintHitAtK = @($resultSet | Where-Object { $_.TitleHintHitAtK }).Count
        PrecisionAtK = [math]::Round((@($resultSet | Measure-Object -Property PrecisionAtK -Average).Average), 4)
        RecallAtK = [math]::Round((@($resultSet | Measure-Object -Property RecallAtK -Average).Average), 4)
        MRR = [math]::Round((@($resultSet | Measure-Object -Property MRR -Average).Average), 4)
        NdcgAtK = [math]::Round((@($resultSet | Measure-Object -Property NdcgAtK -Average).Average), 4)
        TraceabilityAtK = [math]::Round((@($resultSet | Measure-Object -Property TraceabilityAtK -Average).Average), 4)
    }

    $lines.Add(("## Pipeline `{0}`" -f $resolvedPipelineVersion))
    $lines.Add("")
    $lines.Add(('- TotalQueries: `{0}`' -f $summary.Total))
    $lines.Add(('- DomainHitAtK: `{0}`' -f $summary.DomainHitAtK))
    $lines.Add(('- TitleHintHitAtK: `{0}`' -f $summary.TitleHintHitAtK))
    $lines.Add(('- PrecisionAtK: `{0}`' -f $summary.PrecisionAtK))
    $lines.Add(('- RecallAtK: `{0}`' -f $summary.RecallAtK))
    $lines.Add(('- MRR: `{0}`' -f $summary.MRR))
    $lines.Add(('- NdcgAtK: `{0}`' -f $summary.NdcgAtK))
    $lines.Add(('- TraceabilityAtK: `{0}`' -f $summary.TraceabilityAtK))
    $lines.Add("")
    $lines.Add("| Id | Domain | Top1Domain | Top1Title | Precision@K | Recall@K | MRR | nDCG@K | Traceability@K |")
    $lines.Add("| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: |")
    foreach ($result in $resultSet) {
        $lines.Add(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |" -f $result.Id, $result.Domain, $result.Top1Domain, $result.Top1Title, ([math]::Round($result.PrecisionAtK, 4)), ([math]::Round($result.RecallAtK, 4)), ([math]::Round($result.MRR, 4)), ([math]::Round($result.NdcgAtK, 4)), ([math]::Round($result.TraceabilityAtK, 4))))
    }
    $lines.Add("")
}

if ($pipelines.Count -ge 2) {
    $baselineRows = @($pipelineResults | Where-Object { $_.PipelineVersion -eq $BaselinePipelineVersion })
    $candidateRows = @($pipelineResults | Where-Object { $_.PipelineVersion -eq $CandidatePipelineVersion })
    if ($baselineRows.Count -gt 0 -and $candidateRows.Count -gt 0) {
        $baselineMrr = @($baselineRows | Measure-Object -Property MRR -Average).Average
        $candidateMrr = @($candidateRows | Measure-Object -Property MRR -Average).Average
        $baselineNdcg = @($baselineRows | Measure-Object -Property NdcgAtK -Average).Average
        $candidateNdcg = @($candidateRows | Measure-Object -Property NdcgAtK -Average).Average
        $baselineTrace = @($baselineRows | Measure-Object -Property TraceabilityAtK -Average).Average
        $candidateTrace = @($candidateRows | Measure-Object -Property TraceabilityAtK -Average).Average
        $decision = if ($candidateMrr -ge $baselineMrr -and $candidateNdcg -ge $baselineNdcg -and $candidateTrace -ge $baselineTrace) { "go" } else { "review" }

        $lines.Add("## Comparison")
        $lines.Add("")
        $lines.Add("| Metric | $BaselinePipelineVersion | $CandidatePipelineVersion | Delta |")
        $lines.Add("| --- | ---: | ---: | ---: |")
        $lines.Add("| MRR | {0} | {1} | {2} |" -f ([math]::Round($baselineMrr, 4)), ([math]::Round($candidateMrr, 4)), ([math]::Round(($candidateMrr - $baselineMrr), 4)))
        $lines.Add("| nDCG@K | {0} | {1} | {2} |" -f ([math]::Round($baselineNdcg, 4)), ([math]::Round($candidateNdcg, 4)), ([math]::Round(($candidateNdcg - $baselineNdcg), 4)))
        $lines.Add("| Traceability@K | {0} | {1} | {2} |" -f ([math]::Round($baselineTrace, 4)), ([math]::Round($candidateTrace, 4)), ([math]::Round(($candidateTrace - $baselineTrace), 4)))
        $lines.Add("")
        $lines.Add(('- Decision: `{0}`' -f $decision))
        $lines.Add("")
    }
}

Set-Content -LiteralPath $OutputPath -Value ($lines -join "`r`n") -Encoding UTF8
$pipelineResults
