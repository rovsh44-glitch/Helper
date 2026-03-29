param(
    [string]$MarkdownPath = "doc/parity_evidence/LOCAL_FIRST_LIBRARIAN_300_CASE_MATRIX_AND_PROMPTS_2026-03-22.md",
    [string]$OutputPath = "eval/web_research_parity/local_first_librarian_300_case_corpus.jsonl",
    [string]$SlicesOutputDirectory = "eval/web_research_parity/local_first_librarian_slices"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\LocalFirstBenchmarkSlices.ps1")

function Convert-ToSlug {
    param(
        [Parameter(Mandatory = $true)][string]$Value
    )

    $normalized = $Value.ToLowerInvariant()
    $normalized = [regex]::Replace($normalized, "[^a-z0-9]+", "_")
    $normalized = [regex]::Replace($normalized, "_{2,}", "_")
    return $normalized.Trim("_")
}

function New-CaseMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$EvidenceMode
    )

    switch ($EvidenceMode) {
        "local_sufficient" {
            return [ordered]@{
                localFirst = "required"
                web = "forbid_unless_local_insufficient"
                sources = "must_display_all_used_sources"
                analysis = "must_compare_local_and_web_evidence_when_web_is_used"
                conclusion = "must_provide_clear_conclusion"
                opinion = "must_provide_separate_labeled_opinion"
                minWebSources = 0
                labels = @(
                    "local_sufficient",
                    "sources_required",
                    "analysis_required",
                    "conclusion_required",
                    "opinion_required"
                )
            }
        }
        "local_plus_web" {
            return [ordered]@{
                localFirst = "required"
                web = "required_as_supplement_after_local_pass"
                sources = "must_display_all_used_sources"
                analysis = "must_compare_local_findings_with_current_external_sources"
                conclusion = "must_provide_clear_conclusion"
                opinion = "must_provide_separate_labeled_opinion"
                minWebSources = 2
                labels = @(
                    "web_supplement",
                    "sources_required",
                    "analysis_required",
                    "conclusion_required",
                    "opinion_required"
                )
            }
        }
        "web_required_fresh" {
            return [ordered]@{
                localFirst = "required"
                web = "mandatory_due_to_freshness_or_current_availability"
                sources = "must_display_all_used_sources"
                analysis = "must_integrate_local_baseline_with_fresh_external_evidence"
                conclusion = "must_provide_clear_conclusion"
                opinion = "must_provide_separate_labeled_opinion"
                minWebSources = 3
                labels = @(
                    "web_required",
                    "freshness_sensitive",
                    "sources_required",
                    "analysis_required",
                    "conclusion_required",
                    "opinion_required"
                )
            }
        }
        "conflict_check" {
            return [ordered]@{
                localFirst = "required"
                web = "mandatory_for_multi_source_reconciliation"
                sources = "must_display_all_used_sources"
                analysis = "must_explicitly_compare_and_reconcile_conflicting_sources"
                conclusion = "must_provide_clear_conclusion"
                opinion = "must_provide_separate_labeled_opinion"
                minWebSources = 3
                labels = @(
                    "contradiction_check",
                    "sources_required",
                    "analysis_required",
                    "conclusion_required",
                    "opinion_required"
                )
            }
        }
        "uncertain_sparse" {
            return [ordered]@{
                localFirst = "required"
                web = "recommended_to_test_evidence_boundaries"
                sources = "must_display_all_used_sources"
                analysis = "must_state_uncertainty_and_evidence_limits_explicitly"
                conclusion = "must_provide_cautious_conclusion"
                opinion = "must_provide_separate_labeled_opinion_with_limits"
                minWebSources = 2
                labels = @(
                    "uncertain_evidence",
                    "sources_required",
                    "analysis_required",
                    "conclusion_required",
                    "opinion_required"
                )
            }
        }
        default {
            throw "Unsupported evidence mode '$EvidenceMode'."
        }
    }
}

function New-TaskLabels {
    param(
        [Parameter(Mandatory = $true)][string]$TaskType
    )

    switch ($TaskType) {
        "explain_and_structure" { return @("task_explain_and_structure") }
        "compare_and_choose" { return @("task_compare_and_choose") }
        "plan_actions" { return @("task_plan_actions") }
        "review_diagnose_or_critique" { return @("task_review_diagnose_or_critique") }
        default { throw "Unsupported task type '$TaskType'." }
    }
}

if (-not (Test-Path -LiteralPath $MarkdownPath)) {
    throw "Markdown source not found: $MarkdownPath"
}

$headingRegex = '^###\s+D\d+\.\s+(?<title>.+?)\s*$'
$promptRegex = '^- `(?<id>LFWR-\d{3}) \| (?<task>[a-z_]+) \| (?<evidence>[a-z_]+) \| (?<prompt>.+)`$'
$responseSections = @(
    "local_findings",
    "web_findings",
    "sources",
    "analysis",
    "conclusion",
    "opinion"
)

