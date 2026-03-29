param(
    [string]$DatasetPath = "eval/reasoning_arc_lite.jsonl",
    [string]$OutputMarkdownPath = "doc/reasoning/active/CURRENT_REASONING_BASELINE.md",
    [string]$OutputJsonPath = "doc/reasoning/active/CURRENT_REASONING_BASELINE.json",
    [string]$ApiBase = "http://localhost:5000",
    [int]$RequestTimeoutSec = 45,
    [int]$MaxScenarios = 0,
    [switch]$DryRun,
    [switch]$NoFailOnThreshold
)

$ErrorActionPreference = "Stop"

function Test-ContainsIgnoreCase {
    param(
        [string]$InputText,
        [string]$Needle
    )

    if ([string]::IsNullOrWhiteSpace($InputText) -or [string]::IsNullOrWhiteSpace($Needle)) {
        return $false
    }

    return $InputText.IndexOf($Needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Get-OptionalPropertyValue {
    param(
        [object]$InputObject,
        [string]$PropertyName,
        $DefaultValue = $null
    )

    if ($null -eq $InputObject) {
        return $DefaultValue
    }

    $property = $InputObject.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return $DefaultValue
    }

    return $property.Value
}

if (-not (Test-Path $DatasetPath)) {
    throw "[ReasoningBenchmark] Dataset not found: $DatasetPath"
}

$scenarios = [System.Collections.Generic.List[object]]::new()
$lineNo = 0
foreach ($line in Get-Content $DatasetPath) {
    $lineNo++
    if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith("#", [System.StringComparison]::Ordinal)) {
        continue
    }

    $scenario = $line | ConvertFrom-Json
    foreach ($field in @("id", "language", "kind", "prompt", "expectedContains")) {
        if ($null -eq $scenario.$field -or [string]::IsNullOrWhiteSpace([string]$scenario.$field)) {
            throw "[ReasoningBenchmark] Invalid dataset line ${lineNo}: missing '$field'."
        }
    }

    $scenarios.Add($scenario)
}

if ($scenarios.Count -eq 0) {
    throw "[ReasoningBenchmark] Dataset is empty."
}

$selected = if ($MaxScenarios -gt 0) { @($scenarios | Select-Object -First $MaxScenarios) } else { @($scenarios) }
$results = [System.Collections.Generic.List[object]]::new()
$runtimeErrors = 0
$passes = 0
$candidatesGenerated = 0
$candidatesLocallyVerified = 0
$candidatesRejected = 0
$reasoningTurns = 0
$reasoningBranchingTurns = 0
$reasoningBranchesExplored = 0
$reasoningCandidateRejects = 0
$reasoningLocalVerificationPasses = 0
$reasoningLocalVerificationRejects = 0
$reasoningModelCallsUsed = 0
$reasoningRetrievalChunksUsed = 0
$reasoningProceduralLessonsUsed = 0
$reasoningApproximateTokenCost = 0

