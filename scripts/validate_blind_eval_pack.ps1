param(
    [string]$PackCsv = "eval/human_eval_blind_pack.csv",
    [string]$ManifestPath = "eval/human_eval_manifest.json",
    [string]$OutputJsonPath = "doc/human_eval_blind_pack_validation.json",
    [string]$OutputMarkdownPath = "doc/human_eval_blind_pack_validation.md",
    [switch]$FailOnViolation
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\HumanEvalCommon.ps1")

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

if (-not (Test-Path $PackCsv)) {
    throw "[BlindEvalPack] Pack CSV not found: $PackCsv"
}

if (-not (Test-Path $ManifestPath)) {
    throw "[BlindEvalPack] Manifest not found: $ManifestPath"
}

$rows = Import-Csv -Path $PackCsv
if (-not $rows -or $rows.Count -eq 0) {
    throw "[BlindEvalPack] Pack CSV is empty."
}

$manifest = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
$packType = [string]$(if ($null -ne $manifest.packType) { $manifest.packType } else { "post_hoc_score_pack" })
$blindSemantics = [string]$(if ($null -ne $manifest.blindSemantics) { $manifest.blindSemantics } else { "post_hoc_serialized" })
$manifestConversations = @($manifest.conversations)
$manifestMap = @{}
foreach ($conversation in $manifestConversations) {
    $manifestMap[[string]$conversation.conversationId] = $conversation
}

$findings = [System.Collections.Generic.List[object]]::new()
$forbiddenMarkers = @("helper", "baseline", "gpt", "claude", "openai", "anthropic", "moonshot", "qwen")

if ($blindSemantics -ne "pre_score_blind") {
    Add-Finding -Target $findings -Severity "fail" -Code "post_hoc_blindness_insufficient" -Message "Blind pack semantics are '$blindSemantics'; this is not sufficient proof of true blind collection."
}

$requiredColumns = if ($packType -eq "live_review_packet") {
    @("packet_id", "conversation_id", "blind_label", "language", "task_family", "source_scenario_id", "collection_date", "collection_mode", "prompt", "candidate_response")
}
else {
    @("conversation_id", "blind_label", "language", "reviewer_id", "task_family", "source_scenario_id", "collection_date", "collection_mode") + (Get-HumanEvalCriteria)
}

foreach ($column in $requiredColumns) {
    if (-not ($rows[0].PSObject.Properties.Name -contains $column)) {
        throw "[BlindEvalPack] Missing required column: $column"
    }
}

foreach ($row in $rows) {
    $blindLabel = [string]$row.blind_label
    if (($blindLabel -ne "A") -and ($blindLabel -ne "B")) {
        Add-Finding -Target $findings -Severity "fail" -Code "blind_label_invalid" -Message "Conversation '$($row.conversation_id)' has invalid blind label '$blindLabel'. Only A/B are allowed."
    }

    $serialized = ($row.PSObject.Properties | ForEach-Object { [string]$_.Value }) -join " "
    foreach ($marker in $forbiddenMarkers) {
        if ($serialized.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Add-Finding -Target $findings -Severity "fail" -Code "provider_leak_detected" -Message "Conversation '$($row.conversation_id)' contains forbidden marker '$marker' in blind pack."
            break
        }
    }

    if ($packType -eq "live_review_packet") {
        if ([string]::IsNullOrWhiteSpace([string]$row.prompt) -or [string]::IsNullOrWhiteSpace([string]$row.candidate_response)) {
            Add-Finding -Target $findings -Severity "fail" -Code "packet_content_missing" -Message "Conversation '$($row.conversation_id)' has missing prompt or candidate response."
        }
    }
    else {
        foreach ($criterion in (Get-HumanEvalCriteria)) {
            $null = Convert-ToHumanEvalScore -RawValue $row.$criterion -Criterion $criterion -ConversationId ([string]$row.conversation_id) -Variant $blindLabel
        }
    }
}

$packCoverageRows = @(
    $rows |
        Group-Object conversation_id |
        Sort-Object Name |
        ForEach-Object {
            $conversationId = [string]$_.Name
            $blindLabels = @($_.Group | Select-Object -ExpandProperty blind_label | Sort-Object -Unique)
            $manifestEntry = if ($manifestMap.ContainsKey($conversationId)) { $manifestMap[$conversationId] } else { $null }

            if ($blindLabels.Count -ne 2) {
                Add-Finding -Target $findings -Severity "fail" -Code "blind_pair_incomplete" -Message "Conversation '$conversationId' has blind labels '$($blindLabels -join ",")' instead of exactly A/B."
            }

            if ($null -eq $manifestEntry) {
                Add-Finding -Target $findings -Severity "fail" -Code "manifest_entry_missing" -Message "Conversation '$conversationId' is missing in manifest."
            }
            else {
                $firstRow = $_.Group | Select-Object -First 1
                if ([string]::IsNullOrWhiteSpace([string]$manifestEntry.sourceScenarioId) -or
                    [string]::IsNullOrWhiteSpace([string]$manifestEntry.collectionDate) -or
                    [string]::IsNullOrWhiteSpace([string]$manifestEntry.collectionMode) -or
                    [string]::IsNullOrWhiteSpace([string]$manifestEntry.language) -or
                    [string]::IsNullOrWhiteSpace([string]$manifestEntry.taskFamily)) {
                    Add-Finding -Target $findings -Severity "fail" -Code "manifest_provenance_incomplete" -Message "Conversation '$conversationId' has incomplete provenance fields in manifest."
                }

                if ([string]$manifestEntry.language -ne [string]$firstRow.language) {
                    Add-Finding -Target $findings -Severity "fail" -Code "manifest_language_mismatch" -Message "Conversation '$conversationId' language differs between pack and manifest."
                }

                if ([string]$manifestEntry.taskFamily -ne [string]$firstRow.task_family) {
                    Add-Finding -Target $findings -Severity "fail" -Code "manifest_task_family_mismatch" -Message "Conversation '$conversationId' task family differs between pack and manifest."
                }

                if ([string]$manifestEntry.sourceScenarioId -ne [string]$firstRow.source_scenario_id) {
                    Add-Finding -Target $findings -Severity "fail" -Code "manifest_source_id_mismatch" -Message "Conversation '$conversationId' source scenario id differs between pack and manifest."
                }
            }

            [PSCustomObject]@{
                conversationId = $conversationId
                rowCount = $_.Count
                blindLabels = $blindLabels
                provenancePresent = ($null -ne $manifestEntry)
            }
        }
)

foreach ($manifestConversation in $manifestConversations) {
    $conversationId = [string]$manifestConversation.conversationId
    if (-not ($rows | Where-Object { $_.conversation_id -eq $conversationId } | Select-Object -First 1)) {
        Add-Finding -Target $findings -Severity "fail" -Code "manifest_orphan_entry" -Message "Manifest conversation '$conversationId' is missing from blind pack."
    }
}

$failCount = @($findings | Where-Object { $_.severity -eq "fail" }).Count
$warnCount = @($findings | Where-Object { $_.severity -eq "warn" }).Count
$status = if ($failCount -gt 0) { "FAIL" } elseif ($warnCount -gt 0) { "WARN" } else { "PASS" }
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"

$payload = [ordered]@{
    generated = $timestamp
    packCsv = $PackCsv
    manifestPath = $ManifestPath
    packType = $packType
    blindSemantics = $blindSemantics
    collectionFlowStatus = if ($blindSemantics -eq "pre_score_blind") { "PRE_SCORE_BLIND" } else { "POST_HOC_SERIALIZED" }
    authoritativeBlindness = ($status -eq "PASS") -and ($blindSemantics -eq "pre_score_blind")
    status = $status
    rows = $rows.Count
    conversationCount = @($rows | Select-Object -ExpandProperty conversation_id | Sort-Object -Unique).Count
    coverage = $packCoverageRows
    findings = @($findings)
}

$jsonDir = [System.IO.Path]::GetDirectoryName($OutputJsonPath)
if (-not [string]::IsNullOrWhiteSpace($jsonDir)) {
    New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null
}

$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputJsonPath -Encoding UTF8

$markdownDir = [System.IO.Path]::GetDirectoryName($OutputMarkdownPath)
if (-not [string]::IsNullOrWhiteSpace($markdownDir)) {
    New-Item -ItemType Directory -Force -Path $markdownDir | Out-Null
}

$lines = @()
$lines += "# Blind Eval Pack Validation"
$lines += "Generated: $timestamp"
$lines += "Pack CSV: $PackCsv"
$lines += "Manifest: $ManifestPath"
$lines += "Pack type: $packType"
$lines += "Blind semantics: $blindSemantics"
$lines += "Collection flow status: $($payload.collectionFlowStatus)"
$lines += "Authoritative blindness: $(if ($payload.authoritativeBlindness) { "YES" } else { "NO" })"
$lines += "Status: $status"
$lines += ""
$lines += "## Topline"
$lines += "- Rows: $($rows.Count)"
$lines += "- Conversations: $($payload.conversationCount)"
$lines += ""
$lines += "## Coverage"
$lines += "| Conversation | Rows | Blind labels | Manifest |"
$lines += "|---|---:|---|---|"
foreach ($coverage in $packCoverageRows | Select-Object -First 20) {
    $lines += "| $($coverage.conversationId) | $($coverage.rowCount) | $($coverage.blindLabels -join ",") | $(if ($coverage.provenancePresent) { "YES" } else { "NO" }) |"
}
$lines += ""
$lines += "## Findings"
if ($findings.Count -eq 0) {
    $lines += "- none"
}
else {
    foreach ($finding in $findings) {
        $lines += "- [$($finding.severity.ToUpperInvariant())] $($finding.code): $($finding.message)"
    }
}

Set-Content -Path $OutputMarkdownPath -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[BlindEvalPack] Validation reports saved to $OutputJsonPath and $OutputMarkdownPath"

if ($FailOnViolation.IsPresent -and $status -eq "FAIL") {
    throw "[BlindEvalPack] Validation failed."
}

