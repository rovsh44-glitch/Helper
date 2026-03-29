. (Join-Path $PSScriptRoot "BlindEvalPacketCommon.ps1")
. (Join-Path $PSScriptRoot "BlindEvalImportCommon.ps1")
. (Join-Path $PSScriptRoot "HumanEvalCommon.ps1")
. (Join-Path $PSScriptRoot "ParityEvidenceCommon.ps1")

function Resolve-BlindEvalWorkspacePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Convert-BlindEvalToRepoRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root)
    if (-not $resolvedRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar) -and -not $resolvedRoot.EndsWith([System.IO.Path]::AltDirectorySeparatorChar)) {
        $resolvedRoot += [System.IO.Path]::DirectorySeparatorChar
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if ($resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $resolvedPath.Substring($resolvedRoot.Length).Replace("\", "/")
    }

    return $resolvedPath
}

function New-BlindEvalDirectoryIfNeeded {
    param([Parameter(Mandatory = $true)][string]$Path)

    $directory = [System.IO.Path]::GetDirectoryName($Path)
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
}

function Invoke-CheckedBlindEvalScript {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptName,
        [Parameter(Mandatory = $true)][hashtable]$Arguments,
        [string]$LogPrefix = "BlindEvalAuthoritative"
    )

    $scriptPath = Join-Path (Split-Path $PSScriptRoot -Parent) $ScriptName
    Write-Host ("[{0}] {1}" -f $LogPrefix, $ScriptName)
    & $scriptPath @Arguments
}

function Assert-BlindEvalResponsePairPreconditions {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$MinimumDialogs,
        [string]$LogPrefix = "BlindEvalAuthoritative"
    )

    if (-not (Test-Path $Path)) {
        throw "[${LogPrefix}] Response-pairs file not found: $Path"
    }

    $pairs = @(Import-BlindEvalResponsePairs -InputPath $Path)
    $pairCount = $pairs.Count
    if ($pairCount -lt $MinimumDialogs) {
        throw "[${LogPrefix}] Response-pairs corpus has $pairCount dialog pairs; required at least $MinimumDialogs."
    }

    $duplicateIds = @(
        $pairs |
            Group-Object conversation_id |
            Where-Object { $_.Count -gt 1 } |
            Select-Object -ExpandProperty Name
    )
    if ($duplicateIds.Count -gt 0) {
        $preview = @($duplicateIds | Select-Object -First 5) -join ", "
        throw "[${LogPrefix}] Response-pairs corpus contains duplicate conversation_id values: $preview"
    }

    return [PSCustomObject]@{
        pairCount = $pairCount
        languages = @($pairs | Select-Object -ExpandProperty language | Sort-Object -Unique)
        taskFamilies = @($pairs | Select-Object -ExpandProperty task_family | Sort-Object -Unique)
    }
}

function Assert-BlindEvalStructuredNoteCompletion {
    param(
        [Parameter(Mandatory = $true)][string]$CsvPath,
        [string]$LogPrefix = "BlindEvalAuthoritative"
    )

    if (-not (Test-Path $CsvPath)) {
        throw "[${LogPrefix}] Structured-note validation input not found: $CsvPath"
    }

    $rows = Import-Csv -Path $CsvPath
    if (-not $rows -or $rows.Count -eq 0) {
        throw "[${LogPrefix}] Structured-note validation input is empty: $CsvPath"
    }

    $coverage = Get-HumanEvalStructuredNoteCoverageSummary -Rows $rows
    if (-not $coverage.allColumnsPresent) {
        $missingColumns = @($coverage.columns | Where-Object { -not $_.present } | ForEach-Object { $_.name })
        throw "[${LogPrefix}] Structured note columns missing in '$CsvPath': $($missingColumns -join ", ")"
    }

    $incompleteColumns = @($coverage.columns | Where-Object { $_.filledRows -lt $coverage.totalRows })
    if ($incompleteColumns.Count -gt 0) {
        $preview = @(
            $incompleteColumns |
                Select-Object -First 5 |
                ForEach-Object { "{0}:{1}/{2}" -f $_.name, $_.filledRows, $coverage.totalRows }
        ) -join ", "
        throw "[${LogPrefix}] Structured note completion is incomplete in '$CsvPath': $preview"
    }

    return $coverage
}

