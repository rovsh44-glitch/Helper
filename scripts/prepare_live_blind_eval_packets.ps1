param(
    [string]$InputPairsPath = "eval/live_blind_eval/source/response_pairs.jsonl",
    [string]$OutputPackCsv = "eval/live_blind_eval/packets/live_blind_eval_packet.csv",
    [string]$OutputManifestPath = "eval/live_blind_eval/manifests/live_blind_eval_packet_manifest.json",
    [string]$OutputRevealMapPath = "eval/live_blind_eval/reveal/live_blind_eval_reveal_map.json",
    [string]$CollectionDate = "",
    [string]$CollectionMode = "live_non_authoritative"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\BlindEvalPacketCommon.ps1")

if ([string]::IsNullOrWhiteSpace($CollectionDate)) {
    $CollectionDate = Get-Date -Format "yyyy-MM-dd"
}

$pairs = Import-BlindEvalResponsePairs -InputPath $InputPairsPath
$packRows = New-Object System.Collections.Generic.List[object]
$manifestConversations = New-Object System.Collections.Generic.List[object]
$revealConversations = New-Object System.Collections.Generic.List[object]

foreach ($pair in $pairs | Sort-Object conversation_id) {
    $revealLabels = Get-BlindEvalRevealLabels -ConversationId $pair.conversation_id
    $packetId = ("pkt-{0}" -f $pair.conversation_id)

    $packRows.Add([PSCustomObject]@{
        packet_id = $packetId
        conversation_id = $pair.conversation_id
        blind_label = $revealLabels.helper
        language = $pair.language
        task_family = $pair.task_family
        source_scenario_id = $pair.source_scenario_id
        collection_date = $CollectionDate
        collection_mode = $CollectionMode
        prompt = $pair.prompt
        candidate_response = $pair.helper_response
    })
    $packRows.Add([PSCustomObject]@{
        packet_id = $packetId
        conversation_id = $pair.conversation_id
        blind_label = $revealLabels.baseline
        language = $pair.language
        task_family = $pair.task_family
        source_scenario_id = $pair.source_scenario_id
        collection_date = $CollectionDate
        collection_mode = $CollectionMode
        prompt = $pair.prompt
        candidate_response = $pair.baseline_response
    })

    $manifestConversations.Add([PSCustomObject]@{
        packetId = $packetId
        conversationId = $pair.conversation_id
        sourceScenarioId = $pair.source_scenario_id
        language = $pair.language
        taskFamily = $pair.task_family
        collectionDate = $CollectionDate
        collectionMode = $CollectionMode
        blindLabels = @($revealLabels.helper, $revealLabels.baseline)
    })
    $revealConversations.Add([PSCustomObject]@{
        packetId = $packetId
        conversationId = $pair.conversation_id
        helperLabel = $revealLabels.helper
        baselineLabel = $revealLabels.baseline
    })
}

foreach ($path in @($OutputPackCsv, $OutputManifestPath, $OutputRevealMapPath)) {
    $directory = [System.IO.Path]::GetDirectoryName($path)
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
}

$packRows | Export-Csv -Path $OutputPackCsv -NoTypeInformation -Encoding UTF8

$manifest = [ordered]@{
    schemaVersion = 1
    generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    packType = "live_review_packet"
    blindSemantics = "pre_score_blind"
    authoritativeBlindnessEligible = $true
    inputPairsPath = $InputPairsPath
    packCsv = $OutputPackCsv
    revealMapPath = $OutputRevealMapPath
    collectionDate = $CollectionDate
    collectionMode = $CollectionMode
    conversationCount = $manifestConversations.Count
    conversations = [object[]]$manifestConversations.ToArray()
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputManifestPath -Encoding UTF8

$revealMap = [ordered]@{
    schemaVersion = 1
    generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    revealType = "blind_label_map"
    conversations = [object[]]$revealConversations.ToArray()
}
$revealMap | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputRevealMapPath -Encoding UTF8

Write-Host "[BlindEval] Live blind packet saved to $OutputPackCsv"
Write-Host "[BlindEval] Manifest saved to $OutputManifestPath"
Write-Host "[BlindEval] Reveal map saved to $OutputRevealMapPath"
