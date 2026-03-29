param(
    [string]$InputBlindScoresCsv = "eval/live_blind_eval/merged/live_blind_eval_blind_scores.csv",
    [string]$RevealMapPath = "eval/live_blind_eval/reveal/live_blind_eval_reveal_map.json",
    [string]$OutputCsv = "eval/live_blind_eval/merged/live_blind_eval_scores.csv",
    [string]$OutputSummaryPath = "eval/live_blind_eval/merged/live_blind_eval_reveal_summary.json"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\BlindEvalImportCommon.ps1")

if (-not (Test-Path $InputBlindScoresCsv)) {
    throw "[BlindEval] Blind scores CSV not found: $InputBlindScoresCsv"
}
if (-not (Test-Path $RevealMapPath)) {
    throw "[BlindEval] Reveal map not found: $RevealMapPath"
}

$rows = Import-Csv -Path $InputBlindScoresCsv
if (-not $rows -or $rows.Count -eq 0) {
    throw "[BlindEval] Blind scores CSV is empty."
}

$structuredNoteColumns = Get-HumanEvalStructuredNoteColumns
$requiredColumns = @("packet_id", "assignment_id", "conversation_id", "blind_label", "language", "reviewer_id", "task_family", "source_scenario_id", "collection_date", "collection_mode") + (Get-HumanEvalCriteria)
Assert-BlindEvalColumns -SampleRow $rows[0] -RequiredColumns $requiredColumns -ContextLabel "Blind scores CSV"
$presentStructuredNoteColumns = @($structuredNoteColumns | Where-Object { $rows[0].PSObject.Properties.Name -contains $_ })
$revealMap = Import-BlindEvalJsonManifest -Path $RevealMapPath -Label "Reveal map"
$conversationMap = Get-BlindEvalRevealConversationMap -RevealMap $revealMap

$revealedRows = New-Object System.Collections.Generic.List[object]
foreach ($row in $rows) {
    $conversationId = [string]$row.conversation_id
    if (-not $conversationMap.ContainsKey($conversationId)) {
        throw "[BlindEval] Missing reveal entry for conversation '$conversationId'."
    }

    $entry = $conversationMap[$conversationId]
    $blindLabel = [string]$row.blind_label
    $variant = if ($blindLabel -eq [string]$entry.helperLabel) { "Helper" } elseif ($blindLabel -eq [string]$entry.baselineLabel) { "Baseline" } else { "" }
    if ([string]::IsNullOrWhiteSpace($variant)) {
        throw "[BlindEval] Blind label '$blindLabel' is not mapped for conversation '$conversationId'."
    }

    $revealedRow = [ordered]@{
        conversation_id = $conversationId
        variant = $variant
        language = [string]$row.language
        task_family = [string]$row.task_family
        source_scenario_id = [string]$row.source_scenario_id
        collection_date = [string]$row.collection_date
        collection_mode = [string]$row.collection_mode
        clarity = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", (Convert-ToHumanEvalScore -RawValue $row.clarity -Criterion "clarity" -ConversationId $conversationId -Variant $blindLabel))
        empathy_appropriateness = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", (Convert-ToHumanEvalScore -RawValue $row.empathy_appropriateness -Criterion "empathy_appropriateness" -ConversationId $conversationId -Variant $blindLabel))
        usefulness = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", (Convert-ToHumanEvalScore -RawValue $row.usefulness -Criterion "usefulness" -ConversationId $conversationId -Variant $blindLabel))
        factuality = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", (Convert-ToHumanEvalScore -RawValue $row.factuality -Criterion "factuality" -ConversationId $conversationId -Variant $blindLabel))
    }

    foreach ($column in $structuredNoteColumns) {
        $revealedRow[$column] = if ($presentStructuredNoteColumns -contains $column) {
            Convert-ToHumanEvalStructuredNote -RawValue $row.$column -Column $column -AllowBlank
        }
        else {
            ""
        }
    }

    $revealedRow["reviewer_id"] = [string]$row.reviewer_id

    $revealedRows.Add([PSCustomObject]$revealedRow)
}

foreach ($path in @($OutputCsv, $OutputSummaryPath)) {
    $directory = [System.IO.Path]::GetDirectoryName($path)
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
}

$revealedRows | Export-Csv -Path $OutputCsv -NoTypeInformation -Encoding UTF8
$revealedRowArray = @($revealedRows.ToArray())
$coverageConversations = @($revealedRowArray | Select-Object -ExpandProperty conversation_id | Sort-Object -Unique)
$coverageReviewers = @($revealedRowArray | Select-Object -ExpandProperty reviewer_id | Sort-Object -Unique)
$coverageTaskFamilies = @($revealedRowArray | Select-Object -ExpandProperty task_family | Sort-Object -Unique)
$coverageLanguages = @($revealedRowArray | Select-Object -ExpandProperty language | Sort-Object -Unique)
$summary = [ordered]@{
    schemaVersion = 2
    generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    inputBlindScoresCsv = $InputBlindScoresCsv
    revealMapPath = $RevealMapPath
    outputCsv = $OutputCsv
    rows = $revealedRows.Count
    conversations = $coverageConversations.Count
    reviewers = $coverageReviewers.Count
    taskFamilies = $coverageTaskFamilies
    languages = $coverageLanguages
    structuredNoteColumns = $structuredNoteColumns
    structuredNotesPresentInInput = $presentStructuredNoteColumns
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputSummaryPath -Encoding UTF8

Write-Host "[BlindEval] Revealed blind scores to $OutputCsv"
Write-Host "[BlindEval] Reveal summary saved to $OutputSummaryPath"
