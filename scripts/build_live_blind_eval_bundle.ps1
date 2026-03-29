param(
    [string]$PacketManifestPath = "eval/live_blind_eval/manifests/live_blind_eval_packet_manifest.json",
    [string]$AssignmentManifestPath = "eval/live_blind_eval/manifests/reviewer_assignment.json",
    [string]$ImportManifestPath = "eval/live_blind_eval/merged/live_blind_eval_import_manifest.json",
    [string]$RevealSummaryPath = "eval/live_blind_eval/merged/live_blind_eval_reveal_summary.json",
    [string]$ScoredCsvPath = "eval/live_blind_eval/merged/live_blind_eval_scores.csv",
    [string]$OutputJsonPath = "eval/live_blind_eval/merged/live_blind_eval_bundle.json",
    [string]$OutputMarkdownPath = "eval/live_blind_eval/merged/live_blind_eval_bundle.md"
)

$ErrorActionPreference = "Stop"

function Get-JsonOrNull {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        return $null
    }

    try {
        return Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

$packetManifest = Get-JsonOrNull -Path $PacketManifestPath
$assignmentManifest = Get-JsonOrNull -Path $AssignmentManifestPath
$importManifest = Get-JsonOrNull -Path $ImportManifestPath
$revealSummary = Get-JsonOrNull -Path $RevealSummaryPath
$status = if (($null -ne $packetManifest) -and ($null -ne $assignmentManifest) -and ($null -ne $importManifest) -and ($null -ne $revealSummary) -and (Test-Path $ScoredCsvPath)) { "PASS" } else { "INCOMPLETE" }
$structuredNoteColumns = @()
if (($null -ne $revealSummary) -and ($null -ne $revealSummary.structuredNoteColumns)) {
    $structuredNoteColumns = @($revealSummary.structuredNoteColumns)
}
elseif (($null -ne $importManifest) -and ($null -ne $importManifest.structuredNoteColumns)) {
    $structuredNoteColumns = @($importManifest.structuredNoteColumns)
}

$structuredNotesPresentInInput = @()
if (($null -ne $revealSummary) -and ($null -ne $revealSummary.structuredNotesPresentInInput)) {
    $structuredNotesPresentInInput = @($revealSummary.structuredNotesPresentInInput)
}
elseif (($null -ne $importManifest) -and ($null -ne $importManifest.structuredNotesPresentInInput)) {
    $structuredNotesPresentInInput = @($importManifest.structuredNotesPresentInInput)
}

foreach ($path in @($OutputJsonPath, $OutputMarkdownPath)) {
    $directory = [System.IO.Path]::GetDirectoryName($path)
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
}

$payload = [ordered]@{
    schemaVersion = 2
    generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    status = $status
    packetManifestPath = $PacketManifestPath
    assignmentManifestPath = $AssignmentManifestPath
    importManifestPath = $ImportManifestPath
    revealSummaryPath = $RevealSummaryPath
    scoredCsvPath = $ScoredCsvPath
    structuredNoteColumns = $structuredNoteColumns
    structuredNotesPresentInInput = $structuredNotesPresentInInput
    claimEligible = $false
}
$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputJsonPath -Encoding UTF8

$lines = @()
$lines += "# Live Blind Eval Bundle"
$lines += "Generated: $($payload.generated)"
$lines += "Status: $status"
$lines += "- Packet manifest: $(if ($null -ne $packetManifest) { $PacketManifestPath } else { "missing" })"
$lines += "- Reviewer assignment: $(if ($null -ne $assignmentManifest) { $AssignmentManifestPath } else { "missing" })"
$lines += "- Import manifest: $(if ($null -ne $importManifest) { $ImportManifestPath } else { "missing" })"
$lines += "- Reveal summary: $(if ($null -ne $revealSummary) { $RevealSummaryPath } else { "missing" })"
$lines += "- Scored CSV: $(if (Test-Path $ScoredCsvPath) { $ScoredCsvPath } else { "missing" })"
$lines += "- Structured note columns: $(if ($structuredNoteColumns.Count -gt 0) { $structuredNoteColumns -join ", " } else { "none" })"
$lines += "- Structured notes present in current input: $(if ($structuredNotesPresentInInput.Count -gt 0) { $structuredNotesPresentInInput -join ", " } else { "none" })"
Set-Content -Path $OutputMarkdownPath -Value ($lines -join "`r`n") -Encoding UTF8

Write-Host "[BlindEval] Bundle saved to $OutputJsonPath"
