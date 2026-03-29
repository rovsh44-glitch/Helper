param(
    [ValidateSet("Prepare", "Finalize", "Full")]
    [string]$Phase = "Prepare",
    [string]$RunRoot = "eval/live_blind_eval",
    [string]$InputPairsPath = "",
    [string]$ReviewerPoolCsv = "",
    [string[]]$ReviewerIds = @(),
    [string]$OutputReport = "doc/archive/top_level_history/human_eval_parity_report_latest.md",
    [string]$DatasetPath = "eval/human_level_parity_ru_en.jsonl",
    [int]$MinDialogs = 200,
    [int]$MinUniqueReviewers = 4,
    [int]$MinReviewersPerDialog = 2,
    [string]$CollectionDate = "",
    [switch]$RefreshParityEvidenceSnapshot,
    [string]$SummaryJsonPath = "",
    [string]$SummaryMarkdownPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common\BlindEvalAuthoritativeWorkflow.ps1")

Invoke-BlindEvalAuthoritativeWorkflow `
    -Phase $Phase `
    -RunRoot $RunRoot `
    -InputPairsPath $InputPairsPath `
    -ReviewerPoolCsv $ReviewerPoolCsv `
    -ReviewerIds $ReviewerIds `
    -OutputReport $OutputReport `
    -DatasetPath $DatasetPath `
    -MinDialogs $MinDialogs `
    -MinUniqueReviewers $MinUniqueReviewers `
    -MinReviewersPerDialog $MinReviewersPerDialog `
    -CollectionDate $CollectionDate `
    -RefreshParityEvidenceSnapshot:$RefreshParityEvidenceSnapshot `
    -SummaryJsonPath $SummaryJsonPath `
    -SummaryMarkdownPath $SummaryMarkdownPath `
    -ReportGeneratorScript "generate_human_parity_report_v2.ps1" `
    -ReportTitle "Human Blind-Eval Parity Report" `
    -SummaryTitle "Live Blind Eval Authoritative Summary" `
    -BlindEvalLabel "Blind human-eval" `
    -ReportMetadataFunction "Get-BlindHumanEvalReportMetadata" `
    -CoordinatorTitle "Blind Eval Reviewer Handoff Pack" `
    -EnableParityEvidenceRefresh `
    -LogPrefix "BlindEvalAuthoritative"
