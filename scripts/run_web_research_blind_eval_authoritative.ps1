param(
    [ValidateSet("Prepare", "Finalize", "Full")]
    [string]$Phase = "Prepare",
    [string]$RunRoot = "eval/web_research_blind_eval",
    [string]$InputPairsPath = "",
    [string]$ReviewerPoolCsv = "",
    [string[]]$ReviewerIds = @(),
    [string]$OutputReport = "doc/web_research_parity_report_latest.md",
    [string]$DatasetPath = "eval/web_research_parity/corpus.jsonl",
    [int]$MinDialogs = 200,
    [int]$MinUniqueReviewers = 4,
    [int]$MinReviewersPerDialog = 2,
    [string]$CollectionDate = "",
    [string]$SummaryJsonPath = "",
    [string]$SummaryMarkdownPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common\BlindEvalAuthoritativeWorkflow.ps1")

$webReviewerNote = @'
Этот blind-eval фокусируется на web-research сценариях.

При выставлении `usefulness` и `factuality` отдельно учитывайте:
- выглядит ли ответ как реально web-grounded, а не как summary по snippets;
- достаточно ли ответ честен по currentness, uncertainty и conflict handling;
- даёт ли ответ inspectable source-backed impression, а не generic prose.
'@

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
    -SummaryJsonPath $SummaryJsonPath `
    -SummaryMarkdownPath $SummaryMarkdownPath `
    -ReportGeneratorScript "generate_human_parity_report_v2.ps1" `
    -ReportTitle "Web-Research Blind-Eval Parity Report" `
    -SummaryTitle "Web-Research Blind Eval Authoritative Summary" `
    -BlindEvalLabel "Web-research blind eval" `
    -ReportMetadataFunction "Get-BlindEvalReportMetadata" `
    -CoordinatorTitle "Web-Research Blind Eval Reviewer Handoff Pack" `
    -ReviewerInstructionsHeaderNote $webReviewerNote `
    -ReviewInboxDisplayPath "eval/web_research_blind_eval/inbox" `
    -PostCollectionScripts @(
        "scripts/import_live_blind_eval_reviews.ps1",
        "scripts/reveal_live_blind_eval_scores.ps1",
        "scripts/validate_human_eval_integrity_v2.ps1",
        "scripts/generate_human_parity_report_v2.ps1 -ReportTitle 'Web-Research Blind-Eval Parity Report'"
    ) `
    -LogPrefix "WebResearchBlindEvalAuthoritative"