function Write-BlindEvalAuthoritativeSummary {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Payload,
        [Parameter(Mandatory = $true)][string]$JsonPath,
        [Parameter(Mandatory = $true)][string]$MarkdownPath,
        [string]$SummaryTitle = "Blind Eval Authoritative Summary",
        [string]$BlindEvalLabel = "Blind eval"
    )

    New-BlindEvalDirectoryIfNeeded -Path $JsonPath
    New-BlindEvalDirectoryIfNeeded -Path $MarkdownPath

    ($Payload | ConvertTo-Json -Depth 10) | Set-Content -Path $JsonPath -Encoding UTF8

    $lines = @()
    $lines += "# $SummaryTitle"
    $lines += "Generated: $($Payload.generated)"
    $lines += "Phase: $($Payload.phase)"
    $lines += "Status: $($Payload.status)"
    $lines += "Collection mode: $($Payload.collectionMode)"
    $lines += "Run root: $($Payload.runRoot)"
    $lines += "Output report: $($Payload.outputReport)"
    $lines += "Refresh parity evidence snapshot: $(if ($Payload.refreshParityEvidenceSnapshot) { "YES" } else { "NO" })"
    if (-not [string]::IsNullOrWhiteSpace([string]$Payload.errorMessage)) {
        $lines += "Error: $($Payload.errorMessage)"
    }

    if ($null -ne $Payload.preparation) {
        $lines += ""
        $lines += "## Preparation"
        $lines += "- Input pairs: $($Payload.preparation.inputPairsPath)"
        $lines += "- Dialog pairs: $($Payload.preparation.dialogPairs)"
        $lines += "- Languages: $($Payload.preparation.languages -join ", ")"
        $lines += "- Task families: $($Payload.preparation.taskFamilies -join ", ")"
        $lines += "- Reviewer pool size: $($Payload.preparation.reviewerPoolSize)"
        $lines += "- Packet manifest: $($Payload.preparation.packetManifestPath)"
        $lines += "- Assignment manifest: $($Payload.preparation.assignmentManifestPath)"
        $lines += "- Blind pack validation: $($Payload.preparation.blindPackValidationStatus)"
        $lines += "- Handoff root: $($Payload.preparation.handoffRoot)"
    }

    if ($null -ne $Payload.finalization) {
        $lines += ""
        $lines += "## Finalization"
        $lines += "- Review inbox: $($Payload.finalization.reviewInboxDir)"
        $lines += "- Review files: $($Payload.finalization.reviewFiles)"
        $lines += "- Structured note completion: $($Payload.finalization.structuredNoteCompletion)"
        $lines += "- Blind rows imported: $($Payload.finalization.importedRows)"
        $lines += "- Revealed rows: $($Payload.finalization.revealedRows)"
        $lines += "- $BlindEvalLabel evidence level: $($Payload.finalization.blindEval.evidenceLevel)"
        $lines += "- $BlindEvalLabel authoritative: $(if ($Payload.finalization.blindEval.authoritative) { "YES" } else { "NO" })"
        $lines += "- $BlindEvalLabel format status: $($Payload.finalization.blindEval.formatStatus)"
        $lines += "- $BlindEvalLabel provenance status: $($Payload.finalization.blindEval.provenanceStatus)"
        $lines += "- $BlindEvalLabel reviewer diversity status: $($Payload.finalization.blindEval.reviewerDiversityStatus)"
        $lines += "- $BlindEvalLabel integrity status: $($Payload.finalization.blindEval.integrityStatus)"
        $lines += "- Live blind-eval bundle status: $($Payload.finalization.liveBlindEvalBundleStatus)"
        if ($null -ne $Payload.finalization.parityBundle) {
            $lines += "- Parity evidence bundle status: $($Payload.finalization.parityBundle.status)"
            $lines += "- Parity evidence bundle claim eligible: $(if ($Payload.finalization.parityBundle.claimEligible) { "YES" } else { "NO" })"
        }
    }

    Set-Content -Path $MarkdownPath -Value ($lines -join "`r`n") -Encoding UTF8
}

