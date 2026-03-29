. (Join-Path $PSScriptRoot "HumanEvalCommon.ps1")

function Import-BlindEvalJsonManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path $Path)) {
        throw "[BlindEval] $Label not found: $Path"
    }

    try {
        return Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "[BlindEval] Failed to parse $Label '$Path': $($_.Exception.Message)"
    }
}

function Assert-BlindEvalColumns {
    param(
        [Parameter(Mandatory = $true)]$SampleRow,
        [Parameter(Mandatory = $true)][string[]]$RequiredColumns,
        [Parameter(Mandatory = $true)][string]$ContextLabel
    )

    foreach ($column in $RequiredColumns) {
        if (-not ($SampleRow.PSObject.Properties.Name -contains $column)) {
            throw "[BlindEval] $ContextLabel is missing column '$column'."
        }
    }
}

function Get-BlindEvalPacketConversationMap {
    param([Parameter(Mandatory = $true)]$PacketManifest)

    $packetMap = @{}
    foreach ($conversation in @($PacketManifest.conversations)) {
        $packetMap[[string]$conversation.packetId] = $conversation
    }

    return $packetMap
}

function Get-BlindEvalAssignmentMap {
    param([Parameter(Mandatory = $true)]$AssignmentManifest)

    $assignmentMap = @{}
    foreach ($assignment in @($AssignmentManifest.assignments)) {
        $assignmentMap[("{0}|{1}" -f [string]$assignment.packet_id, [string]$assignment.reviewer_id)] = [string]$assignment.assignment_id
    }

    return $assignmentMap
}

function Get-BlindEvalRevealConversationMap {
    param([Parameter(Mandatory = $true)]$RevealMap)

    $conversationMap = @{}
    foreach ($entry in @($RevealMap.conversations)) {
        $conversationMap[[string]$entry.conversationId] = $entry
    }

    return $conversationMap
}

function Get-BlindEvalCoverageSummary {
    param(
        [Parameter(Mandatory = $true)]$Rows,
        [string]$ConversationIdProperty = "conversation_id",
        [string]$ReviewerIdProperty = "reviewer_id",
        [string]$TaskFamilyProperty = "task_family",
        [string]$LanguageProperty = "language"
    )

    $rowArray = @($Rows)
    $conversationValues = New-Object System.Collections.Generic.List[string]
    $reviewerValues = New-Object System.Collections.Generic.List[string]
    $taskFamilyValues = New-Object System.Collections.Generic.List[string]
    $languageValues = New-Object System.Collections.Generic.List[string]

    foreach ($row in $rowArray) {
        if ($null -eq $row) {
            continue
        }

        $conversationProperty = @($row.PSObject.Properties.Match($ConversationIdProperty) | Select-Object -First 1)[0]
        if (($null -ne $conversationProperty) -and (-not [string]::IsNullOrWhiteSpace([string]$conversationProperty.Value))) {
            $conversationValues.Add([string]$conversationProperty.Value)
        }

        $reviewerProperty = @($row.PSObject.Properties.Match($ReviewerIdProperty) | Select-Object -First 1)[0]
        if (($null -ne $reviewerProperty) -and (-not [string]::IsNullOrWhiteSpace([string]$reviewerProperty.Value))) {
            $reviewerValues.Add([string]$reviewerProperty.Value)
        }

        $taskFamilyProperty = @($row.PSObject.Properties.Match($TaskFamilyProperty) | Select-Object -First 1)[0]
        if (($null -ne $taskFamilyProperty) -and (-not [string]::IsNullOrWhiteSpace([string]$taskFamilyProperty.Value))) {
            $taskFamilyValues.Add([string]$taskFamilyProperty.Value)
        }

        $languageProperty = @($row.PSObject.Properties.Match($LanguageProperty) | Select-Object -First 1)[0]
        if (($null -ne $languageProperty) -and (-not [string]::IsNullOrWhiteSpace([string]$languageProperty.Value))) {
            $languageValues.Add([string]$languageProperty.Value)
        }
    }

    $conversationIds = @($conversationValues | Sort-Object -Unique)
    $reviewerIds = @($reviewerValues | Sort-Object -Unique)
    $taskFamilies = @($taskFamilyValues | Sort-Object -Unique)
    $languages = @($languageValues | Sort-Object -Unique)

    return [ordered]@{
        rows = $rowArray.Count
        conversations = $conversationIds.Count
        reviewers = $reviewerIds.Count
        taskFamilies = $taskFamilies
        languages = $languages
    }
}
