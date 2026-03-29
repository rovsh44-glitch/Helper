param(
    [string]$InputCsv = "eval/human_eval_scores.csv",
    [string]$DatasetPath = "eval/human_level_parity_ru_en.jsonl",
    [string]$OutputPackCsv = "eval/human_eval_blind_pack.csv",
    [string]$OutputManifestPath = "eval/human_eval_manifest.json",
    [string]$CollectionDate = "",
    [string]$CollectionMode = "synthetic"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\HumanEvalCommon.ps1")

function Get-BlindLabelForVariant {
    param(
        [Parameter(Mandatory = $true)][string]$ConversationId,
        [Parameter(Mandatory = $true)][string]$Variant
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($ConversationId)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
    }
    finally {
        $sha.Dispose()
    }
    $helperFirst = ($hash[0] % 2) -eq 0

    if ($Variant -ieq "Helper") {
        return $(if ($helperFirst) { "A" } else { "B" })
    }

    return $(if ($helperFirst) { "B" } else { "A" })
}

if ([string]::IsNullOrWhiteSpace($CollectionDate)) {
    $CollectionDate = Get-Date -Format "yyyy-MM-dd"
}

$normalizedRows = Import-HumanEvalNormalizedRows -InputCsv $InputCsv
$datasetMap = @{}
if (Test-Path $DatasetPath) {
    $datasetLines = Get-Content -Path $DatasetPath -Encoding UTF8 | Where-Object { $_ -and -not $_.TrimStart().StartsWith("#") }
    foreach ($line in $datasetLines) {
        $entry = $line | ConvertFrom-Json
        if (-not [string]::IsNullOrWhiteSpace([string]$entry.id)) {
            $datasetMap[[string]$entry.id] = $entry
        }
    }
}

$packRows = @()
$manifestConversations = @()

foreach ($group in ($normalizedRows | Group-Object conversation_id | Sort-Object Name)) {
    $conversationId = [string]$group.Name
    $firstRow = $group.Group | Select-Object -First 1
    $sourceScenario = if ($datasetMap.ContainsKey($conversationId)) { $datasetMap[$conversationId] } else { $null }
    $sourceScenarioId = if ($null -ne $sourceScenario) { [string]$sourceScenario.id } else { $conversationId }
    $language = if ($null -ne $sourceScenario -and -not [string]::IsNullOrWhiteSpace([string]$sourceScenario.language)) { [string]$sourceScenario.language } else { [string]$firstRow.language }
    $taskFamily = if ($null -ne $sourceScenario -and -not [string]::IsNullOrWhiteSpace([string]$sourceScenario.kind)) { [string]$sourceScenario.kind } else { [string]$firstRow.task_family }
    $blindLabels = @($group.Group | ForEach-Object { Get-BlindLabelForVariant -ConversationId $conversationId -Variant ([string]$_.variant) } | Sort-Object -Unique)

    $manifestConversations += [PSCustomObject]@{
        conversationId = $conversationId
        sourceScenarioId = $sourceScenarioId
        collectionDate = $CollectionDate
        collectionMode = $CollectionMode
        language = $language
        taskFamily = $taskFamily
        blindLabels = $blindLabels
        reviewerCount = @($group.Group | Select-Object -ExpandProperty reviewer_id | Sort-Object -Unique).Count
        rowCount = $group.Count
    }

    foreach ($row in $group.Group) {
        $packRows += [PSCustomObject]@{
            conversation_id = $conversationId
            blind_label = Get-BlindLabelForVariant -ConversationId $conversationId -Variant ([string]$row.variant)
            language = $language
            reviewer_id = [string]$row.reviewer_id
            task_family = $taskFamily
            source_scenario_id = $sourceScenarioId
            collection_date = $CollectionDate
            collection_mode = $CollectionMode
            clarity = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", [double]$row.clarity)
            empathy_appropriateness = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", [double]$row.empathy_appropriateness)
            usefulness = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", [double]$row.usefulness)
            factuality = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", [double]$row.factuality)
        }
    }
}

$packDir = [System.IO.Path]::GetDirectoryName($OutputPackCsv)
if (-not [string]::IsNullOrWhiteSpace($packDir)) {
    New-Item -ItemType Directory -Force -Path $packDir | Out-Null
}

$manifestDir = [System.IO.Path]::GetDirectoryName($OutputManifestPath)
if (-not [string]::IsNullOrWhiteSpace($manifestDir)) {
    New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null
}

$packRows | Export-Csv -Path $OutputPackCsv -NoTypeInformation -Encoding UTF8

$manifestPayload = [ordered]@{
    schemaVersion = 1
    generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    packType = "post_hoc_score_pack"
    blindSemantics = "post_hoc_serialized"
    authoritativeBlindnessEligible = $false
    inputCsv = $InputCsv
    datasetPath = $DatasetPath
    packCsv = $OutputPackCsv
    collectionDate = $CollectionDate
    collectionMode = $CollectionMode
    conversationCount = $manifestConversations.Count
    conversations = $manifestConversations
}

$manifestPayload | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputManifestPath -Encoding UTF8
Write-Host "[BlindEvalPack] Blind pack saved to $OutputPackCsv"
Write-Host "[BlindEvalPack] Manifest saved to $OutputManifestPath"