function Invoke-BlindEvalAuthoritativeWorkflow {
    param(
        [ValidateSet("Prepare", "Finalize", "Full")]
        [string]$Phase = "Prepare",
        [Parameter(Mandatory = $true)][string]$RunRoot,
        [string]$InputPairsPath = "",
        [string]$ReviewerPoolCsv = "",
        [string[]]$ReviewerIds = @(),
        [Parameter(Mandatory = $true)][string]$OutputReport,
        [Parameter(Mandatory = $true)][string]$DatasetPath,
        [int]$MinDialogs = 200,
        [int]$MinUniqueReviewers = 4,
        [int]$MinReviewersPerDialog = 2,
        [string]$CollectionDate = "",
        [switch]$RefreshParityEvidenceSnapshot,
        [string]$SummaryJsonPath = "",
        [string]$SummaryMarkdownPath = "",
        [string]$ReportGeneratorScript = "generate_human_parity_report_v2.ps1",
        [string]$ReportTitle = "Human Blind-Eval Parity Report",
        [string]$SummaryTitle = "Blind Eval Authoritative Summary",
        [string]$BlindEvalLabel = "Blind eval",
        [string]$ReportMetadataFunction = "Get-BlindEvalReportMetadata",
        [string]$CoordinatorTitle = "Blind Eval Reviewer Handoff Pack",
        [string]$ReviewerInstructionsTemplatePath = "",
        [string]$ReviewerInstructionsHeaderNote = "",
        [string]$ReviewInboxDisplayPath = "",
        [string[]]$PostCollectionScripts = @(
            "scripts/import_live_blind_eval_reviews.ps1",
            "scripts/reveal_live_blind_eval_scores.ps1",
            "scripts/validate_human_eval_integrity_v2.ps1",
            "scripts/generate_human_parity_report_v2.ps1"
        ),
        [switch]$EnableParityEvidenceRefresh,
        [string]$ParityEvidenceBundlePath = "doc/parity_evidence/active/CURRENT_PARITY_EVIDENCE_BUNDLE.json",
        [string]$LogPrefix = "BlindEvalAuthoritative"
    )

    if ([string]::IsNullOrWhiteSpace($CollectionDate)) {
        $CollectionDate = Get-Date -Format "yyyy-MM-dd"
    }

    $workspaceRoot = (Get-Location).Path
    $resolvedRunRoot = Resolve-BlindEvalWorkspacePath -Path $RunRoot
    $resolvedInputPairsPath = if ([string]::IsNullOrWhiteSpace($InputPairsPath)) {
        Join-Path $resolvedRunRoot "source\response_pairs.jsonl"
    }
    else {
        Resolve-BlindEvalWorkspacePath -Path $InputPairsPath
    }
    $resolvedOutputReport = Resolve-BlindEvalWorkspacePath -Path $OutputReport
    $resolvedDatasetPath = Resolve-BlindEvalWorkspacePath -Path $DatasetPath
    $resolvedSummaryJsonPath = if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
        [System.IO.Path]::ChangeExtension($resolvedOutputReport, ".blind_eval_authoritative.json")
    }
    else {
        Resolve-BlindEvalWorkspacePath -Path $SummaryJsonPath
    }
    $resolvedSummaryMarkdownPath = if ([string]::IsNullOrWhiteSpace($SummaryMarkdownPath)) {
        [System.IO.Path]::ChangeExtension($resolvedOutputReport, ".blind_eval_authoritative.md")
    }
    else {
        Resolve-BlindEvalWorkspacePath -Path $SummaryMarkdownPath
    }

    $resolvedPacketCsvPath = Join-Path $resolvedRunRoot "packets\live_blind_eval_packet.csv"
    $resolvedPacketManifestPath = Join-Path $resolvedRunRoot "manifests\live_blind_eval_packet_manifest.json"
    $resolvedRevealMapPath = Join-Path $resolvedRunRoot "reveal\live_blind_eval_reveal_map.json"
    $resolvedAssignmentManifestPath = Join-Path $resolvedRunRoot "manifests\reviewer_assignment.json"
    $resolvedHandoffRoot = Join-Path $resolvedRunRoot "handoff\active"
    $resolvedReviewInboxDir = Join-Path $resolvedRunRoot "inbox"
    $resolvedBlindScoresCsv = Join-Path $resolvedRunRoot "merged\live_blind_eval_blind_scores.csv"
    $resolvedImportManifestPath = Join-Path $resolvedRunRoot "merged\live_blind_eval_import_manifest.json"
    $resolvedRevealedScoresCsv = Join-Path $resolvedRunRoot "merged\live_blind_eval_scores.csv"
    $resolvedRevealSummaryPath = Join-Path $resolvedRunRoot "merged\live_blind_eval_reveal_summary.json"
    $resolvedLiveBlindEvalBundleJsonPath = Join-Path $resolvedRunRoot "merged\live_blind_eval_bundle.json"
    $resolvedLiveBlindEvalBundleMarkdownPath = Join-Path $resolvedRunRoot "merged\live_blind_eval_bundle.md"

    $resolvedBlindPackValidationJsonPath = [System.IO.Path]::ChangeExtension($resolvedOutputReport, ".blind_pack_validation.json")
    $resolvedBlindPackValidationMarkdownPath = [System.IO.Path]::ChangeExtension($resolvedOutputReport, ".blind_pack_validation.md")
    $resolvedIntegrityJsonPath = [System.IO.Path]::ChangeExtension($resolvedOutputReport, ".integrity.json")

    $reviewInboxDisplay = if ([string]::IsNullOrWhiteSpace($ReviewInboxDisplayPath)) {
        Convert-BlindEvalToRepoRelativePath -Root $workspaceRoot -Path $resolvedReviewInboxDir
    }
    else {
        $ReviewInboxDisplayPath
    }

    $summaryPayload = [ordered]@{
        generated = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
        phase = $Phase
        status = "NOT_STARTED"
        collectionMode = "authoritative"
        runRoot = Convert-BlindEvalToRepoRelativePath -Root $workspaceRoot -Path $resolvedRunRoot
        outputReport = Convert-BlindEvalToRepoRelativePath -Root $workspaceRoot -Path $resolvedOutputReport
        refreshParityEvidenceSnapshot = $RefreshParityEvidenceSnapshot.IsPresent
        errorMessage = ""
        preparation = $null
        finalization = $null
    }

    try {
        if (($Phase -eq "Prepare") -or ($Phase -eq "Full")) {
            $pairSummary = Assert-BlindEvalResponsePairPreconditions -Path $resolvedInputPairsPath -MinimumDialogs $MinDialogs -LogPrefix $LogPrefix
            $reviewerPool = @(Import-BlindEvalReviewerPool -ReviewerPoolCsv $ReviewerPoolCsv -ReviewerIds $ReviewerIds)
            if ($reviewerPool.Count -lt $MinUniqueReviewers) {
                throw "[${LogPrefix}] Reviewer pool has only $($reviewerPool.Count) unique reviewer(s); required at least $MinUniqueReviewers."
            }

            Invoke-CheckedBlindEvalScript -ScriptName "prepare_live_blind_eval_packets.ps1" -Arguments @{
                InputPairsPath = $resolvedInputPairsPath
                OutputPackCsv = $resolvedPacketCsvPath
                OutputManifestPath = $resolvedPacketManifestPath
                OutputRevealMapPath = $resolvedRevealMapPath
                CollectionDate = $CollectionDate
                CollectionMode = "authoritative"
            } -LogPrefix $LogPrefix

            Invoke-CheckedBlindEvalScript -ScriptName "validate_blind_eval_pack.ps1" -Arguments @{
                PackCsv = $resolvedPacketCsvPath
                ManifestPath = $resolvedPacketManifestPath
                OutputJsonPath = $resolvedBlindPackValidationJsonPath
                OutputMarkdownPath = $resolvedBlindPackValidationMarkdownPath
                FailOnViolation = $true
            } -LogPrefix $LogPrefix

            $assignArguments = @{
                PacketManifestPath = $resolvedPacketManifestPath
                OutputManifestPath = $resolvedAssignmentManifestPath
                MinUniqueReviewers = $MinUniqueReviewers
                MinReviewersPerDialog = $MinReviewersPerDialog
            }
            if (-not [string]::IsNullOrWhiteSpace($ReviewerPoolCsv)) {
                $assignArguments["ReviewerPoolCsv"] = Resolve-BlindEvalWorkspacePath -Path $ReviewerPoolCsv
            }
            else {
                $assignArguments["ReviewerIds"] = $ReviewerIds
            }

            Invoke-CheckedBlindEvalScript -ScriptName "assign_blind_eval_reviewers.ps1" -Arguments $assignArguments -LogPrefix $LogPrefix

            $exportArguments = @{
                PacketCsvPath = $resolvedPacketCsvPath
                PacketManifestPath = $resolvedPacketManifestPath
                AssignmentManifestPath = $resolvedAssignmentManifestPath
                OutputRoot = $resolvedHandoffRoot
                CoordinatorTitle = $CoordinatorTitle
                ReviewerInstructionsHeaderNote = $ReviewerInstructionsHeaderNote
                ReviewInboxDir = $reviewInboxDisplay
                PostCollectionScripts = $PostCollectionScripts
            }
            if (-not [string]::IsNullOrWhiteSpace($ReviewerInstructionsTemplatePath)) {
                $exportArguments["ReviewerInstructionsTemplatePath"] = Resolve-BlindEvalWorkspacePath -Path $ReviewerInstructionsTemplatePath
            }

            Invoke-CheckedBlindEvalScript -ScriptName "export_reviewer_handoff_pack.ps1" -Arguments $exportArguments -LogPrefix $LogPrefix

            $packetManifest = Import-BlindEvalJsonManifest -Path $resolvedPacketManifestPath -Label "Packet manifest"
            $assignmentManifest = Import-BlindEvalJsonManifest -Path $resolvedAssignmentManifestPath -Label "Assignment manifest"
            $blindPackValidation = Import-BlindEvalJsonManifest -Path $resolvedBlindPackValidationJsonPath -Label "Blind pack validation"

            $summaryPayload.preparation = [ordered]@{
                inputPairsPath = Convert-BlindEvalToRepoRelativePath -Root $workspaceRoot -Path $resolvedInputPairsPath
                dialogPairs = [int]$pairSummary.pairCount
                languages = @($pairSummary.languages)
                taskFamilies = @($pairSummary.taskFamilies)
                reviewerPoolSize = $reviewerPool.Count
                reviewerIds = @($reviewerPool | ForEach-Object { [string]$_.reviewer_id } | Sort-Object)
                packetManifestPath = Convert-BlindEvalToRepoRelativePath -Root $workspaceRoot -Path $resolvedPacketManifestPath
                assignmentManifestPath = Convert-BlindEvalToRepoRelativePath -Root $workspaceRoot -Path $resolvedAssignmentManifestPath
                blindPackValidationStatus = [string]$blindPackValidation.status
                packetConversationCount = [int]$packetManifest.conversationCount
                assignedReviewerCount = [int]$assignmentManifest.reviewerCount
                handoffRoot = Convert-BlindEvalToRepoRelativePath -Root $workspaceRoot -Path $resolvedHandoffRoot
            }

            $summaryPayload.status = "PREPARED"
        }

        if (($Phase -eq "Finalize") -or ($Phase -eq "Full")) {
            $packetManifest = Import-BlindEvalJsonManifest -Path $resolvedPacketManifestPath -Label "Packet manifest"
            $assignmentManifest = Import-BlindEvalJsonManifest -Path $resolvedAssignmentManifestPath -Label "Assignment manifest"

            if ([int]$packetManifest.conversationCount -lt $MinDialogs) {
                throw "[${LogPrefix}] Packet manifest contains $($packetManifest.conversationCount) dialogs; required at least $MinDialogs."
            }
            if ([string]$packetManifest.collectionMode -ne "authoritative") {
                throw "[${LogPrefix}] Packet manifest collectionMode is '$($packetManifest.collectionMode)'; expected 'authoritative'."
            }
            if ([int]$assignmentManifest.reviewerCount -lt $MinUniqueReviewers) {
                throw "[${LogPrefix}] Assignment manifest contains only $($assignmentManifest.reviewerCount) reviewer(s); required at least $MinUniqueReviewers."
            }

            $reviewFiles = @(Get-ChildItem -Path $resolvedReviewInboxDir -Filter "*.csv" -File -ErrorAction SilentlyContinue | Sort-Object Name)
            if ($reviewFiles.Count -lt $MinUniqueReviewers) {
                throw "[${LogPrefix}] Review inbox has only $($reviewFiles.Count) CSV file(s); required at least $MinUniqueReviewers."
            }

            Invoke-CheckedBlindEvalScript -ScriptName "import_live_blind_eval_reviews.ps1" -Arguments @{
                ReviewInboxDir = $resolvedReviewInboxDir
                PacketManifestPath = $resolvedPacketManifestPath
                AssignmentManifestPath = $resolvedAssignmentManifestPath
                OutputBlindScoresCsv = $resolvedBlindScoresCsv
                OutputImportManifestPath = $resolvedImportManifestPath
            } -LogPrefix $LogPrefix

            $null = Assert-BlindEvalStructuredNoteCompletion -CsvPath $resolvedBlindScoresCsv -LogPrefix $LogPrefix

            Invoke-CheckedBlindEvalScript -ScriptName "reveal_live_blind_eval_scores.ps1" -Arguments @{
                InputBlindScoresCsv = $resolvedBlindScoresCsv
                RevealMapPath = $resolvedRevealMapPath
                OutputCsv = $resolvedRevealedScoresCsv
                OutputSummaryPath = $resolvedRevealSummaryPath
            } -LogPrefix $LogPrefix

            $revealedStructuredNoteCoverage = Assert-BlindEvalStructuredNoteCompletion -CsvPath $resolvedRevealedScoresCsv -LogPrefix $LogPrefix

            Invoke-CheckedBlindEvalScript -ScriptName "build_live_blind_eval_bundle.ps1" -Arguments @{
                PacketManifestPath = $resolvedPacketManifestPath
                AssignmentManifestPath = $resolvedAssignmentManifestPath
                ImportManifestPath = $resolvedImportManifestPath
                RevealSummaryPath = $resolvedRevealSummaryPath
                ScoredCsvPath = $resolvedRevealedScoresCsv
                OutputJsonPath = $resolvedLiveBlindEvalBundleJsonPath
                OutputMarkdownPath = $resolvedLiveBlindEvalBundleMarkdownPath
            } -LogPrefix $LogPrefix

            Invoke-CheckedBlindEvalScript -ScriptName $ReportGeneratorScript -Arguments @{
                InputCsv = $resolvedRevealedScoresCsv
                OutputReport = $resolvedOutputReport
                ReportTitle = $ReportTitle
                BlindEvalPackPath = $resolvedPacketCsvPath
                BlindEvalManifestPath = $resolvedPacketManifestPath
                BlindEvalValidationPath = $resolvedBlindPackValidationJsonPath
                IntegrityReportPath = $resolvedIntegrityJsonPath
                DatasetPath = $resolvedDatasetPath
                CollectionMode = "authoritative"
                EvidenceLevel = "authoritative"
                MinDialogs = $MinDialogs
                MinUniqueReviewers = $MinUniqueReviewers
                MinReviewersPerDialog = $MinReviewersPerDialog
            } -LogPrefix $LogPrefix

            $parityBundleMetadata = $null
            if ($EnableParityEvidenceRefresh.IsPresent -and $RefreshParityEvidenceSnapshot.IsPresent) {
                Invoke-CheckedBlindEvalScript -ScriptName "build_parity_evidence_bundle.ps1" -Arguments @{
                    BlindHumanEvalReportPath = $resolvedOutputReport
                } -LogPrefix $LogPrefix
                Invoke-CheckedBlindEvalScript -ScriptName "refresh_parity_evidence_snapshot.ps1" -Arguments @{
                    HumanEvalReportPath = $resolvedOutputReport
                } -LogPrefix $LogPrefix
                $parityBundleMetadata = Get-ParityEvidenceBundleMetadata -Path $ParityEvidenceBundlePath
            }

            $blindEvalMetadata = & $ReportMetadataFunction -Path $resolvedOutputReport
            $blindPackValidation = Import-BlindEvalJsonManifest -Path $resolvedBlindPackValidationJsonPath -Label "Blind pack validation"
            $liveBlindEvalBundle = Import-BlindEvalJsonManifest -Path $resolvedLiveBlindEvalBundleJsonPath -Label "Live blind-eval bundle"
            $revealedRows = Import-Csv -Path $resolvedRevealedScoresCsv

            $structuredNoteCompletion = if ($revealedStructuredNoteCoverage.allRowsComplete) { "COMPLETE" } else { "INCOMPLETE" }
            $summaryPayload.finalization = [ordered]@{
                reviewInboxDir = Convert-BlindEvalToRepoRelativePath -Root $workspaceRoot -Path $resolvedReviewInboxDir
                reviewFiles = $reviewFiles.Count
                importedRows = @((Import-Csv -Path $resolvedBlindScoresCsv)).Count
                revealedRows = $revealedRows.Count
                structuredNoteCompletion = $structuredNoteCompletion
                structuredNoteColumns = @($revealedStructuredNoteCoverage.columns | ForEach-Object { $_.name })
                blindEval = $blindEvalMetadata
                blindPackValidationStatus = [string]$blindPackValidation.status
                liveBlindEvalBundleStatus = [string]$liveBlindEvalBundle.status
                parityBundle = $parityBundleMetadata
            }

            $authoritativePass = $blindEvalMetadata.authoritative -and
                ([string]$blindEvalMetadata.integrityStatus -eq "PASS") -and
                ([string]$blindEvalMetadata.provenanceStatus -eq "PASS") -and
                ([string]$blindEvalMetadata.reviewerDiversityStatus -eq "PASS") -and
                ([string]$blindPackValidation.status -eq "PASS") -and
                ($structuredNoteCompletion -eq "COMPLETE")

            if (-not $authoritativePass) {
                throw "[${LogPrefix}] Final authoritative gate failed. authoritative=$($blindEvalMetadata.authoritative); integrity=$($blindEvalMetadata.integrityStatus); provenance=$($blindEvalMetadata.provenanceStatus); reviewer_diversity=$($blindEvalMetadata.reviewerDiversityStatus); structured_notes=$structuredNoteCompletion"
            }

            $summaryPayload.status = "PASS"
        }
    }
    catch {
        $summaryPayload.status = "FAIL"
        $summaryPayload.errorMessage = $_.Exception.Message
        Write-BlindEvalAuthoritativeSummary -Payload $summaryPayload -JsonPath $resolvedSummaryJsonPath -MarkdownPath $resolvedSummaryMarkdownPath -SummaryTitle $SummaryTitle -BlindEvalLabel $BlindEvalLabel
        throw
    }

    Write-BlindEvalAuthoritativeSummary -Payload $summaryPayload -JsonPath $resolvedSummaryJsonPath -MarkdownPath $resolvedSummaryMarkdownPath -SummaryTitle $SummaryTitle -BlindEvalLabel $BlindEvalLabel
    Write-Host ("[{0}] Summary saved to {1}" -f $LogPrefix, $resolvedSummaryMarkdownPath)
}
