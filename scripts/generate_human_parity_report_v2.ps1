param(
    [string]$InputCsv = "eval/human_eval_scores.csv",
    [string]$OutputReport = "doc/archive/top_level_history/human_eval_parity_report_latest.md",
    [string]$ReportTitle = "Human Blind-Eval Parity Report",
    [string]$HelperVariant = "Helper",
    [string]$BaselineVariant = "",
    [string]$EvidenceLevel = "synthetic",
    [string]$IntegrityReportPath = "",
    [string]$BlindEvalPackPath = "eval/human_eval_blind_pack.csv",
    [string]$BlindEvalManifestPath = "eval/human_eval_manifest.json",
    [string]$BlindEvalValidationPath = "",
    [string]$DatasetPath = "eval/human_level_parity_ru_en.jsonl",
    [string]$CollectionMode = "synthetic",
    [int]$MinDialogs = 200,
    [int]$MinUniqueReviewers = 2,
    [int]$MinReviewersPerDialog = 2,
    [switch]$FailOnThresholds,
    [switch]$SkipIntegrityValidation,
    [switch]$SkipBlindPackValidation
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\HumanEvalCommon.ps1")

$allowedEvidenceLevels = @("sample", "synthetic", "dry_run", "live_non_authoritative", "authoritative")
if ($allowedEvidenceLevels -notcontains $EvidenceLevel) {
    throw "[HumanParityReport] Invalid EvidenceLevel '$EvidenceLevel'. Allowed values: $($allowedEvidenceLevels -join ", ")."
}

function Get-OptionalObjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName,
        $DefaultValue = $null
    )

    if ($null -eq $InputObject) {
        return $DefaultValue
    }

    $property = $InputObject.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return $DefaultValue
    }

    return $property.Value
}

if ([string]::IsNullOrWhiteSpace($IntegrityReportPath)) {
    $IntegrityReportPath = [System.IO.Path]::ChangeExtension($OutputReport, ".integrity.json")
}

if ([string]::IsNullOrWhiteSpace($BlindEvalValidationPath)) {
    $BlindEvalValidationPath = [System.IO.Path]::ChangeExtension($OutputReport, ".blind_pack_validation.json")
}

$integrity = $null
if (-not $SkipIntegrityValidation.IsPresent) {
    $integrityMarkdownPath = [System.IO.Path]::ChangeExtension($IntegrityReportPath, ".md")
    & (Join-Path $PSScriptRoot "validate_human_eval_integrity.ps1") `
        -InputCsv $InputCsv `
        -OutputJsonPath $IntegrityReportPath `
        -OutputMarkdownPath $integrityMarkdownPath
    $integrity = Get-Content $IntegrityReportPath -Raw | ConvertFrom-Json
}

$blindPackValidation = $null
if (-not $SkipBlindPackValidation.IsPresent) {
    $useExistingBlindArtifacts = (Test-Path $BlindEvalPackPath) -and (Test-Path $BlindEvalManifestPath)
    if (-not $useExistingBlindArtifacts) {
        & (Join-Path $PSScriptRoot "export_blind_eval_pack.ps1") `
            -InputCsv $InputCsv `
            -DatasetPath $DatasetPath `
            -OutputPackCsv $BlindEvalPackPath `
            -OutputManifestPath $BlindEvalManifestPath `
            -CollectionMode $CollectionMode
    }

    $blindPackValidationMarkdownPath = [System.IO.Path]::ChangeExtension($BlindEvalValidationPath, ".md")
    & (Join-Path $PSScriptRoot "validate_blind_eval_pack.ps1") `
        -PackCsv $BlindEvalPackPath `
        -ManifestPath $BlindEvalManifestPath `
        -OutputJsonPath $BlindEvalValidationPath `
        -OutputMarkdownPath $blindPackValidationMarkdownPath
    $blindPackValidation = Get-Content $BlindEvalValidationPath -Raw | ConvertFrom-Json
}

$criteria = Get-HumanEvalCriteria
$rawRows = Import-Csv -Path $InputCsv
$rows = Import-HumanEvalNormalizedRows -InputCsv $InputCsv
$structuredNoteCoverage = Get-HumanEvalStructuredNoteCoverageSummary -Rows $rawRows
$structuredNoteCompletion = if ($structuredNoteCoverage.allRowsComplete) {
    "COMPLETE"
}
elseif ($structuredNoteCoverage.allColumnsPresent) {
    "PARTIAL"
}
else {
    "MISSING_COLUMNS"
}

