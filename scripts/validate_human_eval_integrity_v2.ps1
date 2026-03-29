param(
    [string]$InputCsv = "eval/human_eval_scores.csv",
    [string]$OutputJsonPath = "doc/human_eval_parity_report_latest.integrity.json",
    [string]$OutputMarkdownPath = "",
    [int]$MinUniqueReviewers = 4,
    [int]$MinReviewersPerTaskFamily = 2,
    [int]$PreferredReviewersPerTaskFamily = 4,
    [double]$MaxReviewerShareWarn = 0.35,
    [double]$MaxReviewerShareFail = 0.45,
    [double]$PerFamilyMaxShareWarn = 0.60,
    [double]$PerFamilyMaxShareFail = 0.75,
    [switch]$FailOnViolation
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\HumanEvalCommon.ps1")

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

function Get-FindingStatus {
    param([object[]]$Findings)

    $findingsArray = @($Findings)
    if (@($findingsArray | Where-Object { $_.severity -eq "fail" }).Count -gt 0) {
        return "FAIL"
    }

    if (@($findingsArray | Where-Object { $_.severity -eq "warn" }).Count -gt 0) {
        return "WARN"
    }

    return "PASS"
}

$criteria = Get-HumanEvalCriteria
$normalizedRows = Import-HumanEvalNormalizedRows -InputCsv $InputCsv

$reviewerRows = @(
    $normalizedRows |
        Group-Object reviewer_id |
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

$taskFamilyReviewerCoverage = @(
    $normalizedRows |
        Group-Object task_family |
        Sort-Object Name |
        ForEach-Object {
            $familyTotal = $_.Count
            $familyReviewerRows = @(
                $_.Group |
                    Group-Object reviewer_id |
                    Sort-Object Count -Descending |
                    ForEach-Object {
                        [PSCustomObject]@{
                            reviewerId = $_.Name
                            rows = $_.Count
                            share = [math]::Round($_.Count / [double]$familyTotal, 4)
                        }
                    }
            )

            [PSCustomObject]@{
                taskFamily = $_.Name
                rows = $_.Count
                reviewerCount = $familyReviewerRows.Count
                maxReviewerShare = if ($familyReviewerRows.Count -eq 0) { 0.0 } else { [double]$familyReviewerRows[0].share }
                reviewers = $familyReviewerRows
            }
        }
)

$patternMetrics = @(
    $normalizedRows |
        Group-Object variant |
        Sort-Object Name |
        ForEach-Object {
            $variantRows = $_.Group | ForEach-Object {
                $patternKey = "{0:F2}|{1:F2}|{2:F2}|{3:F2}" -f $_.clarity, $_.empathy_appropriateness, $_.usefulness, $_.factuality
                [PSCustomObject]@{
                    clarity = [double]$_.clarity
                    empathy_appropriateness = [double]$_.empathy_appropriateness
                    usefulness = [double]$_.usefulness
                    factuality = [double]$_.factuality
                    pattern_key = $patternKey
                }
            }

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
$reviewerDiversityFindings = [System.Collections.Generic.List[object]]::new()

if ($reviewerRows.Count -lt $MinUniqueReviewers) {
    Add-Finding -Target $reviewerDiversityFindings -Severity "fail" -Code "reviewer_diversity_low" -Message "Only $($reviewerRows.Count) unique reviewer(s) present; authoritative blind-eval requires at least $MinUniqueReviewers."
}

if ($maxReviewerShare -gt $MaxReviewerShareFail) {
    Add-Finding -Target $reviewerDiversityFindings -Severity "fail" -Code "reviewer_concentration_high" -Message "Single reviewer share is $([math]::Round($maxReviewerShare * 100, 2))%, above the $([math]::Round($MaxReviewerShareFail * 100, 2))% fail threshold."
}
elseif ($maxReviewerShare -gt $MaxReviewerShareWarn) {
    Add-Finding -Target $reviewerDiversityFindings -Severity "warn" -Code "reviewer_concentration_warn" -Message "Single reviewer share is $([math]::Round($maxReviewerShare * 100, 2))%, above the $([math]::Round($MaxReviewerShareWarn * 100, 2))% warning threshold."
}

foreach ($coverage in $taskFamilyReviewerCoverage) {
    if ($coverage.reviewerCount -lt $MinReviewersPerTaskFamily) {
        Add-Finding -Target $reviewerDiversityFindings -Severity "fail" -Code "task_family_reviewer_count_low" -Message "Task family '$($coverage.taskFamily)' has only $($coverage.reviewerCount) reviewer(s); required at least $MinReviewersPerTaskFamily."
    }
    elseif ($coverage.reviewerCount -lt $PreferredReviewersPerTaskFamily) {
        Add-Finding -Target $reviewerDiversityFindings -Severity "warn" -Code "task_family_reviewer_spread_warn" -Message "Task family '$($coverage.taskFamily)' has only $($coverage.reviewerCount) reviewer(s); preferred target is $PreferredReviewersPerTaskFamily."
    }

    if ($coverage.maxReviewerShare -gt $PerFamilyMaxShareFail) {
        Add-Finding -Target $reviewerDiversityFindings -Severity "fail" -Code "task_family_reviewer_concentration_high" -Message "Task family '$($coverage.taskFamily)' has max reviewer share $([math]::Round($coverage.maxReviewerShare * 100, 2))%, above the $([math]::Round($PerFamilyMaxShareFail * 100, 2))% fail threshold."
    }
    elseif ($coverage.maxReviewerShare -gt $PerFamilyMaxShareWarn) {
        Add-Finding -Target $reviewerDiversityFindings -Severity "warn" -Code "task_family_reviewer_concentration_warn" -Message "Task family '$($coverage.taskFamily)' has max reviewer share $([math]::Round($coverage.maxReviewerShare * 100, 2))%, above the $([math]::Round($PerFamilyMaxShareWarn * 100, 2))% warning threshold."
    }
}

foreach ($finding in $reviewerDiversityFindings) {
    $findings.Add($finding)
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

$reviewerDiversityStatus = Get-FindingStatus -Findings $reviewerDiversityFindings
$status = Get-FindingStatus -Findings $findings
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"

$payload = [ordered]@{
    generated = $timestamp
    input = $InputCsv
    totalRows = $normalizedRows.Count
    dialogCount = @($normalizedRows | Select-Object -ExpandProperty conversation_id | Sort-Object -Unique).Count
    reviewerCount = $reviewerRows.Count
    status = $status
    reviewerDiversity = [ordered]@{
        status = $reviewerDiversityStatus
        minimumUniqueReviewers = $MinUniqueReviewers
        minimumReviewersPerTaskFamily = $MinReviewersPerTaskFamily
        preferredReviewersPerTaskFamily = $PreferredReviewersPerTaskFamily
        maxReviewerShare = $maxReviewerShare
        maxReviewerShareWarnThreshold = $MaxReviewerShareWarn
        maxReviewerShareFailThreshold = $MaxReviewerShareFail
        perFamilyMaxShareWarnThreshold = $PerFamilyMaxShareWarn
        perFamilyMaxShareFailThreshold = $PerFamilyMaxShareFail
        taskFamilies = $taskFamilyReviewerCoverage
        findings = @($reviewerDiversityFindings)
    }
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
$markdownLines += "Reviewer diversity status: $reviewerDiversityStatus"
$markdownLines += ""
$markdownLines += "## Topline"
$markdownLines += "- Rows: $($normalizedRows.Count)"
$markdownLines += "- Dialogs: $($payload.dialogCount)"
$markdownLines += "- Reviewers: $($payload.reviewerCount)"
$markdownLines += "- Max reviewer share: $([math]::Round($maxReviewerShare * 100, 2))%"
$markdownLines += "- Largest task-family share: $([math]::Round($largestTaskFamilyShare * 100, 2))%"
$markdownLines += "- Unique Helper/Baseline delta ratio: $($mirroredDeltaMetrics.uniqueDeltaRatio)"
$markdownLines += ""
$markdownLines += "## Reviewer diversity"
$markdownLines += "- Minimum unique reviewers target: $MinUniqueReviewers"
$markdownLines += "- Reviewer diversity status: $reviewerDiversityStatus"
$markdownLines += "| Task family | Rows | Reviewers | Max reviewer share |"
$markdownLines += "|---|---:|---:|---:|"
foreach ($coverage in $taskFamilyReviewerCoverage) {
    $markdownLines += "| $($coverage.taskFamily) | $($coverage.rows) | $($coverage.reviewerCount) | $([math]::Round($coverage.maxReviewerShare * 100, 2))% |"
}
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
