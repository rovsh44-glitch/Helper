param(
    [string]$ReviewInboxDir = "eval/live_blind_eval/inbox",
    [string]$PacketManifestPath = "eval/live_blind_eval/manifests/live_blind_eval_packet_manifest.json",
    [string]$AssignmentManifestPath = "eval/live_blind_eval/manifests/reviewer_assignment.json",
    [string]$OutputBlindScoresCsv = "eval/live_blind_eval/merged/live_blind_eval_blind_scores.csv",
    [string]$OutputImportManifestPath = "eval/live_blind_eval/merged/live_blind_eval_import_manifest.json"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\BlindEvalImportCommon.ps1")

if (-not (Test-Path $ReviewInboxDir)) {
    throw "[BlindEval] Review inbox directory not found: $ReviewInboxDir"
}

$packetManifest = Import-BlindEvalJsonManifest -Path $PacketManifestPath -Label "Packet manifest"
$assignmentManifest = Import-BlindEvalJsonManifest -Path $AssignmentManifestPath -Label "Assignment manifest"
$packetMap = Get-BlindEvalPacketConversationMap -PacketManifest $packetManifest
$assignmentSet = Get-BlindEvalAssignmentMap -AssignmentManifest $assignmentManifest

$criteria = Get-HumanEvalCriteria
$structuredNoteColumns = Get-HumanEvalStructuredNoteColumns
$requiredColumns = @("packet_id", "conversation_id", "blind_label", "reviewer_id") + $criteria
$importedRows = New-Object System.Collections.Generic.List[object]
$filesWithStructuredNoteColumns = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
$reviewFiles = Get-ChildItem -Path $ReviewInboxDir -Filter "*.csv" -File | Sort-Object Name
if ($reviewFiles.Count -eq 0) {
    throw "[BlindEval] No review CSV files found in $ReviewInboxDir"
}

foreach ($file in $reviewFiles) {
    $rows = Import-Csv -Path $file.FullName
    if (-not $rows -or $rows.Count -eq 0) {
        continue
    }

    Assert-BlindEvalColumns -SampleRow $rows[0] -RequiredColumns $requiredColumns -ContextLabel "Review file '$($file.Name)'"
    $presentStructuredNoteColumns = @($structuredNoteColumns | Where-Object { $rows[0].PSObject.Properties.Name -contains $_ })
    foreach ($column in $presentStructuredNoteColumns) {
        $null = $filesWithStructuredNoteColumns.Add($column)
    }

    foreach ($row in $rows) {
        $packetId = [string]$row.packet_id
        $conversationId = [string]$row.conversation_id
        $blindLabel = [string]$row.blind_label
        $reviewerId = [string]$row.reviewer_id
        $assignmentKey = "{0}|{1}" -f $packetId, $reviewerId

        if (-not $packetMap.ContainsKey($packetId)) {
            throw "[BlindEval] Unknown packet_id '$packetId' in '$($file.Name)'."
        }
        if (-not $assignmentSet.ContainsKey($assignmentKey)) {
            throw "[BlindEval] Reviewer '$reviewerId' is not assigned to packet '$packetId'."
        }

        $conversation = $packetMap[$packetId]
        if ([string]$conversation.conversationId -ne $conversationId) {
            throw "[BlindEval] Conversation mismatch for packet '$packetId' in '$($file.Name)'."
        }

        foreach ($criterion in $criteria) {
            $null = Convert-ToHumanEvalScore -RawValue $row.$criterion -Criterion $criterion -ConversationId $conversationId -Variant $blindLabel
        }

        $importedRow = [ordered]@{
            packet_id = $packetId
            assignment_id = [string]$assignmentSet[$assignmentKey]
            conversation_id = $conversationId
            blind_label = $blindLabel
            language = [string]$conversation.language
            reviewer_id = $reviewerId
            task_family = [string]$conversation.taskFamily
            source_scenario_id = [string]$conversation.sourceScenarioId
            collection_date = [string]$conversation.collectionDate
            collection_mode = [string]$conversation.collectionMode
            clarity = [string]$row.clarity
            empathy_appropriateness = [string]$row.empathy_appropriateness
            usefulness = [string]$row.usefulness
            factuality = [string]$row.factuality
        }

        foreach ($column in $structuredNoteColumns) {
            $importedRow[$column] = if ($presentStructuredNoteColumns -contains $column) {
                Convert-ToHumanEvalStructuredNote -RawValue $row.$column -Column $column -AllowBlank
            }
            else {
                ""
            }
        }

        $importedRows.Add([PSCustomObject]$importedRow)
    }
}

foreach ($path in @($OutputBlindScoresCsv, $OutputImportManifestPath)) {
    $directory = [System.IO.Path]::GetDirectoryName($path)
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
}

$importedRows | Export-Csv -Path $OutputBlindScoresCsv -NoTypeInformation -Encoding UTF8
$importedRowArray = @($importedRows.ToArray())
$coverageConversations = @($importedRowArray | Select-Object -ExpandProperty conversation_id | Sort-Object -Unique)
$coverageReviewers = @($importedRowArray | Select-Object -ExpandProperty reviewer_id | Sort-Object -Unique)
$coverageTaskFamilies = @($importedRowArray | Select-Object -ExpandProperty task_family | Sort-Object -Unique)
$coverageLanguages = @($importedRowArray | Select-Object -ExpandProperty language | Sort-Object -Unique)
$payload = [ordered]@{
    schemaVersion = 2
    generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    reviewInboxDir = $ReviewInboxDir
    packetManifestPath = $PacketManifestPath
    assignmentManifestPath = $AssignmentManifestPath
    importedRows = $importedRows.Count
    conversations = $coverageConversations.Count
    reviewers = $coverageReviewers.Count
    taskFamilies = $coverageTaskFamilies
    languages = $coverageLanguages
    criteria = $criteria
    structuredNoteColumns = $structuredNoteColumns
    structuredNotesPresentInInput = @($filesWithStructuredNoteColumns | Sort-Object)
    outputBlindScoresCsv = $OutputBlindScoresCsv
    reviewFiles = @($reviewFiles | Select-Object -ExpandProperty FullName)
}
$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputImportManifestPath -Encoding UTF8

Write-Host "[BlindEval] Imported blind reviews to $OutputBlindScoresCsv"
Write-Host "[BlindEval] Import manifest saved to $OutputImportManifestPath"