$dialogCount = @($rows | Select-Object -ExpandProperty conversation_id | Sort-Object -Unique).Count
$uniqueReviewerCount = @($rows | Select-Object -ExpandProperty reviewer_id | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique).Count

$dialogsByCoverage = $rows | Group-Object conversation_id
$dialogsBelowReviewerMin = @(
    $dialogsByCoverage | Where-Object {
        (($_.Group | Select-Object -ExpandProperty reviewer_id | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique).Count) -lt $MinReviewersPerDialog
    }
)

$sampleSizeOk = $dialogCount -ge $MinDialogs
$uniqueReviewersOk = $uniqueReviewerCount -ge $MinUniqueReviewers
$perDialogCoverageOk = $dialogsBelowReviewerMin.Count -eq 0
$formatSufficient = $sampleSizeOk -and $uniqueReviewersOk -and $perDialogCoverageOk

$variants = $rows | Group-Object variant
$summaryRows = @()

foreach ($variantGroup in $variants) {
    $variantRows = $variantGroup.Group
    $entry = [ordered]@{
        variant = $variantGroup.Name
        samples = $variantRows.Count
    }

    foreach ($criterion in $criteria) {
        $values = [double[]]@($variantRows | Select-Object -ExpandProperty $criterion)
        $avg = if ($values.Count -eq 0) { 0.0 } else { ($values | Measure-Object -Average).Average }
        $entry[$criterion] = [math]::Round([double]$avg, 3)
    }

    $entry["overall"] = [math]::Round((($criteria | ForEach-Object { [double]$entry[$_] } | Measure-Object -Average).Average), 3)
    $summaryRows += [PSCustomObject]$entry
}

$helper = $summaryRows | Where-Object { $_.variant -ieq $HelperVariant } | Select-Object -First 1
if (-not $helper) {
    throw "[HumanParityReport] Helper variant '$HelperVariant' not found in CSV."
}

$baselineCandidates = $summaryRows | Where-Object { $_.variant -ine $helper.variant }
$baseline = $null
if (-not [string]::IsNullOrWhiteSpace($BaselineVariant)) {
    $baseline = $baselineCandidates | Where-Object { $_.variant -ieq $BaselineVariant } | Select-Object -First 1
    if (-not $baseline) {
        throw "[HumanParityReport] Baseline variant '$BaselineVariant' not found in CSV."
    }
}
else {
    $baseline = $baselineCandidates | Sort-Object overall -Descending | Select-Object -First 1
}

$helperUsefulnessOk = [double]$helper.usefulness -ge 4.3
$helperMinCategoryOk = (
    [double]$helper.clarity -ge 4.0 -and
    [double]$helper.empathy_appropriateness -ge 4.0 -and
    [double]$helper.usefulness -ge 4.0 -and
    [double]$helper.factuality -ge 4.0
)
$gapOk = $true
if ($baseline) {
    $gapTolerance = 0.2000001
    foreach ($criterion in $criteria) {
        if ([math]::Abs([double]$helper.$criterion - [double]$baseline.$criterion) -gt $gapTolerance) {
            $gapOk = $false
            break
        }
    }
}