if (-not $DryRun.IsPresent) {
    $chatUrl = "$ApiBase/api/chat"
    foreach ($scenario in $selected) {
        try {
            $bodyJson = @{
                message = [string]$scenario.prompt
                conversationId = $null
                maxHistory = 8
                systemInstruction = "Return concise, structured reasoning."
            } | ConvertTo-Json -Depth 8 -Compress
            $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)
            $response = Invoke-RestMethod -Uri $chatUrl -Method Post -ContentType "application/json; charset=utf-8" -Body $bodyBytes -TimeoutSec $RequestTimeoutSec
            $responseText = [string]$response.response
            $passed = Test-ContainsIgnoreCase -InputText $responseText -Needle ([string]$scenario.expectedContains)
            $reasoningMetrics = Get-OptionalPropertyValue -InputObject $response -PropertyName "reasoningMetrics"
            $reasoningPathActive = [bool](Get-OptionalPropertyValue -InputObject $reasoningMetrics -PropertyName "pathActive" -DefaultValue $false)
            $reasoningBranches = [int](Get-OptionalPropertyValue -InputObject $reasoningMetrics -PropertyName "branchesExplored" -DefaultValue 0)
            $reasoningRejected = [int](Get-OptionalPropertyValue -InputObject $reasoningMetrics -PropertyName "candidatesRejected" -DefaultValue 0)
            $reasoningBranchingApplied = [bool](Get-OptionalPropertyValue -InputObject $reasoningMetrics -PropertyName "branchingApplied" -DefaultValue $false)
            $reasoningChecks = [int](Get-OptionalPropertyValue -InputObject $reasoningMetrics -PropertyName "localVerificationChecks" -DefaultValue 0)
            $reasoningPasses = [int](Get-OptionalPropertyValue -InputObject $reasoningMetrics -PropertyName "localVerificationPasses" -DefaultValue 0)
            $reasoningRejects = [int](Get-OptionalPropertyValue -InputObject $reasoningMetrics -PropertyName "localVerificationRejects" -DefaultValue 0)
            $modelCallsUsed = [int](Get-OptionalPropertyValue -InputObject $reasoningMetrics -PropertyName "modelCallsUsed" -DefaultValue 0)
            $retrievalChunksUsed = [int](Get-OptionalPropertyValue -InputObject $reasoningMetrics -PropertyName "retrievalChunksUsed" -DefaultValue 0)
            $proceduralLessonsUsed = [int](Get-OptionalPropertyValue -InputObject $reasoningMetrics -PropertyName "proceduralLessonsUsed" -DefaultValue 0)
            $approximateTokenCost = [int](Get-OptionalPropertyValue -InputObject $reasoningMetrics -PropertyName "approximateTokenCost" -DefaultValue ([int](Get-OptionalPropertyValue -InputObject $response -PropertyName "estimatedTokensGenerated" -DefaultValue 0)))
            $candidatesGenerated++
            if ($passed) {
                $passes++
                $candidatesLocallyVerified++
            }
            else {
                $candidatesRejected++
            }

            if ($reasoningPathActive) {
                $reasoningTurns++
                $reasoningBranchesExplored += $reasoningBranches
                $reasoningCandidateRejects += $reasoningRejected
                $reasoningLocalVerificationPasses += $reasoningPasses
                $reasoningLocalVerificationRejects += $reasoningRejects
                $reasoningModelCallsUsed += $modelCallsUsed
                $reasoningRetrievalChunksUsed += $retrievalChunksUsed
                $reasoningProceduralLessonsUsed += $proceduralLessonsUsed
                $reasoningApproximateTokenCost += $approximateTokenCost
            }

            if ($reasoningBranchingApplied) {
                $reasoningBranchingTurns++
            }

            $results.Add([PSCustomObject]@{
                id = [string]$scenario.id
                kind = [string]$scenario.kind
                language = [string]$scenario.language
                passed = $passed
                runtimeError = $false
                candidateGenerated = $true
                locallyVerified = $passed
                candidateRejected = (-not $passed)
                verificationMode = "expected_contains"
                expectedContains = [string]$scenario.expectedContains
                reasoningPathActive = $reasoningPathActive
                reasoningBranchingApplied = $reasoningBranchingApplied
                branchesExplored = $reasoningBranches
                candidatesRejected = $reasoningRejected
                localVerificationChecks = $reasoningChecks
                localVerificationPasses = $reasoningPasses
                localVerificationRejects = $reasoningRejects
                modelCallsUsed = $modelCallsUsed
                retrievalChunksUsed = $retrievalChunksUsed
                proceduralLessonsUsed = $proceduralLessonsUsed
                approximateTokenCost = $approximateTokenCost
            })
        }
        catch {
            $runtimeErrors++
            $results.Add([PSCustomObject]@{
                id = [string]$scenario.id
                kind = [string]$scenario.kind
                language = [string]$scenario.language
                passed = $false
                runtimeError = $true
                candidateGenerated = $false
                locallyVerified = $false
                candidateRejected = $false
                verificationMode = "expected_contains"
                expectedContains = [string]$scenario.expectedContains
                error = $_.Exception.Message
            })
        }
    }
}

$scenarioCount = $selected.Count
$passRate = if ($DryRun.IsPresent -or $scenarioCount -eq 0) { 0.0 } else { $passes / [double]$scenarioCount }
$status = if ($DryRun.IsPresent) { "NOT_EXECUTED" } elseif ($runtimeErrors -eq 0 -and $passRate -ge 0.70) { "PASS" } else { "FAIL" }
$kindSummary = @(
    $selected |
        Group-Object kind |
        Sort-Object Name |
        ForEach-Object {
            [PSCustomObject]@{
                kind = $_.Name
                total = $_.Count
            }
        }
)

