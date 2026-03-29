param(
    [string]$PacketManifestPath = "eval/live_blind_eval/manifests/live_blind_eval_packet_manifest.json",
    [string]$ReviewerPoolCsv = "",
    [string[]]$ReviewerIds = @(),
    [string]$OutputManifestPath = "eval/live_blind_eval/manifests/reviewer_assignment.json",
    [int]$MinUniqueReviewers = 4,
    [int]$MinReviewersPerDialog = 2,
    [double]$MaxReviewerShare = 0.45
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\BlindEvalPacketCommon.ps1")

if (-not (Test-Path $PacketManifestPath)) {
    throw "[BlindEval] Packet manifest not found: $PacketManifestPath"
}

$packetManifest = Get-Content -Path $PacketManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$conversations = @($packetManifest.conversations)
if ($conversations.Count -eq 0) {
    throw "[BlindEval] Packet manifest does not contain conversations."
}

$reviewers = Import-BlindEvalReviewerPool -ReviewerPoolCsv $ReviewerPoolCsv -ReviewerIds $ReviewerIds
if ($reviewers.Count -lt $MinUniqueReviewers) {
    throw "[BlindEval] Reviewer pool has only $($reviewers.Count) unique reviewer(s); required at least $MinUniqueReviewers."
}

$assignments = New-Object System.Collections.Generic.List[object]
$reviewerLoad = @{}
foreach ($reviewer in $reviewers) {
    $reviewerLoad[[string]$reviewer.reviewer_id] = 0
}

foreach ($conversation in $conversations | Sort-Object conversationId) {
    $eligible = @(
        $reviewers | Where-Object {
            $languageOk = (@($_.languages).Count -eq 0) -or (@($_.languages) -contains ([string]$conversation.language).ToLowerInvariant())
            $taskOk = (@($_.task_families).Count -eq 0) -or (@($_.task_families) -contains ([string]$conversation.taskFamily).ToLowerInvariant())
            $languageOk -and $taskOk
        }
    )

    if ($eligible.Count -lt $MinReviewersPerDialog) {
        throw "[BlindEval] Conversation '$($conversation.conversationId)' has only $($eligible.Count) eligible reviewer(s); required $MinReviewersPerDialog."
    }

    $selected = @(
        $eligible |
            Sort-Object { $reviewerLoad[[string]$_.reviewer_id] }, reviewer_id |
            Select-Object -First $MinReviewersPerDialog
    )

    foreach ($reviewer in $selected) {
        $reviewerId = [string]$reviewer.reviewer_id
        $reviewerLoad[$reviewerId] = [int]$reviewerLoad[$reviewerId] + 1
        $assignments.Add([PSCustomObject]@{
            assignment_id = "asg-$($conversation.packetId)-$reviewerId"
            packet_id = [string]$conversation.packetId
            conversation_id = [string]$conversation.conversationId
            reviewer_id = $reviewerId
            language = [string]$conversation.language
            task_family = [string]$conversation.taskFamily
        })
    }
}

$totalAssignments = [math]::Max(1, $assignments.Count)
$reviewerSummary = @(
    $reviewers |
        ForEach-Object {
            $reviewerId = [string]$_.reviewer_id
            $count = [int]$reviewerLoad[$reviewerId]
            [PSCustomObject]@{
                reviewerId = $reviewerId
                assignments = $count
                share = [math]::Round($count / [double]$totalAssignments, 4)
            }
        } |
        Sort-Object -Property @(
            @{ Expression = "assignments"; Descending = $true },
            @{ Expression = "reviewerId"; Descending = $false }
        )
)
$maxReviewerActualShare = if ($reviewerSummary.Count -eq 0) { 0.0 } else { [double]$reviewerSummary[0].share }
if ($maxReviewerActualShare -gt $MaxReviewerShare) {
    throw "[BlindEval] Reviewer assignment share is $([math]::Round($maxReviewerActualShare * 100, 2))%, above the allowed $([math]::Round($MaxReviewerShare * 100, 2))%."
}

$directory = [System.IO.Path]::GetDirectoryName($OutputManifestPath)
if (-not [string]::IsNullOrWhiteSpace($directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$payload = [ordered]@{
    schemaVersion = 1
    generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    packetManifestPath = $PacketManifestPath
    minUniqueReviewers = $MinUniqueReviewers
    minReviewersPerDialog = $MinReviewersPerDialog
    maxReviewerShare = $MaxReviewerShare
    reviewerCount = $reviewers.Count
    conversationCount = $conversations.Count
    assignments = [object[]]$assignments.ToArray()
    reviewerSummary = $reviewerSummary
}
$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputManifestPath -Encoding UTF8

Write-Host "[BlindEval] Reviewer assignment manifest saved to $OutputManifestPath"
