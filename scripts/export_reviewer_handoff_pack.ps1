param(
    [string]$PacketCsvPath = "eval/live_blind_eval/packets/live_blind_eval_packet.csv",
    [string]$PacketManifestPath = "eval/live_blind_eval/manifests/live_blind_eval_packet_manifest.json",
    [string]$AssignmentManifestPath = "eval/live_blind_eval/manifests/reviewer_assignment.json",
    [string]$OutputRoot = "eval/live_blind_eval/handoff/active",
    [string]$CoordinatorTitle = "Blind Eval Reviewer Handoff Pack",
    [string]$ReviewerInstructionsTemplatePath = "",
    [string]$ReviewerInstructionsHeaderNote = "",
    [string]$ReviewInboxDir = "eval/live_blind_eval/inbox",
    [string[]]$PostCollectionScripts = @(
        "scripts/import_live_blind_eval_reviews.ps1",
        "scripts/reveal_live_blind_eval_scores.ps1",
        "scripts/validate_human_eval_integrity_v2.ps1",
        "scripts/generate_human_parity_report_v2.ps1"
    )
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common\BlindEvalPacketCommon.ps1")
. (Join-Path $PSScriptRoot "common\BlindEvalImportCommon.ps1")
. (Join-Path $PSScriptRoot "common\HumanEvalCommon.ps1")

if (-not (Test-Path $PacketCsvPath)) {
    throw "[BlindEval] Packet CSV not found: $PacketCsvPath"
}

$packetManifest = Import-BlindEvalJsonManifest -Path $PacketManifestPath -Label "Packet manifest"
$assignmentManifest = Import-BlindEvalJsonManifest -Path $AssignmentManifestPath -Label "Assignment manifest"
$packetRows = Import-Csv -Path $PacketCsvPath
if (-not $packetRows -or $packetRows.Count -eq 0) {
    throw "[BlindEval] Packet CSV is empty."
}

$requiredPacketColumns = @(
    "packet_id",
    "conversation_id",
    "blind_label",
    "language",
    "task_family",
    "source_scenario_id",
    "collection_date",
    "collection_mode",
    "prompt",
    "candidate_response"
)
Assert-BlindEvalColumns -SampleRow $packetRows[0] -RequiredColumns $requiredPacketColumns -ContextLabel "Packet CSV"

$criteria = Get-HumanEvalCriteria
$structuredNoteColumns = Get-HumanEvalStructuredNoteColumns
$packetRowsByPacket = @{}
foreach ($row in $packetRows) {
    $packetId = [string]$row.packet_id
    if (-not $packetRowsByPacket.ContainsKey($packetId)) {
        $packetRowsByPacket[$packetId] = New-Object System.Collections.Generic.List[object]
    }

    $packetRowsByPacket[$packetId].Add($row)
}

$outputRootFull = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    [System.IO.Path]::GetFullPath($OutputRoot)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputRoot))
}
New-Item -ItemType Directory -Force -Path $outputRootFull | Out-Null