$integrityStatus = if ($null -eq $integrity) { "SKIPPED" } else { [string](Get-OptionalObjectPropertyValue -InputObject $integrity -PropertyName "status" -DefaultValue "unknown") }
$reviewerDiversity = Get-OptionalObjectPropertyValue -InputObject $integrity -PropertyName "reviewerDiversity" -DefaultValue $null
$reviewerDiversityStatus = if ($null -eq $reviewerDiversity) { "SKIPPED" } else { [string](Get-OptionalObjectPropertyValue -InputObject $reviewerDiversity -PropertyName "status" -DefaultValue "unknown") }
$provenanceStatus = if ($null -eq $blindPackValidation) { "SKIPPED" } else { [string](Get-OptionalObjectPropertyValue -InputObject $blindPackValidation -PropertyName "status" -DefaultValue "unknown") }
$blindCollectionStatus = if ($null -eq $blindPackValidation) { "SKIPPED" } else { [string](Get-OptionalObjectPropertyValue -InputObject $blindPackValidation -PropertyName "collectionFlowStatus" -DefaultValue "unknown") }
$integritySufficient = ($integrityStatus -eq "PASS") -and ($reviewerDiversityStatus -eq "PASS") -and ($provenanceStatus -eq "PASS")
$authoritative = ($EvidenceLevel -eq "authoritative") -and $formatSufficient -and $integritySufficient

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
$lines = @()
$lines += "# $ReportTitle"
$lines += "Generated: $timestamp"
$lines += "Input: $InputCsv"
$lines += "Evidence level: $EvidenceLevel"
$lines += "Blind pack: $BlindEvalPackPath"
$lines += "Blind manifest: $BlindEvalManifestPath"
$lines += "Blind pack validation: $BlindEvalValidationPath"
$lines += "Integrity report: $IntegrityReportPath"
$lines += "Helper variant: $HelperVariant"
$lines += "Baseline variant: $(if ($baseline) { $baseline.variant } else { "n/a" })"
$lines += "Dialog sample size: $dialogCount (required >= $MinDialogs)"
$lines += "Unique reviewers: $uniqueReviewerCount (required >= $MinUniqueReviewers)"
$lines += "Dialogs with reviewer coverage >= ${MinReviewersPerDialog}: $($dialogCount - $dialogsBelowReviewerMin.Count)/$dialogCount"
$lines += "Format status: $(if ($formatSufficient) { "SUFFICIENT_FORMAT" } else { "INSUFFICIENT_FORMAT" })"
$lines += "Provenance status: $provenanceStatus"
$lines += "Blind collection status: $blindCollectionStatus"
$lines += "Reviewer diversity status: $reviewerDiversityStatus"
$lines += "Integrity status: $integrityStatus"
$lines += "Integrity sufficiency: $(if ($integritySufficient) { "SUFFICIENT_INTEGRITY" } else { "INSUFFICIENT_INTEGRITY" })"
$lines += "Structured note completion: $structuredNoteCompletion"
$lines += "Authoritative evidence: $(if ($authoritative) { "YES" } else { "NO" })"
$lines += ""
$lines += "| Variant | Samples | Clarity | Empathy | Usefulness | Factuality | Overall |"
$lines += "|---|---:|---:|---:|---:|---:|---:|"
foreach ($row in ($summaryRows | Sort-Object variant)) {
    $lines += "| $($row.variant) | $($row.samples) | $($row.clarity) | $($row.empathy_appropriateness) | $($row.usefulness) | $($row.factuality) | $($row.overall) |"
}

$lines += ""
$lines += "## Sample sufficiency checks"
$lines += "- Dialog count gate: $(if ($sampleSizeOk) { "PASS" } else { "FAIL" })"
$lines += "- Unique reviewer gate: $(if ($uniqueReviewersOk) { "PASS" } else { "FAIL" })"
$lines += "- Per-dialog reviewer coverage gate: $(if ($perDialogCoverageOk) { "PASS" } else { "FAIL" })"
if ($dialogsBelowReviewerMin.Count -gt 0) {
    $previewIds = @($dialogsBelowReviewerMin | Select-Object -ExpandProperty Name | Sort-Object | Select-Object -First 10)
    $lines += "- Coverage deficits (first 10): $($previewIds -join ", ")"
}

$lines += ""
$lines += "## Provenance and blindness"
$lines += "- Blind pack validation: $provenanceStatus"
$lines += "- Blind collection flow: $blindCollectionStatus"
if ($null -ne $blindPackValidation) {
    $provenanceFindings = @($blindPackValidation.findings)
    if ($provenanceFindings.Count -eq 0) {
        $lines += "- Blind-pack findings: none"
    }
    else {
        foreach ($finding in $provenanceFindings | Select-Object -First 8) {
            $lines += "- [$($finding.severity.ToUpperInvariant())] $($finding.code): $($finding.message)"
        }
    }
}

$lines += ""
$lines += "## Reviewer diversity"
$lines += "- Reviewer diversity status: $reviewerDiversityStatus"
if ($null -eq $reviewerDiversity) {
    $lines += "- Reviewer diversity metrics: unavailable"
}
else {
    $lines += "- Unique reviewers: $uniqueReviewerCount"
    $lines += "- Reviewer max share: $([math]::Round(([double](Get-OptionalObjectPropertyValue -InputObject $reviewerDiversity -PropertyName "maxReviewerShare" -DefaultValue 0.0)) * 100, 2))%"
    $taskFamilies = @((Get-OptionalObjectPropertyValue -InputObject $reviewerDiversity -PropertyName "taskFamilies" -DefaultValue @()))
    if ($taskFamilies.Count -eq 0) {
        $lines += "- Task-family reviewer spread: unavailable"
    }
    else {
        foreach ($family in $taskFamilies | Select-Object -First 8) {
            $lines += "- $($family.taskFamily): reviewers=$($family.reviewerCount), max_share=$([math]::Round([double]$family.maxReviewerShare * 100, 2))%"
        }
    }
}