$payload = [ordered]@{
    generated = (Get-Date -Format "yyyy-MM-dd HH:mm:ss K")
    benchmarkMode = if ($DryRun.IsPresent) { "dry-run" } else { "live" }
    datasetPath = $DatasetPath
    scenarioCount = $scenarioCount
    runtimeErrors = $runtimeErrors
    passCount = $passes
    candidatesGenerated = $candidatesGenerated
    candidatesLocallyVerified = $candidatesLocallyVerified
    candidatesRejected = $candidatesRejected
    reasoningTurns = $reasoningTurns
    reasoningBranchingTurns = $reasoningBranchingTurns
    reasoningCandidateRejects = $reasoningCandidateRejects
    reasoningLocalVerificationPasses = $reasoningLocalVerificationPasses
    reasoningLocalVerificationRejects = $reasoningLocalVerificationRejects
    avgReasoningBranchesExplored = if ($reasoningTurns -gt 0) { [math]::Round($reasoningBranchesExplored / [double]$reasoningTurns, 4) } else { 0.0 }
    avgReasoningModelCallsUsed = if ($reasoningTurns -gt 0) { [math]::Round($reasoningModelCallsUsed / [double]$reasoningTurns, 4) } else { 0.0 }
    avgReasoningRetrievalChunksUsed = if ($reasoningTurns -gt 0) { [math]::Round($reasoningRetrievalChunksUsed / [double]$reasoningTurns, 4) } else { 0.0 }
    avgReasoningProceduralLessonsUsed = if ($reasoningTurns -gt 0) { [math]::Round($reasoningProceduralLessonsUsed / [double]$reasoningTurns, 4) } else { 0.0 }
    avgReasoningApproximateTokenCost = if ($reasoningTurns -gt 0) { [math]::Round($reasoningApproximateTokenCost / [double]$reasoningTurns, 4) } else { 0.0 }
    passRate = [math]::Round($passRate, 4)
    status = $status
    kinds = $kindSummary
    results = @($results)
}

$jsonDir = [System.IO.Path]::GetDirectoryName($OutputJsonPath)
if (-not [string]::IsNullOrWhiteSpace($jsonDir)) {
    New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null
}
$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputJsonPath -Encoding UTF8

$lines = @()
$lines += "# Reasoning Benchmark Baseline"
$lines += "Generated: $($payload.generated)"
$lines += "Benchmark mode: $($payload.benchmarkMode)"
$lines += "Dataset: $DatasetPath"
$lines += "Scenarios: $scenarioCount"
$lines += "Runtime errors: $runtimeErrors"
$lines += "Candidates generated: $candidatesGenerated"
$lines += "Candidates locally verified: $candidatesLocallyVerified"
$lines += "Candidates rejected: $candidatesRejected"
$lines += "Reasoning turns: $reasoningTurns"
$lines += "Reasoning branching turns: $reasoningBranchingTurns"
$lines += "Reasoning candidate rejects: $reasoningCandidateRejects"
$lines += "Reasoning local verification passes: $reasoningLocalVerificationPasses"
$lines += "Reasoning local verification rejects: $reasoningLocalVerificationRejects"
$lines += "Avg reasoning branches explored: $(if ($reasoningTurns -gt 0) { [math]::Round($reasoningBranchesExplored / [double]$reasoningTurns, 2) } else { 0 })"
$lines += "Avg reasoning model calls used: $(if ($reasoningTurns -gt 0) { [math]::Round($reasoningModelCallsUsed / [double]$reasoningTurns, 2) } else { 0 })"
$lines += "Avg reasoning retrieval chunks used: $(if ($reasoningTurns -gt 0) { [math]::Round($reasoningRetrievalChunksUsed / [double]$reasoningTurns, 2) } else { 0 })"
$lines += "Avg reasoning procedural lessons used: $(if ($reasoningTurns -gt 0) { [math]::Round($reasoningProceduralLessonsUsed / [double]$reasoningTurns, 2) } else { 0 })"
$lines += "Avg reasoning approximate token cost: $(if ($reasoningTurns -gt 0) { [math]::Round($reasoningApproximateTokenCost / [double]$reasoningTurns, 2) } else { 0 })"
$lines += "Pass rate: $([math]::Round($passRate * 100, 2))%"
$lines += "Status: $status"
$lines += ""
$lines += "| Kind | Total |"
$lines += "|---|---:|"
foreach ($row in $kindSummary) {
    $lines += "| $($row.kind) | $($row.total) |"
}

Set-Content -Path $OutputMarkdownPath -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[ReasoningBenchmark] Results saved to $OutputJsonPath and $OutputMarkdownPath"

if ((-not $DryRun.IsPresent) -and (-not $NoFailOnThreshold.IsPresent) -and $status -eq "FAIL") {
    throw "[ReasoningBenchmark] Benchmark failed."
}
