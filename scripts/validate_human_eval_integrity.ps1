param(
    [string]$InputCsv = "eval/human_eval_scores.csv",
    [string]$OutputJsonPath = "doc/human_eval_parity_report_latest.integrity.json",
    [string]$OutputMarkdownPath = "",
    [switch]$FailOnViolation
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "validate_human_eval_integrity_v2.ps1") @PSBoundParameters
return

function Convert-ToScore {
    param(
        [Parameter(Mandatory = $true)]$RawValue,
        [Parameter(Mandatory = $true)][string]$Criterion,
        [Parameter(Mandatory = $true)][string]$ConversationId,
        [Parameter(Mandatory = $true)][string]$Variant
    )

    $rawText = [string]$RawValue
    if ([string]::IsNullOrWhiteSpace($rawText)) {
        throw "[HumanEvalIntegrity] Empty score for '$Criterion' in conversation '$ConversationId' variant '$Variant'."
    }

    $normalized = $rawText.Trim().Replace(",", ".")
    $parsed = 0.0
    if (-not [double]::TryParse($normalized, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        throw "[HumanEvalIntegrity] Invalid numeric score '$rawText' for '$Criterion' in conversation '$ConversationId' variant '$Variant'."
    }

    return $parsed
}

function Get-TaskFamily {
    param([string]$ConversationId)

    if ([string]::IsNullOrWhiteSpace($ConversationId)) {
        return "unknown"
    }

    $tokens = $ConversationId.Split("-", [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($tokens.Length -ge 3) {
        return $tokens[1].Trim().ToLowerInvariant()
    }

    if ($tokens.Length -ge 2) {
        return $tokens[0].Trim().ToLowerInvariant()
    }

    return "unknown"
}

function Measure-StandardDeviation {
    param([double[]]$Values)

    if ($null -eq $Values -or $Values.Count -le 1) {
        return 0.0
    }

    $avg = ($Values | Measure-Object -Average).Average
    $variance = ($Values | ForEach-Object {
        $delta = $_ - $avg
        $delta * $delta
    } | Measure-Object -Average).Average
    return [math]::Sqrt([double]$variance)
}

function Add-Finding {
    param(
        [System.Collections.Generic.List[object]]$Target,
        [string]$Severity,
        [string]$Code,
        [string]$Message
    )

    $Target.Add([PSCustomObject]@{
        severity = $Severity
        code = $Code
        message = $Message
    })
}

if (-not (Test-Path $InputCsv)) {
    throw "[HumanEvalIntegrity] Input CSV not found: $InputCsv"
}

$rows = Import-Csv -Path $InputCsv
if (-not $rows -or $rows.Count -eq 0) {
    throw "[HumanEvalIntegrity] Input CSV is empty."
}

$criteria = @("clarity", "empathy_appropriateness", "usefulness", "factuality")
$requiredColumns = @("conversation_id", "variant", "language", "reviewer_id") + $criteria
foreach ($column in $requiredColumns) {
    if (-not ($rows[0].PSObject.Properties.Name -contains $column)) {
        throw "[HumanEvalIntegrity] Missing required column: $column"
    }
}

$normalizedRows = foreach ($row in $rows) {
    $scores = [ordered]@{}
    foreach ($criterion in $criteria) {
        $scores[$criterion] = Convert-ToScore -RawValue $row.$criterion -Criterion $criterion -ConversationId ([string]$row.conversation_id) -Variant ([string]$row.variant)
    }

    [PSCustomObject]@{
        conversation_id = [string]$row.conversation_id
        variant = [string]$row.variant
        language = [string]$row.language
        reviewer_id = [string]$row.reviewer_id
        task_family = Get-TaskFamily -ConversationId ([string]$row.conversation_id)
        clarity = [double]$scores.clarity
        empathy_appropriateness = [double]$scores.empathy_appropriateness
        usefulness = [double]$scores.usefulness
        factuality = [double]$scores.factuality
        pattern_key = "{0:F2}|{1:F2}|{2:F2}|{3:F2}" -f $scores.clarity, $scores.empathy_appropriateness, $scores.usefulness, $scores.factuality
    }
}

$reviewerGroups = $normalizedRows | Group-Object reviewer_id
$reviewerRows = @(
    $reviewerGroups |
        Sort-Object Count -Descending |
        ForEach-Object {
            [PSCustomObject]@{
                reviewerId = $_.Name
                rows = $_.Count
                share = [math]::Round($_.Count / [double]$normalizedRows.Count, 4)
            }
        }
)
$maxReviewerShare = if ($reviewerRows.Count -eq 0) { 0.0 } else { [double]$reviewerRows[0].share }

$taskFamilyRows = @(
    $normalizedRows |
        Group-Object task_family |
        Sort-Object Count -Descending |
        ForEach-Object {
            [PSCustomObject]@{
                taskFamily = $_.Name
                rows = $_.Count
                share = [math]::Round($_.Count / [double]$normalizedRows.Count, 4)
            }
        }
)
$largestTaskFamilyShare = if ($taskFamilyRows.Count -eq 0) { 0.0 } else { [double]$taskFamilyRows[0].share }

$patternMetrics = @(
    $normalizedRows |
        Group-Object variant |
        Sort-Object Name |
        ForEach-Object {
            $variantRows = $_.Group
            $patternGroups = $variantRows | Group-Object pattern_key | Sort-Object Count -Descending
            $topPatternCount = if ($patternGroups.Count -eq 0) { 0 } else { $patternGroups[0].Count }
            $stddevs = [ordered]@{}
            foreach ($criterion in $criteria) {
                $stddevs[$criterion] = [math]::Round((Measure-StandardDeviation -Values ([double[]]@($variantRows | Select-Object -ExpandProperty $criterion))), 4)
            }

            [PSCustomObject]@{
                variant = $_.Name
                rows = $variantRows.Count
                uniquePatterns = $patternGroups.Count
                uniquePatternRatio = [math]::Round(($patternGroups.Count / [double]$variantRows.Count), 4)
                topPatternCount = $topPatternCount
                topPatternShare = [math]::Round(($topPatternCount / [double]$variantRows.Count), 4)
                stddev = [PSCustomObject]$stddevs
                avgStdDev = [math]::Round((($stddevs.Values | Measure-Object -Average).Average), 4)
            }
        }
)

$pairedRows = [System.Collections.Generic.List[object]]::new()
$pairGroups = $normalizedRows | Group-Object conversation_id, reviewer_id
foreach ($pairGroup in $pairGroups) {
    $helperRow = $pairGroup.Group | Where-Object { $_.variant -ieq "Helper" } | Select-Object -First 1
    $baselineRow = $pairGroup.Group | Where-Object { $_.variant -ine "Helper" } | Select-Object -First 1
    if ($null -eq $helperRow -or $null -eq $baselineRow) {
        continue
    }

    $deltaKey = "{0:F2}|{1:F2}|{2:F2}|{3:F2}" -f
        ($helperRow.clarity - $baselineRow.clarity),
        ($helperRow.empathy_appropriateness - $baselineRow.empathy_appropriateness),
        ($helperRow.usefulness - $baselineRow.usefulness),
        ($helperRow.factuality - $baselineRow.factuality)

    $pairedRows.Add([PSCustomObject]@{
        conversation_id = $helperRow.conversation_id
        reviewer_id = $helperRow.reviewer_id
        delta_key = $deltaKey
    })
}

$mirroredDeltaGroups = @($pairedRows | Group-Object delta_key | Sort-Object Count -Descending)
$mirroredDeltaMetrics = [PSCustomObject]@{
    pairedRows = $pairedRows.Count
    uniqueDeltaPatterns = $mirroredDeltaGroups.Count
    uniqueDeltaRatio = if ($pairedRows.Count -eq 0) { 0.0 } else { [math]::Round(($mirroredDeltaGroups.Count / [double]$pairedRows.Count), 4) }
    topDeltaShare = if ($pairedRows.Count -eq 0 -or $mirroredDeltaGroups.Count -eq 0) { 0.0 } else { [math]::Round(($mirroredDeltaGroups[0].Count / [double]$pairedRows.Count), 4) }
}

$findings = [System.Collections.Generic.List[object]]::new()

if ($reviewerRows.Count -lt 2) {
    Add-Finding -Target $findings -Severity "fail" -Code "reviewer_count_low" -Message "Only $($reviewerRows.Count) unique reviewer(s) present."
}
elseif ($maxReviewerShare -gt 0.80) {
    Add-Finding -Target $findings -Severity "fail" -Code "reviewer_concentration_high" -Message "Single reviewer share is $([math]::Round($maxReviewerShare * 100, 2))%, above the 80% fail threshold."
}
elseif ($maxReviewerShare -gt 0.65) {
    Add-Finding -Target $findings -Severity "warn" -Code "reviewer_concentration_warn" -Message "Single reviewer share is $([math]::Round($maxReviewerShare * 100, 2))%, above the 65% warning threshold."
}

if ($largestTaskFamilyShare -gt 0.70) {
    Add-Finding -Target $findings -Severity "fail" -Code "task_family_imbalance" -Message "Largest task family share is $([math]::Round($largestTaskFamilyShare * 100, 2))%, above the 70% fail threshold."
}
elseif ($largestTaskFamilyShare -gt 0.50) {
    Add-Finding -Target $findings -Severity "warn" -Code "task_family_balance_warn" -Message "Largest task family share is $([math]::Round($largestTaskFamilyShare * 100, 2))%, above the 50% warning threshold."
}

foreach ($metric in $patternMetrics) {
    if ($metric.uniquePatternRatio -lt 0.15) {
        Add-Finding -Target $findings -Severity "fail" -Code "pattern_uniqueness_low" -Message "Variant '$($metric.variant)' has unique pattern ratio $($metric.uniquePatternRatio), below the 0.15 fail threshold."
    }
    elseif ($metric.uniquePatternRatio -lt 0.25) {
        Add-Finding -Target $findings -Severity "warn" -Code "pattern_uniqueness_warn" -Message "Variant '$($metric.variant)' has unique pattern ratio $($metric.uniquePatternRatio), below the 0.25 warning threshold."
    }

    if ($metric.topPatternShare -gt 0.10) {
        Add-Finding -Target $findings -Severity "fail" -Code "pattern_repeat_high" -Message "Variant '$($metric.variant)' top score pattern share is $($metric.topPatternShare), above the 0.10 fail threshold."
    }
    elseif ($metric.topPatternShare -gt 0.06) {
        Add-Finding -Target $findings -Severity "warn" -Code "pattern_repeat_warn" -Message "Variant '$($metric.variant)' top score pattern share is $($metric.topPatternShare), above the 0.06 warning threshold."
    }

    if ($metric.avgStdDev -lt 0.12) {
        Add-Finding -Target $findings -Severity "fail" -Code "variance_too_low" -Message "Variant '$($metric.variant)' average score stddev is $($metric.avgStdDev), below the 0.12 fail threshold."
    }
    elseif ($metric.avgStdDev -lt 0.18) {
        Add-Finding -Target $findings -Severity "warn" -Code "variance_low_warn" -Message "Variant '$($metric.variant)' average score stddev is $($metric.avgStdDev), below the 0.18 warning threshold."
    }
}

if ($mirroredDeltaMetrics.pairedRows -eq 0) {
    Add-Finding -Target $findings -Severity "fail" -Code "variant_pairing_missing" -Message "No Helper/Baseline reviewer pairs were found for delta analysis."
}
elseif ($mirroredDeltaMetrics.uniqueDeltaRatio -lt 0.20) {
    Add-Finding -Target $findings -Severity "fail" -Code "mirrored_delta_low" -Message "Helper/Baseline delta pattern ratio is $($mirroredDeltaMetrics.uniqueDeltaRatio), below the 0.20 fail threshold."
}
elseif ($mirroredDeltaMetrics.uniqueDeltaRatio -lt 0.35) {
    Add-Finding -Target $findings -Severity "warn" -Code "mirrored_delta_warn" -Message "Helper/Baseline delta pattern ratio is $($mirroredDeltaMetrics.uniqueDeltaRatio), below the 0.35 warning threshold."
}

$failCount = @($findings | Where-Object { $_.severity -eq "fail" }).Count
$warnCount = @($findings | Where-Object { $_.severity -eq "warn" }).Count
$status = if ($failCount -gt 0) { "FAIL" } elseif ($warnCount -gt 0) { "WARN" } else { "PASS" }
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"

$payload = [ordered]@{
    generated = $timestamp
    input = $InputCsv
    totalRows = $normalizedRows.Count
    dialogCount = @($normalizedRows | Select-Object -ExpandProperty conversation_id | Sort-Object -Unique).Count
    reviewerCount = $reviewerRows.Count
    status = $status
    reviewerConcentration = [ordered]@{
        maxShare = $maxReviewerShare
        reviewers = $reviewerRows
    }
    taskFamilyDistribution = $taskFamilyRows
    patternMetrics = $patternMetrics
    mirroredDeltaMetrics = $mirroredDeltaMetrics
    findings = @($findings)
}

$jsonDir = [System.IO.Path]::GetDirectoryName($OutputJsonPath)
if (-not [string]::IsNullOrWhiteSpace($jsonDir)) {
    New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null
}

$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputJsonPath -Encoding UTF8

if ([string]::IsNullOrWhiteSpace($OutputMarkdownPath)) {
    $OutputMarkdownPath = [System.IO.Path]::ChangeExtension($OutputJsonPath, ".md")
}

$markdownLines = @()
$markdownLines += "# Human-Eval Integrity Report"
$markdownLines += "Generated: $timestamp"
$markdownLines += "Input: $InputCsv"
$markdownLines += "Status: $status"
$markdownLines += ""
$markdownLines += "## Topline"
$markdownLines += "- Rows: $($normalizedRows.Count)"
$markdownLines += "- Dialogs: $($payload.dialogCount)"
$markdownLines += "- Reviewers: $($payload.reviewerCount)"
$markdownLines += "- Max reviewer share: $([math]::Round($maxReviewerShare * 100, 2))%"
$markdownLines += "- Largest task-family share: $([math]::Round($largestTaskFamilyShare * 100, 2))%"
$markdownLines += "- Unique Helper/Baseline delta ratio: $($mirroredDeltaMetrics.uniqueDeltaRatio)"
$markdownLines += ""
$markdownLines += "## Variant pattern metrics"
$markdownLines += "| Variant | Rows | Unique patterns | Unique ratio | Top pattern share | Avg stddev |"
$markdownLines += "|---|---:|---:|---:|---:|---:|"
foreach ($metric in $patternMetrics) {
    $markdownLines += "| $($metric.variant) | $($metric.rows) | $($metric.uniquePatterns) | $($metric.uniquePatternRatio) | $($metric.topPatternShare) | $($metric.avgStdDev) |"
}
$markdownLines += ""
$markdownLines += "## Findings"
if ($findings.Count -eq 0) {
    $markdownLines += "- none"
}
else {
    foreach ($finding in $findings) {
        $markdownLines += "- [$($finding.severity.ToUpperInvariant())] $($finding.code): $($finding.message)"
    }
}

$markdownDir = [System.IO.Path]::GetDirectoryName($OutputMarkdownPath)
if (-not [string]::IsNullOrWhiteSpace($markdownDir)) {
    New-Item -ItemType Directory -Force -Path $markdownDir | Out-Null
}

Set-Content -Path $OutputMarkdownPath -Value ($markdownLines -join "`r`n") -Encoding UTF8
Write-Host "[HumanEvalIntegrity] Reports saved to $OutputJsonPath and $OutputMarkdownPath"

if ($FailOnViolation.IsPresent -and $status -eq "FAIL") {
    throw "[HumanEvalIntegrity] Integrity validation failed."
}