function New-CoordinatorReadmeText {
    param(
        [Parameter(Mandatory = $true)]$PacketManifest,
        [Parameter(Mandatory = $true)]$AssignmentManifest,
        [Parameter(Mandatory = $true)][string]$OutputRootPath,
        [Parameter(Mandatory = $true)][string[]]$Criteria,
        [Parameter(Mandatory = $true)][string[]]$StructuredNoteColumns,
        [Parameter(Mandatory = $true)][string]$CoordinatorTitleText,
        [Parameter(Mandatory = $true)][string]$ReviewInboxPathText,
        [Parameter(Mandatory = $true)][string[]]$PostCollectionScriptList
    )

    $conversationCount = @($PacketManifest.conversations).Count
    $reviewerCount = [int]$AssignmentManifest.reviewerCount
    $collectionMode = [string]$PacketManifest.collectionMode
    $collectionDate = [string]$PacketManifest.collectionDate
    $reviewerIds = @($AssignmentManifest.reviewerSummary | Select-Object -ExpandProperty reviewerId | Sort-Object)

    $template = @'
# __COORDINATOR_TITLE__

Generated: __GENERATED__
Output root: __OUTPUT_ROOT__

## Corpus

- Conversations: __CONVERSATIONS__
- Reviewers: __REVIEWERS__
- Collection date: __COLLECTION_DATE__
- Collection mode: __COLLECTION_MODE__
- Reviewers in this pack: __REVIEWER_IDS__

## Contents

- `README_COORDINATOR.md`: this coordinator note
- `REVIEWER_INSTRUCTIONS_RU.md`: instructions to send to each reviewer
- `reviewer_response_template.csv`: blank response template with rubric + structured-note columns
- `reviewers\<reviewer_id>\packet.csv`: reviewer-facing blind packet
- `reviewers\<reviewer_id>\submission.csv`: prefilled file the reviewer should return

## Submission Schema

- Base rubric scores (`1.0 .. 5.0`): __CRITERIA__
- Structured reviewer notes: __STRUCTURED_NOTES__
- `clarification_helpfulness` uses `n_a` outside clarification turns.

## Coordinator Checklist

1. Send each reviewer only their own folder.
2. Do not send `reveal` artifacts or any `Helper/Baseline` mapping.
3. Do not let reviewers discuss scores while collection is active.
4. Ask reviewers to return only the filled `submission.csv`, including structured notes.
5. Place returned CSV files into `__REVIEW_INBOX__`.
6. Then run:
__POST_COLLECTION_SCRIPTS__

## Important

This handoff pack inherits `collectionMode = __COLLECTION_MODE__` from the current packet manifest.
If this pack was generated from a rehearsal or sample corpus, the resulting evidence will remain non-authoritative.
'@

    return $template.Replace("__GENERATED__", (Get-Date -Format "yyyy-MM-dd HH:mm:ss K")).
        Replace("__COORDINATOR_TITLE__", $CoordinatorTitleText).
        Replace("__OUTPUT_ROOT__", $OutputRootPath).
        Replace("__CONVERSATIONS__", [string]$conversationCount).
        Replace("__REVIEWERS__", [string]$reviewerCount).
        Replace("__COLLECTION_DATE__", $collectionDate).
        Replace("__COLLECTION_MODE__", $collectionMode).
        Replace("__REVIEWER_IDS__", ($reviewerIds -join ", ")).
        Replace("__CRITERIA__", ($Criteria -join ", ")).
        Replace("__STRUCTURED_NOTES__", ($StructuredNoteColumns -join ", ")).
        Replace("__REVIEW_INBOX__", $ReviewInboxPathText).
        Replace("__POST_COLLECTION_SCRIPTS__", (($PostCollectionScriptList | ForEach-Object { "   - ``{0}``" -f $_ }) -join "`r`n"))
}

function New-ReviewerInstructionsText {
    $templatePath = if ([string]::IsNullOrWhiteSpace($ReviewerInstructionsTemplatePath)) {
        Join-Path $PSScriptRoot "..\doc\parity_evidence\REVIEWER_INSTRUCTIONS_RU_TEMPLATE.md"
    }
    else {
        $ReviewerInstructionsTemplatePath
    }
    if (-not (Test-Path $templatePath)) {
        throw "[BlindEval] Reviewer instructions template not found: $templatePath"
    }

    $templateBody = Get-Content -Path $templatePath -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($ReviewerInstructionsHeaderNote)) {
        return $templateBody
    }

    return ("{0}`r`n`r`n{1}" -f $ReviewerInstructionsHeaderNote.Trim(), $templateBody)
}

