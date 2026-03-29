param(
    [string]$InputCsv = "eval/human_eval_scores.csv",
    [string]$OutputReport = "doc/archive/top_level_history/human_eval_parity_report_latest.md",
    [string]$HelperVariant = "Helper",
    [string]$BaselineVariant = "",
    [string]$EvidenceLevel = "synthetic",
    [string]$IntegrityReportPath = "",
    [int]$MinDialogs = 200,
    [int]$MinUniqueReviewers = 2,
    [int]$MinReviewersPerDialog = 2,
    [switch]$FailOnThresholds,
    [switch]$SkipIntegrityValidation
)

$ErrorActionPreference = "Stop"
$forwardedParameters = @{}
foreach ($entry in $PSBoundParameters.GetEnumerator()) {
    $forwardedParameters[$entry.Key] = $entry.Value
}
if (-not $forwardedParameters.ContainsKey("CollectionMode") -and ($forwardedParameters.ContainsKey("EvidenceLevel"))) {
    $forwardedParameters["CollectionMode"] = [string]$forwardedParameters["EvidenceLevel"]
}
& (Join-Path $PSScriptRoot "generate_human_parity_report_v2.ps1") @forwardedParameters
return

$allowedEvidenceLevels = @("sample", "synthetic", "dry_run", "live_non_authoritative", "authoritative")
if ($allowedEvidenceLevels -notcontains $EvidenceLevel) {
    throw "[HumanParityReport] Invalid EvidenceLevel '$EvidenceLevel'. Allowed values: $($allowedEvidenceLevels -join ", ")."
}