$lines = Get-Content -LiteralPath $MarkdownPath -Encoding UTF8
$cases = New-Object System.Collections.Generic.List[object]
$currentDomainTitle = ""
$currentDomainSlug = ""

foreach ($line in $lines) {
    if ($line -match $headingRegex) {
        $currentDomainTitle = $Matches["title"].Trim()
        $currentDomainSlug = Convert-ToSlug -Value $currentDomainTitle
        continue
    }

    if ($line -notmatch $promptRegex) {
        continue
    }

    if ([string]::IsNullOrWhiteSpace($currentDomainSlug)) {
        throw "Encountered prompt before domain heading: $line"
    }

    $originalId = $Matches["id"].Trim()
    $taskType = $Matches["task"].Trim()
    $evidenceMode = $Matches["evidence"].Trim()
    $prompt = $Matches["prompt"].Trim()
    $metadata = New-CaseMetadata -EvidenceMode $evidenceMode
    $labels = New-Object System.Collections.Generic.List[string]
    $labels.Add("ru_only")
    $labels.Add("local_first")
    $labels.AddRange([string[]](New-TaskLabels -TaskType $taskType))
    $labels.AddRange([string[]]$metadata.labels)
    $labels.Add("domain_$currentDomainSlug")

    $case = [ordered]@{
        id = $originalId.ToLowerInvariant()
        externalId = $originalId
        language = "ru"
        kind = "local_first_librarian_case"
        benchmarkPolicy = "local_first_librarian_v1"
        domain = $currentDomainSlug
        domainTitle = $currentDomainTitle
        taskType = $taskType
        evidenceMode = $evidenceMode
        prompt = $prompt
        endToEnd = $true
        labels = [string[]]$labels
        localFirst = $metadata.localFirst
        web = $metadata.web
        sources = $metadata.sources
        analysis = $metadata.analysis
        conclusion = $metadata.conclusion
        opinion = $metadata.opinion
        minWebSources = [int]$metadata.minWebSources
        responseSections = $responseSections
    }

    $sliceIds = @(Get-LocalFirstBenchmarkSliceIds -Case $case)
    $case["sliceIds"] = $sliceIds
    $case["sliceLabels"] = @($sliceIds | ForEach-Object { "slice_$_" })
    foreach ($sliceLabel in $case["sliceLabels"]) {
        $case["labels"] = @($case["labels"]) + [string]$sliceLabel
    }

    $cases.Add($case)
}

if ($cases.Count -ne 300) {
    throw "Expected 300 cases, found $($cases.Count)."
}

$duplicateIds = [string[]]@(
    $cases |
        Group-Object -Property id |
        Where-Object { $_.Count -gt 1 } |
        Select-Object -ExpandProperty Name |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)

if ($duplicateIds.Count -gt 0) {
    throw "Duplicate case ids: $($duplicateIds -join ', ')"
}

$outputDirectory = Split-Path -Path $OutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$jsonLines = $cases | ForEach-Object {
    $_ | ConvertTo-Json -Depth 8 -Compress
}

Set-Content -LiteralPath $OutputPath -Value $jsonLines -Encoding UTF8

if (-not [string]::IsNullOrWhiteSpace($SlicesOutputDirectory)) {
    New-Item -ItemType Directory -Force -Path $SlicesOutputDirectory | Out-Null

    $sliceCatalog = @(Get-LocalFirstBenchmarkSliceCatalog)
    foreach ($slice in $sliceCatalog) {
        $sliceId = [string]$slice.id
        $sliceCases = @($cases | Where-Object { @($_.sliceIds) -contains $sliceId })
        $slicePath = Join-Path $SlicesOutputDirectory ("{0}.jsonl" -f $sliceId)
        $sliceLines = $sliceCases | ForEach-Object { $_ | ConvertTo-Json -Depth 8 -Compress }
        Set-Content -LiteralPath $slicePath -Value $sliceLines -Encoding UTF8
    }

    $sliceCatalogPath = Join-Path $SlicesOutputDirectory "slice_catalog.json"
    $sliceCatalogPayload = @(
        $sliceCatalog | ForEach-Object {
            $sliceId = [string]$_.id
            [ordered]@{
                id = $sliceId
                title = [string]$_.title
                description = [string]$_.description
                caseCount = @($cases | Where-Object { @($_.sliceIds) -contains $sliceId }).Count
                path = [System.IO.Path]::GetFullPath((Join-Path $SlicesOutputDirectory ("{0}.jsonl" -f $sliceId)))
            }
        }
    )
    $sliceCatalogPayload | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $sliceCatalogPath -Encoding UTF8
}

Write-Host "[LocalFirstCorpus] Generated $($cases.Count) cases -> $OutputPath"