function New-BlankTemplateRows {
    param(
        [string]$ReviewerId,
        [string]$CollectionModeValue
    )

    return New-HumanEvalReviewerSubmissionRow `
        -PacketId "pkt-example" `
        -ConversationId "example-001" `
        -BlindLabel "A" `
        -ReviewerId $ReviewerId `
        -Language "en" `
        -TaskFamily "research" `
        -SourceScenarioId "example-001" `
        -CollectionDate "yyyy-MM-dd" `
        -CollectionMode $CollectionModeValue `
        -Prompt "User prompt goes here." `
        -CandidateResponse "Blind candidate response goes here."
}

$coordinatorReadmePath = Join-Path $outputRootFull "README_COORDINATOR.md"
$reviewerInstructionsPath = Join-Path $outputRootFull "REVIEWER_INSTRUCTIONS_RU.md"
$blankTemplatePath = Join-Path $outputRootFull "reviewer_response_template.csv"

Set-Content -Path $coordinatorReadmePath -Value (New-CoordinatorReadmeText -PacketManifest $packetManifest -AssignmentManifest $assignmentManifest -OutputRootPath $outputRootFull -Criteria $criteria -StructuredNoteColumns $structuredNoteColumns -CoordinatorTitleText $CoordinatorTitle -ReviewInboxPathText $ReviewInboxDir -PostCollectionScriptList $PostCollectionScripts) -Encoding UTF8
Set-Content -Path $reviewerInstructionsPath -Value (New-ReviewerInstructionsText) -Encoding UTF8
@((New-BlankTemplateRows -ReviewerId "reviewer_id" -CollectionModeValue ([string]$packetManifest.collectionMode))) | Export-Csv -Path $blankTemplatePath -NoTypeInformation -Encoding UTF8

$reviewerSummaries = New-Object System.Collections.Generic.List[object]
$assignmentGroups = @($assignmentManifest.assignments | Group-Object reviewer_id | Sort-Object Name)
foreach ($reviewerGroup in $assignmentGroups) {
    $reviewerId = [string]$reviewerGroup.Name
    $reviewerDir = Join-Path $outputRootFull ("reviewers\{0}" -f $reviewerId)
    New-Item -ItemType Directory -Force -Path $reviewerDir | Out-Null

    $reviewerPacketRows = New-Object System.Collections.Generic.List[object]
    foreach ($assignment in $reviewerGroup.Group | Sort-Object packet_id, blind_label) {
        $packetId = [string]$assignment.packet_id
        if (-not $packetRowsByPacket.ContainsKey($packetId)) {
            throw "[BlindEval] Packet '$packetId' is referenced by assignment but missing in packet CSV."
        }

        foreach ($packetRow in @($packetRowsByPacket[$packetId].ToArray() | Sort-Object blind_label)) {
            $reviewerPacketRows.Add([PSCustomObject]@{
                packet_id = [string]$packetRow.packet_id
                conversation_id = [string]$packetRow.conversation_id
                blind_label = [string]$packetRow.blind_label
                reviewer_id = $reviewerId
                language = [string]$packetRow.language
                task_family = [string]$packetRow.task_family
                source_scenario_id = [string]$packetRow.source_scenario_id
                collection_date = [string]$packetRow.collection_date
                collection_mode = [string]$packetRow.collection_mode
                prompt = [string]$packetRow.prompt
                candidate_response = [string]$packetRow.candidate_response
            })
        }
    }

    $packetPath = Join-Path $reviewerDir "packet.csv"
    $submissionPath = Join-Path $reviewerDir "submission.csv"
    $readmePath = Join-Path $reviewerDir "README.md"

    $reviewerPacketRows.ToArray() | Export-Csv -Path $packetPath -NoTypeInformation -Encoding UTF8
    @(
        foreach ($row in $reviewerPacketRows.ToArray()) {
            New-HumanEvalReviewerSubmissionRow `
                -PacketId ([string]$row.packet_id) `
                -ConversationId ([string]$row.conversation_id) `
                -BlindLabel ([string]$row.blind_label) `
                -ReviewerId ([string]$row.reviewer_id) `
                -Language ([string]$row.language) `
                -TaskFamily ([string]$row.task_family) `
                -SourceScenarioId ([string]$row.source_scenario_id) `
                -CollectionDate ([string]$row.collection_date) `
                -CollectionMode ([string]$row.collection_mode) `
                -Prompt ([string]$row.prompt) `
                -CandidateResponse ([string]$row.candidate_response)
        }
    ) | Export-Csv -Path $submissionPath -NoTypeInformation -Encoding UTF8

    $reviewerReadme = @"
# Reviewer Packet: $reviewerId

Files in this folder:

- `packet.csv`: read-only blind packet
- `submission.csv`: file to fill and return

Please follow the global instructions in `..\..\REVIEWER_INSTRUCTIONS_RU.md`.
"@
    Set-Content -Path $readmePath -Value $reviewerReadme -Encoding UTF8

    $reviewerPacketRowArray = @($reviewerPacketRows.ToArray())
    $conversationIds = @($reviewerPacketRowArray | Select-Object -ExpandProperty conversation_id | Sort-Object -Unique)
    $taskFamilies = @($reviewerPacketRowArray | Select-Object -ExpandProperty task_family | Sort-Object -Unique)
    $languages = @($reviewerPacketRowArray | Select-Object -ExpandProperty language | Sort-Object -Unique)
    $reviewerSummaries.Add([PSCustomObject]@{
        reviewerId = $reviewerId
        reviewerDir = $reviewerDir
        packetPath = $packetPath
        submissionPath = $submissionPath
        rows = $reviewerPacketRowArray.Count
        conversations = $conversationIds.Count
        taskFamilies = $taskFamilies
        languages = $languages
    })
}

$handoffManifestPath = Join-Path $outputRootFull "handoff_manifest.json"
$handoffManifest = [ordered]@{
    schemaVersion = 2
    generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    packetCsvPath = $PacketCsvPath
    packetManifestPath = $PacketManifestPath
    assignmentManifestPath = $AssignmentManifestPath
    outputRoot = $outputRootFull
    criteria = $criteria
    structuredNoteColumns = $structuredNoteColumns
    reviewerCount = $reviewerSummaries.Count
    reviewers = @($reviewerSummaries.ToArray())
}
$handoffManifest | ConvertTo-Json -Depth 8 | Set-Content -Path $handoffManifestPath -Encoding UTF8

Write-Host "[BlindEval] Reviewer handoff pack exported to $outputRootFull"
Write-Host "[BlindEval] Handoff manifest saved to $handoffManifestPath"