function Convert-ToScore {
    param(
        [Parameter(Mandatory = $true)]$RawValue,
        [Parameter(Mandatory = $true)][string]$Criterion,
        [Parameter(Mandatory = $true)][string]$ConversationId,
        [Parameter(Mandatory = $true)][string]$Variant
    )

    $rawText = [string]$RawValue
    if ([string]::IsNullOrWhiteSpace($rawText)) {
        throw "[HumanParityReport] Empty score for '$Criterion' in conversation '$ConversationId' variant '$Variant'."
    }

    $normalized = $rawText.Trim().Replace(",", ".")
    $parsed = 0.0
    if (-not [double]::TryParse($normalized, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        throw "[HumanParityReport] Invalid numeric score '$rawText' for '$Criterion' in conversation '$ConversationId' variant '$Variant'."
    }

    return $parsed
}

if (-not (Test-Path $InputCsv)) {
    throw "[HumanParityReport] Input CSV not found: $InputCsv"
}

if ([string]::IsNullOrWhiteSpace($IntegrityReportPath)) {
    $IntegrityReportPath = [System.IO.Path]::ChangeExtension($OutputReport, ".integrity.json")
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

$rows = Import-Csv -Path $InputCsv
if (-not $rows -or $rows.Count -eq 0) {
    throw "[HumanParityReport] Input CSV is empty."
}

$dialogCount = ($rows | Select-Object -ExpandProperty conversation_id | Sort-Object -Unique).Count
$uniqueReviewerCount = ($rows | Select-Object -ExpandProperty reviewer_id | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique).Count

$dialogsByCoverage = $rows | Group-Object conversation_id
$dialogsBelowReviewerMin = @(
    $dialogsByCoverage | Where-Object {
        (($_.Group | Select-Object -ExpandProperty reviewer_id | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique).Count) -lt $MinReviewersPerDialog
    }
)

$sampleSizeOk = $dialogCount -ge $MinDialogs
$uniqueReviewersOk = $uniqueReviewerCount -ge $MinUniqueReviewers
$perDialogCoverageOk = $dialogsBelowReviewerMin.Count -eq 0
$insufficientData = (-not $sampleSizeOk) -or (-not $uniqueReviewersOk) -or (-not $perDialogCoverageOk)

$criteria = @("clarity", "empathy_appropriateness", "usefulness", "factuality")
$requiredColumns = @("conversation_id", "variant", "language") + $criteria
foreach ($column in $requiredColumns) {
    if (-not ($rows[0].PSObject.Properties.Name -contains $column)) {
        throw "[HumanParityReport] Missing required column: $column"
    }
}

foreach ($row in $rows) {
    foreach ($criterion in $criteria) {
        $value = Convert-ToScore -RawValue $row.$criterion -Criterion $criterion -ConversationId ([string]$row.conversation_id) -Variant ([string]$row.variant)
        if ($value -lt 1 -or $value -gt 5) {
            throw "[HumanParityReport] Score out of range 1..5 for '$criterion' in conversation '$($row.conversation_id)' variant '$($row.variant)'."
        }
    }
}

$variants = $rows | Group-Object variant
$summaryRows = @()

foreach ($variantGroup in $variants) {
    $variantRows = $variantGroup.Group
    $entry = [ordered]@{
        variant = $variantGroup.Name
        samples = $variantRows.Count
    }

    foreach ($criterion in $criteria) {
        $values = $variantRows | ForEach-Object {
            Convert-ToScore -RawValue $_.$criterion -Criterion $criterion -ConversationId ([string]$_.conversation_id) -Variant ([string]$_.variant)
        }
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
    [double]$helper.factuality -ge 4.0)
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

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
$lines = @()
$lines += "# Human Blind-Eval Parity Report"
$lines += "Generated: $timestamp"
$lines += "Input: $InputCsv"
$lines += "Evidence level: $EvidenceLevel"
$lines += "Integrity report: $IntegrityReportPath"
$lines += "Helper variant: $HelperVariant"
$lines += "Baseline variant: $(if ($baseline) { $baseline.variant } else { "n/a" })"
$lines += "Dialog sample size: $dialogCount (required >= $MinDialogs)"
$lines += "Unique reviewers: $uniqueReviewerCount (required >= $MinUniqueReviewers)"
$lines += "Dialogs with reviewer coverage >= ${MinReviewersPerDialog}: $($dialogCount - $dialogsBelowReviewerMin.Count)/$dialogCount"
$lines += "Sample status: $(if ($insufficientData) { "INSUFFICIENT_DATA" } else { "SUFFICIENT" })"
$lines += "Integrity status: $(if ($null -eq $integrity) { "SKIPPED" } else { $integrity.status })"
$lines += "Authoritative evidence: $(if (($EvidenceLevel -eq "authoritative") -and ($null -ne $integrity) -and ($integrity.status -eq "PASS") -and (-not $insufficientData)) { "YES" } else { "NO" })"
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
$lines += "## Evidence integrity"
if ($null -eq $integrity) {
    $lines += "- Integrity validation: SKIPPED"
}
else {
    $lines += "- Integrity validation: $($integrity.status)"
    $findings = @($integrity.findings)
    if ($findings.Count -eq 0) {
        $lines += "- Integrity findings: none"
    }
    else {
        foreach ($finding in $findings | Select-Object -First 8) {
            $lines += "- [$($finding.severity.ToUpperInvariant())] $($finding.code): $($finding.message)"
        }
    }
}

$lines += ""
$lines += "## KPI checks"
if ($insufficientData) {
    $lines += "- Helper usefulness >= 4.3: INSUFFICIENT_DATA"
    $lines += "- Helper each category >= 4.0: INSUFFICIENT_DATA"
    $lines += "- Gap vs selected baseline <= 0.2: INSUFFICIENT_DATA"
}
else {
    $lines += "- Helper usefulness >= 4.3: $(if ($helperUsefulnessOk) { "PASS" } else { "FAIL" })"
    $lines += "- Helper each category >= 4.0: $(if ($helperMinCategoryOk) { "PASS" } else { "FAIL" })"
    $lines += "- Gap vs selected baseline <= 0.2: $(if ($gapOk) { "PASS" } else { "FAIL" })"
}

Set-Content -Path $OutputReport -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[HumanParityReport] Report saved to $OutputReport"

if ($FailOnThresholds.IsPresent -and ($null -ne $integrity) -and ($integrity.status -eq "FAIL")) {
    throw "[HumanParityReport] Integrity validation failed."
}

if ($FailOnThresholds.IsPresent -and (-not $insufficientData) -and ((-not $helperUsefulnessOk) -or (-not $helperMinCategoryOk) -or (-not $gapOk))) {
    throw "[HumanParityReport] Threshold check failed."
}