$lines += ""
$lines += "## Evidence integrity"
$lines += "- Integrity validation: $integrityStatus"
if ($null -eq $integrity) {
    $lines += "- Integrity findings: unavailable"
}
else {
    $patternMetrics = @((Get-OptionalObjectPropertyValue -InputObject $integrity -PropertyName "patternMetrics" -DefaultValue @()))
    $mirroredDeltaMetrics = Get-OptionalObjectPropertyValue -InputObject $integrity -PropertyName "mirroredDeltaMetrics" -DefaultValue $null
    if ($patternMetrics.Count -gt 0) {
        foreach ($metric in $patternMetrics) {
            $lines += "- Variance summary [$($metric.variant)]: unique_pattern_ratio=$($metric.uniquePatternRatio), top_pattern_share=$($metric.topPatternShare), avg_stddev=$($metric.avgStdDev)"
        }
    }

    if ($null -ne $mirroredDeltaMetrics) {
        $lines += "- Suspicious pattern summary: unique_delta_ratio=$($mirroredDeltaMetrics.uniqueDeltaRatio), top_delta_share=$($mirroredDeltaMetrics.topDeltaShare)"
    }

    $findings = @($integrity.findings)
    if ($findings.Count -eq 0) {
        $lines += "- Integrity findings: none"
    }
    else {
        foreach ($finding in $findings | Select-Object -First 10) {
            $lines += "- [$($finding.severity.ToUpperInvariant())] $($finding.code): $($finding.message)"
        }
    }
}

$lines += ""
$lines += "## Structured Surface Notes"
$lines += "- Structured note completion: $structuredNoteCompletion"
$lines += "- Structured note columns present: $(if ($structuredNoteCoverage.allColumnsPresent) { "YES" } else { "NO" })"
$lines += "- Structured note rows fully filled: $(if ($structuredNoteCoverage.allRowsComplete) { "$($structuredNoteCoverage.totalRows)/$($structuredNoteCoverage.totalRows)" } else { "$(@($structuredNoteCoverage.columns | Measure-Object -Property filledRows -Minimum).Minimum)/$($structuredNoteCoverage.totalRows) minimum-per-column" })"
$lines += "| Note | Completion | Values |"
$lines += "|---|---:|---|"
foreach ($column in $structuredNoteCoverage.columns) {
    $valuePreview = if ($column.values.Count -eq 0) {
        "none"
    }
    else {
        @(
            $column.values |
                Select-Object -First 4 |
                ForEach-Object { "{0}={1}" -f $_.value, $_.count }
        ) -join "; "
    }

    $lines += "| $($column.name) | $([math]::Round([double]$column.completionRate * 100, 2))% | $valuePreview |"
}

$lines += ""
$lines += "## KPI checks"
if (-not $formatSufficient) {
    $lines += "- Helper usefulness >= 4.3: INSUFFICIENT_FORMAT"
    $lines += "- Helper each category >= 4.0: INSUFFICIENT_FORMAT"
    $lines += "- Gap vs selected baseline <= 0.2: INSUFFICIENT_FORMAT"
}
else {
    $lines += "- Helper usefulness >= 4.3: $(if ($helperUsefulnessOk) { "PASS" } else { "FAIL" })"
    $lines += "- Helper each category >= 4.0: $(if ($helperMinCategoryOk) { "PASS" } else { "FAIL" })"
    $lines += "- Gap vs selected baseline <= 0.2: $(if ($gapOk) { "PASS" } else { "FAIL" })"
}

Set-Content -Path $OutputReport -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[HumanParityReport] Report saved to $OutputReport"

if ($FailOnThresholds.IsPresent -and ($integrityStatus -eq "FAIL" -or $provenanceStatus -eq "FAIL" -or $reviewerDiversityStatus -eq "FAIL")) {
    throw "[HumanParityReport] Evidence integrity/provenance validation failed."
}

if ($FailOnThresholds.IsPresent -and $formatSufficient -and ((-not $helperUsefulnessOk) -or (-not $helperMinCategoryOk) -or (-not $gapOk))) {
    throw "[HumanParityReport] Threshold check failed."
}
