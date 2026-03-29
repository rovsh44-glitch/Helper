param(
    [string]$ApiBase = "http://localhost:5000",
    [string]$DatasetPath = "eval/human_level_parity_ru_en.jsonl",
    [int]$MaxScenarios = 200,
    [int]$MinScenarioCount = 200,
    [string]$OutputReport = "doc/archive/top_level_history/eval_real_model_report.md",
    [string]$ErrorLogPath = "",
    [string]$QdrantBase = "http://localhost:6333",
    [string]$SessionToken = "",
    [int]$SessionTtlMinutes = 240,
    [string]$ApiKey = "",
    [string]$EvidenceLevel = "",
    [int]$RequestTimeoutSec = 90,
    [int]$RetryBackoffMs = 750,
    [int]$ProgressEvery = 10,
    [switch]$RequireApiReady,
    [switch]$LaunchLocalApiIfUnavailable,
    [string]$ApiRuntimeDir = "",
    [int]$ReadinessTimeoutSec = 600,
    [int]$ReadinessPollIntervalMs = 2000,
    [string]$ReadinessReportPath = "",
    [switch]$SkipDatasetValidation,
    [switch]$SkipKnowledgePreflight,
    [switch]$KnowledgePreflightWaived,
    [switch]$FailOnKnowledgePreflight,
    [switch]$SkipWarmup,
    [switch]$DryRun,
    [switch]$NoFailOnThreshold,
    [switch]$NoFailOnRuntimeErrors,
    [int]$AuditDrainTimeoutSec = 20,
    [int]$AuditDrainPollIntervalMs = 750,
    [string]$RuntimeMetadataPath = ""
)

$ErrorActionPreference = "Stop"
$allowedEvidenceLevels = @("sample", "synthetic", "dry_run", "live_non_authoritative", "authoritative")
$requestedEvidenceLevel = $EvidenceLevel

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")
. (Join-Path $PSScriptRoot "common\HelperRuntimeMetadata.ps1")
. (Join-Path $PSScriptRoot "common\Wait-PostTurnAuditDrain.ps1")
. (Join-Path $PSScriptRoot "common\AuditTraceJoin.ps1")

if ([string]::IsNullOrWhiteSpace($EvidenceLevel)) {
    $EvidenceLevel = if ($DryRun.IsPresent) { "dry_run" } else { "live_non_authoritative" }
}

if ($allowedEvidenceLevels -notcontains $EvidenceLevel) {
    throw "[EvalRealModel] Invalid EvidenceLevel '$EvidenceLevel'. Allowed values: $($allowedEvidenceLevels -join ", ")."
}

$dryRunDemotion = $false
if ($DryRun.IsPresent) {
    if ($EvidenceLevel -ne "dry_run") {
        $dryRunDemotion = $true
        Write-Warning "[EvalRealModel] Dry-run forces EvidenceLevel=dry_run. Requested '$EvidenceLevel' has been demoted."
    }

    $EvidenceLevel = "dry_run"
}

function Test-ContainsIgnoreCase {
    param(
        [string]$InputText,
        [string]$Needle
    )

    if ([string]::IsNullOrEmpty($InputText) -or [string]::IsNullOrEmpty($Needle)) {
        return $false
    }

    return $InputText.IndexOf($Needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Get-HttpStatusCode {
    param([Parameter(Mandatory = $true)]$Exception)

    if ($null -eq $Exception) {
        return $null
    }

    $response = Get-SafePropertyValue -InputObject $Exception -PropertyName "Response"
    if ($null -eq $response) {
        return $null
    }

    $statusCode = Get-SafePropertyValue -InputObject $response -PropertyName "StatusCode"
    if ($null -ne $statusCode) {
        try { return [int]$statusCode } catch { return $null }
    }

    return $null
}

function Get-CorrelationId {
    param([Parameter(Mandatory = $true)]$Exception)

    if ($null -eq $Exception) {
        return ""
    }

    $response = Get-SafePropertyValue -InputObject $Exception -PropertyName "Response"
    if ($null -eq $response) {
        return ""
    }

    $headers = Get-SafePropertyValue -InputObject $response -PropertyName "Headers"
    if ($null -eq $headers) {
        return ""
    }

    try {
        $id = $headers["x-correlation-id"]
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            return [string]$id
        }

        $id = $headers["X-Correlation-Id"]
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            return [string]$id
        }
    }
    catch {
        return ""
    }

    return ""
}

function Get-ResponseHeaderValue {
    param(
        [Parameter(Mandatory = $false)]$Headers,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Headers -or [string]::IsNullOrWhiteSpace($Name)) {
        return ""
    }

    try {
        $value = $Headers[$Name]
        if ($null -eq $value -and ($Headers.PSObject.Properties.Name -contains $Name)) {
            $value = $Headers.$Name
        }
        if ($null -eq $value) {
            return ""
        }

        if ($value -is [System.Array]) {
            return [string]($value | Select-Object -First 1)
        }

        return [string]$value
    }
    catch {
        return ""
    }
}

function Get-SafePropertyValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    if ($null -eq $InputObject) {
        return $null
    }

    try {
        $property = $InputObject.PSObject.Properties[$PropertyName]
        if ($null -ne $property) {
            return $property.Value
        }
    }
    catch {
        return $null
    }

    return $null
}

function Get-SafeBooleanPropertyValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName,
        [bool]$Default = $false
    )

    $value = Get-SafePropertyValue -InputObject $InputObject -PropertyName $PropertyName
    if ($null -eq $value) {
        return $Default
    }

    try {
        return [bool]$value
    }
    catch {
        return $Default
    }
}

function Get-SafeIntPropertyValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName,
        [int]$Default = 0
    )

    $value = Get-SafePropertyValue -InputObject $InputObject -PropertyName $PropertyName
    if ($null -eq $value) {
        return $Default
    }

    try {
        return [int]$value
    }
    catch {
        return $Default
    }
}

function Test-IsTimeoutException {
    param([Parameter(Mandatory = $true)]$Exception)

    if ($Exception -is [System.TimeoutException]) {
        return $true
    }

    if (($Exception -is [System.Threading.Tasks.TaskCanceledException]) -or ($Exception -is [System.OperationCanceledException])) {
        return $true
    }

    if ($Exception -is [System.Net.WebException]) {
        if ($Exception.Status -eq [System.Net.WebExceptionStatus]::Timeout) {
            return $true
        }
    }

    $message = [string]$Exception.Message
    return (Test-ContainsIgnoreCase -InputText $message -Needle "timed out") -or
        (Test-ContainsIgnoreCase -InputText $message -Needle "timeout") -or
        (Test-ContainsIgnoreCase -InputText $message -Needle "task was canceled") -or
        (Test-ContainsIgnoreCase -InputText $message -Needle "operation was canceled")
}

function Get-SessionAccessToken {
    param(
        [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
        [string]$ResolvedApiKey,
        [int]$RequestedTtlMinutes,
        [string]$RequestedSurface = "conversation",
        [string[]]$RequestedScopes = @("chat:read", "chat:write")
    )

    $sessionUrl = "$ApiBaseUrl/api/auth/session"
    $sessionHeaders = @{}
    if (-not [string]::IsNullOrWhiteSpace($ResolvedApiKey)) {
        $sessionHeaders["X-API-KEY"] = $ResolvedApiKey
    }

    $normalizedScopes = @(
        $RequestedScopes |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim() } |
            Select-Object -Unique
    )
    if ($normalizedScopes.Count -eq 0) {
        $normalizedScopes = @("chat:read", "chat:write")
    }

    $ttl = [math]::Min(240, [math]::Max(2, $RequestedTtlMinutes))
    $sessionPayload = @{
        surface = if ([string]::IsNullOrWhiteSpace($RequestedSurface)) { "conversation" } else { $RequestedSurface }
        requestedScopes = $normalizedScopes
        ttlMinutes = $ttl
    }

    if (-not [string]::IsNullOrWhiteSpace($ResolvedApiKey)) {
        $sessionPayload["apiKey"] = $ResolvedApiKey
    }

    $sessionBodyJson = $sessionPayload | ConvertTo-Json -Depth 8 -Compress
    $sessionBody = [System.Text.Encoding]::UTF8.GetBytes($sessionBodyJson)
    $session = Invoke-RestMethod -Uri $sessionUrl -Method Post -Headers $sessionHeaders -ContentType "application/json; charset=utf-8" -Body $sessionBody -TimeoutSec 30
    return [string]$session.accessToken
}

function Invoke-EvalWarmup {
    param(
        [Parameter(Mandatory = $true)][string]$ChatUrl,
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [int]$TimeoutSec = 90
    )

    $warmupPrompts = @(
        "[warmup] Ready-check: answer in one short sentence.",
        "[warmup] Собери источники по .NET observability",
        "[warmup] remember: отвечай кратко"
    )

    foreach ($warmupPrompt in $warmupPrompts) {
        try {
            $warmupBodyJson = @{
                message = $warmupPrompt
                conversationId = $null
                maxHistory = 8
                systemInstruction = $null
            } | ConvertTo-Json -Depth 8 -Compress
            $warmupBody = [System.Text.Encoding]::UTF8.GetBytes($warmupBodyJson)
            $null = Invoke-RestMethod -Uri $ChatUrl -Method Post -Headers $Headers -ContentType "application/json; charset=utf-8" -Body $warmupBody -TimeoutSec $TimeoutSec
        }
        catch {
            Write-Warning "[EvalRealModel] Warmup request failed but evaluation will continue: $($_.Exception.Message)"
        }
    }
}

function Test-ScenarioPass {
    param(
        [Parameter(Mandatory = $true)]$Scenario,
        [Parameter(Mandatory = $true)]$Response
    )

    $evaluation = Get-ScenarioEvaluation -Scenario $Scenario -Response $Response
    return [bool]$evaluation.passed
}

function Get-ScenarioEvaluation {
    param(
        [Parameter(Mandatory = $true)]$Scenario,
        [Parameter(Mandatory = $true)]$Response
    )

    $kind = [string]$Scenario.kind
    $responseText = [string]$Response.response
    $requiresConfirmation = ($Response.requiresConfirmation -eq $true)
    $sources = @($Response.sources)
    $citationCoverage = 0.0
    try {
        $citationCoverage = [double]$Response.citationCoverage
    }
    catch {
        $citationCoverage = 0.0
    }
    $groundingStatus = [string]$Response.groundingStatus

    switch ($kind.ToLowerInvariant()) {
        "clarification" {
            $hasClarificationGrounding = $groundingStatus.Equals("clarification_required", [System.StringComparison]::OrdinalIgnoreCase)
            $hasClarificationSignal = $requiresConfirmation -or
                $hasClarificationGrounding -or
                (Test-ContainsIgnoreCase -InputText $responseText -Needle "clarif") -or
                $responseText.TrimEnd().EndsWith("?")

            $signal = "none"
            if ($requiresConfirmation) {
                $signal = "requires_confirmation"
            }
            elseif ($hasClarificationGrounding) {
                $signal = "grounding_clarification_required"
            }
            elseif (Test-ContainsIgnoreCase -InputText $responseText -Needle "clarif") {
                $signal = "clarification_keyword"
            }
            elseif ($responseText.TrimEnd().EndsWith("?")) {
                $signal = "question_form"
            }

            return [PSCustomObject]@{
                passed = $hasClarificationSignal
                responseSignal = $signal
                failReason = if ($hasClarificationSignal) { "" } else { "missing_clarification_signal" }
                sourcesCount = $sources.Count
                citationCoverage = $citationCoverage
                groundingStatus = $groundingStatus
                requiresConfirmation = $requiresConfirmation
            }
        }
        "research" {
            $hasSources = $sources.Count -ge 1
            $hasCoverage = $citationCoverage -ge 0.70
            $passed = $hasSources -or $hasCoverage
            $looksLikeClarification = $groundingStatus.Equals("clarification_required", [System.StringComparison]::OrdinalIgnoreCase) -or
                (Test-ContainsIgnoreCase -InputText $responseText -Needle "clarify whether") -or
                (Test-ContainsIgnoreCase -InputText $responseText -Needle "multiple modes")

            $failReason = ""
            $signal = if ($hasSources) { "sources_present" } elseif ($hasCoverage) { "citation_coverage" } else { "none" }
            if (-not $passed) {
                if ($looksLikeClarification) {
                    $failReason = "clarification_instead_of_research"
                    $signal = "clarification_response"
                }
                elseif ($sources.Count -eq 0) {
                    $failReason = "no_sources"
                    $signal = "missing_sources"
                }
                elseif ($citationCoverage -lt 0.70) {
                    $failReason = "low_citation_coverage"
                    $signal = "low_coverage"
                }
                else {
                    $failReason = "research_quality_failed"
                }
            }

            return [PSCustomObject]@{
                passed = $passed
                responseSignal = $signal
                failReason = $failReason
                sourcesCount = $sources.Count
                citationCoverage = $citationCoverage
                groundingStatus = $groundingStatus
                requiresConfirmation = $requiresConfirmation
            }
        }
        default {
            $hasContent = -not [string]::IsNullOrWhiteSpace($responseText)
            return [PSCustomObject]@{
                passed = $hasContent
                responseSignal = if ($hasContent) { "response_present" } else { "empty_response" }
                failReason = if ($hasContent) { "" } else { "empty_response" }
                sourcesCount = $sources.Count
                citationCoverage = $citationCoverage
                groundingStatus = $groundingStatus
                requiresConfirmation = $requiresConfirmation
            }
        }
    }
}

if (-not (Test-Path $DatasetPath)) {
    throw "[EvalRealModel] Dataset not found: $DatasetPath"
}

$datasetReportPath = [System.IO.Path]::ChangeExtension($OutputReport, ".dataset_validation.md")
$knowledgeReportPath = [System.IO.Path]::ChangeExtension($OutputReport, ".knowledge_preflight.md")
$datasetValidationStatus = if ($DryRun.IsPresent -or $SkipDatasetValidation.IsPresent) { "SKIPPED" } else { "PENDING" }
$apiReadinessStatus = if ($RequireApiReady.IsPresent) { "PENDING" } else { "SKIPPED" }
$apiHealthPreflightStatus = if ($DryRun.IsPresent) { "SKIPPED" } else { "PENDING" }
$knowledgePreflightStatus = if ($DryRun.IsPresent) { "SKIPPED" } elseif ($KnowledgePreflightWaived.IsPresent) { "WAIVED" } elseif ($SkipKnowledgePreflight.IsPresent) { "SKIPPED" } else { "PENDING" }
$sessionBootstrapStatus = if ($DryRun.IsPresent) { "SKIPPED" } elseif ([string]::IsNullOrWhiteSpace($SessionToken)) { "PENDING" } else { "PROVIDED" }

if ((-not $DryRun.IsPresent) -and (-not $SkipDatasetValidation.IsPresent)) {
    powershell -ExecutionPolicy Bypass -File scripts/validate_eval_dataset.ps1 `
        -DatasetPath $DatasetPath `
        -MinScenarios $MinScenarioCount `
        -ReportPath $datasetReportPath
    if ($LASTEXITCODE -ne 0) {
        $datasetValidationStatus = "FAIL"
        throw "[EvalRealModel] Dataset validation failed. See $datasetReportPath"
    }

    $datasetValidationStatus = "PASS"
}

$scenarioLines = Get-Content -Path $DatasetPath -Encoding UTF8 | Where-Object { $_ -and -not $_.TrimStart().StartsWith("#") }
$scenarios = @()
$lineNumber = 0
foreach ($line in $scenarioLines) {
    $lineNumber++
    try {
        $scenarios += ($line | ConvertFrom-Json)
    }
    catch {
        throw "[EvalRealModel] Invalid JSONL line $lineNumber in $DatasetPath"
    }
}

if ($scenarios.Count -eq 0) {
    throw "[EvalRealModel] Dataset is empty."
}

if ((-not $DryRun.IsPresent) -and ($scenarios.Count -lt $MinScenarioCount)) {
    throw "[EvalRealModel] Dataset contains $($scenarios.Count) scenarios, required at least $MinScenarioCount."
}

if ($MaxScenarios -lt 1) {
    throw "[EvalRealModel] MaxScenarios must be >= 1."
}

if ((-not $DryRun.IsPresent) -and ($scenarios.Count -lt $MaxScenarios)) {
    throw "[EvalRealModel] Dataset contains $($scenarios.Count) scenarios, but MaxScenarios=$MaxScenarios requires at least that many."
}

$sample = $scenarios | Select-Object -First $MaxScenarios
$results = @()
$errors = @()
$pass = 0
$runtimeErrors = 0
$qualityFailures = 0
$knowledgePreflightDetail = ""
$token = $SessionToken
$resolvedApiKey = $ApiKey
$controlPlaneHeaders = @{}
if ([string]::IsNullOrWhiteSpace($resolvedApiKey)) {
    $resolvedApiKey = $env:HELPER_API_KEY
}

if ($RequireApiReady.IsPresent) {
    if ([string]::IsNullOrWhiteSpace($ReadinessReportPath)) {
        $ReadinessReportPath = [System.IO.Path]::ChangeExtension($OutputReport, ".api_ready.md")
    }

    if ([string]::IsNullOrWhiteSpace($ApiRuntimeDir)) {
        $reportDir = [System.IO.Path]::GetDirectoryName($OutputReport)
        $ApiRuntimeDir = if ([string]::IsNullOrWhiteSpace($reportDir)) {
            Join-Path (Get-HelperPathConfig -WorkspaceRoot $PSScriptRoot).HelperRoot "runtime"
        }
        else {
            Join-Path $reportDir "runtime"
        }
    }

    if ([string]::IsNullOrWhiteSpace($RuntimeMetadataPath)) {
        $RuntimeMetadataPath = Resolve-HelperRuntimeMetadataPath -RuntimeDir $ApiRuntimeDir
    }

    $apiReadyArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $PSScriptRoot "common\Ensure-HelperApiReady.ps1"),
        "-ApiBase", $ApiBase,
        "-TimeoutSec", $ReadinessTimeoutSec,
        "-PollIntervalMs", $ReadinessPollIntervalMs,
        "-RuntimeDir", $ApiRuntimeDir,
        "-ReportPath", $ReadinessReportPath,
        "-RuntimeMetadataPath", $RuntimeMetadataPath,
        "-NoBuild"
    )
    if ($LaunchLocalApiIfUnavailable.IsPresent) {
        $apiReadyArgs += @(
            "-StartLocalApiIfUnavailable",
            "-EnableStrictAuditMode",
            "-StrictAuditMaxOutstandingTurns", "4"
        )
    }

    powershell @apiReadyArgs
    if ($LASTEXITCODE -ne 0) {
        $apiReadinessStatus = "FAIL"
        throw "[EvalRealModel] API readiness preflight failed."
    }

    $apiReadinessStatus = "PASS"
}

if (-not $DryRun.IsPresent) {
    $healthUrl = "$ApiBase/api/health"
    Write-Host "[EvalRealModel] API health preflight via $healthUrl"
    try {
        $null = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 20
        $apiHealthPreflightStatus = "PASS"
    }
    catch {
        $apiHealthPreflightStatus = "FAIL"
        throw "[EvalRealModel] API health preflight failed: $($_.Exception.Message)"
    }

    if (-not $SkipKnowledgePreflight.IsPresent) {
        Write-Host "[EvalRealModel] Knowledge preflight via scripts/ensure_qdrant_eval_collections.ps1"
        try {
            powershell -ExecutionPolicy Bypass -File scripts/ensure_qdrant_eval_collections.ps1 `
                -QdrantBase $QdrantBase `
                -ReportPath $knowledgeReportPath
            if ($LASTEXITCODE -ne 0) {
                $knowledgePreflightStatus = "FAIL"
                $knowledgePreflightDetail = "knowledge preflight exit code != 0"
                if ($FailOnKnowledgePreflight.IsPresent) {
                    throw "[EvalRealModel] Knowledge preflight failed. See $knowledgeReportPath"
                }

                $knowledgePreflightStatus = "WARN_CONTINUED"
                Write-Warning "[EvalRealModel] Knowledge preflight failed but continuation is allowed. See $knowledgeReportPath"
            }
            else {
                $knowledgePreflightStatus = "PASS"
            }
        }
        catch {
            if ($knowledgePreflightStatus -ne "WARN_CONTINUED") {
                $knowledgePreflightStatus = "FAIL"
            }
            $knowledgePreflightDetail = $_.Exception.Message
            if ($FailOnKnowledgePreflight.IsPresent) {
                throw
            }

            $knowledgePreflightStatus = "WARN_CONTINUED"
            Write-Warning "[EvalRealModel] Knowledge preflight exception but continuation is allowed: $($_.Exception.Message)"
        }
    }

    if ([string]::IsNullOrWhiteSpace($token)) {
        Write-Host "[EvalRealModel] Bootstrapping session token via $ApiBase/api/auth/session"
        try {
            $token = Get-SessionAccessToken -ApiBaseUrl $ApiBase -ResolvedApiKey $resolvedApiKey -RequestedTtlMinutes $SessionTtlMinutes -RequestedSurface "conversation" -RequestedScopes @("chat:read", "chat:write")
            $sessionBootstrapStatus = "PASS"
        }
        catch {
            $sessionBootstrapStatus = "FAIL"
            throw "[EvalRealModel] Failed to get session token. Start API or pass -SessionToken."
        }
    }

    if ([string]::IsNullOrWhiteSpace($token)) {
        $sessionBootstrapStatus = "FAIL"
        throw "[EvalRealModel] Empty session token."
    }
}

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($token)) {
    $headers["Authorization"] = "Bearer $token"
}
if (-not [string]::IsNullOrWhiteSpace($resolvedApiKey)) {
    $headers["X-API-KEY"] = $resolvedApiKey
    $controlPlaneHeaders["X-API-KEY"] = $resolvedApiKey
}

if (-not $DryRun.IsPresent) {
    if (-not [string]::IsNullOrWhiteSpace($resolvedApiKey)) {
        if (-not [string]::IsNullOrWhiteSpace($token)) {
            $controlPlaneHeaders["Authorization"] = "Bearer $token"
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($token)) {
        try {
            $controlPlaneToken = Get-SessionAccessToken -ApiBaseUrl $ApiBase -ResolvedApiKey $resolvedApiKey -RequestedTtlMinutes $SessionTtlMinutes -RequestedSurface "runtime_console" -RequestedScopes @("metrics:read")
            if (-not [string]::IsNullOrWhiteSpace($controlPlaneToken)) {
                $controlPlaneHeaders["Authorization"] = "Bearer $controlPlaneToken"
            }
        }
        catch {
            Write-Warning "[EvalRealModel] Failed to bootstrap runtime-console session for audit drain: $($_.Exception.Message)"
        }
    }
}

$chatUrl = "$ApiBase/api/chat"

if ((-not $DryRun.IsPresent) -and (-not $SkipWarmup.IsPresent)) {
    $warmupTimeout = [Math]::Max(30, [Math]::Min(120, $RequestTimeoutSec))
    Write-Host "[EvalRealModel] Running chat warmup preflight"
    Invoke-EvalWarmup -ChatUrl $chatUrl -Headers $headers -TimeoutSec $warmupTimeout
}

$evalRunStartedAtUtc = [DateTimeOffset]::UtcNow
$scenarioIndex = 0
foreach ($scenario in $sample) {
    $scenarioIndex++
    if (($ProgressEvery -gt 0) -and ($scenarioIndex % $ProgressEvery -eq 0)) {
        Write-Host ("[EvalRealModel] Progress {0}/{1} | runtimeErrors={2} qualityFailures={3}" -f $scenarioIndex, $sample.Count, $runtimeErrors, $qualityFailures)
    }

    if ($DryRun.IsPresent) {
        $pass++
        $results += [PSCustomObject]@{
            id = $scenario.id
            kind = $scenario.kind
            language = $scenario.language
            conversationId = ""
            turnId = ""
            passed = $true
            mode = "dry-run"
            runtimeError = $false
            responseSignal = "dry_run"
            failReason = ""
            sourcesCount = 0
            citationCoverage = 0.0
            groundingStatus = ""
            requiresConfirmation = $false
            attempts = 1
            correlationId = ""
            requestStartedAtUtc = ""
            requestCompletedAtUtc = ""
            auditDecision = "dry_run"
            auditEligible = $false
            auditExpectedTrace = $false
            auditStrictMode = $false
        }
        continue
    }

    $bodyJson = @{
        message = [string]$scenario.prompt
        conversationId = $null
        maxHistory = 24
        systemInstruction = $null
    } | ConvertTo-Json -Depth 8 -Compress
    $scenarioAttempts = 0
    $maxScenarioAttempts = 3
    $scenarioCompleted = $false
    $phaseMarker = "scenario_initialized"

    while ((-not $scenarioCompleted) -and ($scenarioAttempts -lt $maxScenarioAttempts)) {
        $scenarioAttempts++
        try {
            $phaseMarker = "before_request"
            $requestStartedAtUtc = [DateTimeOffset]::UtcNow
            $response = Invoke-RestMethod -Uri $chatUrl -Method Post -Headers $headers -ContentType "application/json; charset=utf-8" -Body $bodyJson -TimeoutSec $RequestTimeoutSec
            $phaseMarker = "after_request"
            $requestCompletedAtUtc = [DateTimeOffset]::UtcNow
            $phaseMarker = "after_parse_response"
            $successCorrelationId = ""
            $phaseMarker = "after_headers"
            $auditStatus = Get-SafePropertyValue -InputObject $response -PropertyName "auditStatus"
            $auditDecision = [string](Get-SafePropertyValue -InputObject $auditStatus -PropertyName "decision")
            if ([string]::IsNullOrWhiteSpace($auditDecision)) {
                $auditDecision = "unknown"
            }
            $auditEligible = Get-SafeBooleanPropertyValue -InputObject $auditStatus -PropertyName "eligible"
            $auditExpectedTrace = Get-SafeBooleanPropertyValue -InputObject $auditStatus -PropertyName "expectedTrace"
            $auditStrictMode = Get-SafeBooleanPropertyValue -InputObject $auditStatus -PropertyName "strictMode"
            $evaluation = Get-ScenarioEvaluation -Scenario $scenario -Response $response
            $phaseMarker = "after_evaluation"
            $passed = [bool]$evaluation.passed
            if ($passed) { $pass++ }
            $results += [PSCustomObject]@{
                id = $scenario.id
                kind = $scenario.kind
                language = $scenario.language
                conversationId = [string]$response.ConversationId
                turnId = [string]$response.TurnId
                passed = $passed
                mode = "live"
                runtimeError = $false
                responseSignal = [string]$evaluation.responseSignal
                failReason = [string]$evaluation.failReason
                sourcesCount = [int]$evaluation.sourcesCount
                citationCoverage = [double]$evaluation.citationCoverage
                groundingStatus = [string]$evaluation.groundingStatus
                requiresConfirmation = [bool]$evaluation.requiresConfirmation
                attempts = $scenarioAttempts
                correlationId = $successCorrelationId
                requestStartedAtUtc = $requestStartedAtUtc.ToString("o")
                requestCompletedAtUtc = $requestCompletedAtUtc.ToString("o")
                auditDecision = $auditDecision
                auditEligible = $auditEligible
                auditExpectedTrace = $auditExpectedTrace
                auditStrictMode = $auditStrictMode
            }
            $phaseMarker = "after_result_append"
            $scenarioCompleted = $true
        }
        catch {
            $statusCode = Get-HttpStatusCode -Exception $_.Exception
            $isTimeout = Test-IsTimeoutException -Exception $_.Exception
            if (($scenarioAttempts -lt $maxScenarioAttempts) -and (($statusCode -eq 401) -or $isTimeout)) {
                try {
                    if ($statusCode -eq 401) {
                        $token = Get-SessionAccessToken -ApiBaseUrl $ApiBase -ResolvedApiKey $resolvedApiKey -RequestedTtlMinutes $SessionTtlMinutes
                        if (-not [string]::IsNullOrWhiteSpace($token)) {
                            $headers["Authorization"] = "Bearer $token"
                        }
                    }
                    else {
                        Start-Sleep -Milliseconds ([Math]::Max(100, $RetryBackoffMs))
                    }

                    if (($statusCode -eq 401) -or $isTimeout) {
                        continue
                    }
                }
                catch {
                    # Continue with default error path below.
                }
            }

            $runtimeErrors++
            $errors += [PSCustomObject]@{
                id = [string]$scenario.id
                kind = [string]$scenario.kind
                language = [string]$scenario.language
                conversationId = ""
                turnId = ""
                statusCode = if ($null -eq $statusCode) { "n/a" } else { [string]$statusCode }
                exceptionType = $_.Exception.GetType().FullName
                message = $_.Exception.Message
                phase = $phaseMarker
                scriptStackTrace = $_.ScriptStackTrace
                correlationId = Get-CorrelationId -Exception $_.Exception
                attempts = $scenarioAttempts
            }

            $results += [PSCustomObject]@{
                id = $scenario.id
                kind = $scenario.kind
                language = $scenario.language
                conversationId = ""
                turnId = ""
                passed = $false
                mode = "live"
                runtimeError = $true
                responseSignal = "runtime_exception"
                failReason = "runtime_exception"
                sourcesCount = 0
                citationCoverage = 0.0
                groundingStatus = ""
                requiresConfirmation = $false
                attempts = $scenarioAttempts
                correlationId = Get-CorrelationId -Exception $_.Exception
                requestStartedAtUtc = ""
                requestCompletedAtUtc = ""
                auditDecision = "runtime_error"
                auditEligible = $false
                auditExpectedTrace = $false
                auditStrictMode = $false
            }
            $scenarioCompleted = $true
        }
    }
}

$total = $results.Count
$passRate = if ($total -eq 0) { 0.0 } else { $pass / [double]$total }
$qualityFailures = @($results | Where-Object { (-not $_.runtimeError) -and (-not $_.passed) }).Count
$qualityFailuresDetailed = @(
    $results |
        Where-Object { (-not $_.runtimeError) -and (-not $_.passed) } |
        ForEach-Object {
            [PSCustomObject]@{
                id = [string]$_.id
                kind = [string]$_.kind
                language = [string]$_.language
                conversationId = [string]$_.conversationId
                turnId = [string]$_.turnId
                failReason = [string]$_.failReason
                responseSignal = [string]$_.responseSignal
                sourcesCount = [int]$_.sourcesCount
                citationCoverage = [double]$_.citationCoverage
                groundingStatus = [string]$_.groundingStatus
                requiresConfirmation = [bool]$_.requiresConfirmation
                attempts = [int]$_.attempts
            }
        }
)
$runtimeFailureRate = if ($total -eq 0) { 0.0 } else { $runtimeErrors / [double]$total }
$qualityFailureRate = if ($total -eq 0) { 0.0 } else { $qualityFailures / [double]$total }
$transportTimeoutCount = @(
    $errors | Where-Object {
        $status = [string]$_.statusCode
        $message = [string]$_.message
        ($status -eq "n/a") -and (
            (Test-ContainsIgnoreCase -InputText $message -Needle "timed out") -or
            (Test-ContainsIgnoreCase -InputText $message -Needle "timeout")
        )
    }
).Count
$httpTimeoutCount = @(
    $errors | Where-Object {
        $status = [string]$_.statusCode
        ($status -eq "408") -or ($status -eq "499") -or ($status -eq "504")
    }
).Count
$server5xxCount = @(
    $errors | Where-Object {
        $code = 0
        [int]::TryParse([string]$_.statusCode, [ref]$code) -and ($code -ge 500) -and ($code -le 599)
    }
).Count
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"

if ([string]::IsNullOrWhiteSpace($ErrorLogPath)) {
    $ErrorLogPath = [System.IO.Path]::ChangeExtension($OutputReport, ".errors.json")
}

$errorDir = [System.IO.Path]::GetDirectoryName($ErrorLogPath)
if (-not [string]::IsNullOrWhiteSpace($errorDir)) {
    New-Item -ItemType Directory -Force -Path $errorDir | Out-Null
}

$errorsPayload = @{
    generated = $timestamp
    apiBase = $ApiBase
    dataset = $DatasetPath
    total = $total
    runtimeErrors = $runtimeErrors
    qualityFailures = $qualityFailures
    runtimeTaxonomy = @{
        transportTimeout = $transportTimeoutCount
        httpTimeoutOrCanceled = $httpTimeoutCount
        server5xx = $server5xxCount
        qualityFail = $qualityFailures
    }
    qualityFailuresDetailed = $qualityFailuresDetailed
    errors = $errors
}
$errorsPayload | ConvertTo-Json -Depth 8 | Set-Content -Path $ErrorLogPath -Encoding UTF8

$pathConfig = Get-HelperPathConfig -WorkspaceRoot $PSScriptRoot
$runtimeMetadata = Import-HelperRuntimeMetadata -Path $RuntimeMetadataPath
$defaultAuditTraceSourcePath = Join-Path $pathConfig.LogsRoot "post_turn_audit.trace.jsonl"
$auditTraceSourcePath = if (($null -ne $runtimeMetadata) -and -not [string]::IsNullOrWhiteSpace([string]$runtimeMetadata.auditTracePath)) {
    [string]$runtimeMetadata.auditTracePath
}
else {
    $defaultAuditTraceSourcePath
}
$auditTraceOutputPath = [System.IO.Path]::ChangeExtension($OutputReport, ".audit_trace.json")
$runtimeMetadataAuditPath = if ($null -eq $runtimeMetadata) { "" } else { [string]$runtimeMetadata.auditTracePath }
$normalizedMetadataAuditPath = if ([string]::IsNullOrWhiteSpace($runtimeMetadataAuditPath)) { "" } else { [System.IO.Path]::GetFullPath($runtimeMetadataAuditPath) }
$normalizedAuditTraceSourcePath = if ([string]::IsNullOrWhiteSpace($auditTraceSourcePath)) { "" } else { [System.IO.Path]::GetFullPath($auditTraceSourcePath) }
$traceabilityPathMismatch = (-not [string]::IsNullOrWhiteSpace($normalizedMetadataAuditPath)) -and ($normalizedMetadataAuditPath -ne $normalizedAuditTraceSourcePath)
$completedTurnResults = @(
    $results | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string]$_.conversationId) -and
        -not [string]::IsNullOrWhiteSpace([string]$_.turnId)
    }
)
$resultsWithAuditMetadata = @(
    $completedTurnResults | Where-Object {
        $decision = [string]$_.auditDecision
        -not [string]::IsNullOrWhiteSpace($decision) -and ($decision -ne "unknown")
    }
)
$eligibleAuditResults = @(
    $completedTurnResults | Where-Object { $_.auditEligible -eq $true }
)
$resultsExpectedTrace = @(
    $completedTurnResults | Where-Object { $_.auditExpectedTrace -eq $true }
)
$skippedEligibleAuditResults = @(
    $eligibleAuditResults | Where-Object { $_.auditExpectedTrace -ne $true }
)
$strictAuditObserved = @(
    $completedTurnResults | Where-Object { $_.auditStrictMode -eq $true }
).Count -gt 0
$missingAuditDecisionCount = [Math]::Max(0, $completedTurnResults.Count - $resultsWithAuditMetadata.Count)
$runStartCandidates = @(
    $completedTurnResults |
        ForEach-Object {
            $raw = [string]$_.requestStartedAtUtc
            if (-not [string]::IsNullOrWhiteSpace($raw)) {
                $parsed = [datetimeoffset]::MinValue
                if ([datetimeoffset]::TryParse($raw, [ref]$parsed)) { $parsed }
            }
        }
)
$runCompleteCandidates = @(
    $completedTurnResults |
        ForEach-Object {
            $raw = [string]$_.requestCompletedAtUtc
            if (-not [string]::IsNullOrWhiteSpace($raw)) {
                $parsed = [datetimeoffset]::MinValue
                if ([datetimeoffset]::TryParse($raw, [ref]$parsed)) { $parsed }
            }
        }
)
$runStartedAtUtc = if ($runStartCandidates.Count -gt 0) { ($runStartCandidates | Sort-Object | Select-Object -First 1) } else { $evalRunStartedAtUtc }
$runCompletedAtUtc = if ($runCompleteCandidates.Count -gt 0) { ($runCompleteCandidates | Sort-Object -Descending | Select-Object -First 1) } else { [DateTimeOffset]::UtcNow }
$traceDrainStatus = if ($DryRun.IsPresent) {
    [PSCustomObject]@{
        status = "SKIPPED_DRY_RUN"
        reason = "dry_run"
        pending = 0
        processed = 0
        failed = 0
        traceExists = $false
        traceLineCount = 0
        stablePolls = 0
        elapsedSec = 0
        readStatus = "skipped"
        lastProcessedAtUtc = ""
        polledAtUtc = ""
    }
}
elseif ($resultsExpectedTrace.Count -eq 0) {
    [PSCustomObject]@{
        status = "WARN_RUNTIME_PARTIAL"
        reason = "no_expected_trace_keys"
        pending = -1
        processed = -1
        failed = -1
        traceExists = Test-Path $auditTraceSourcePath
        traceLineCount = -1
        stablePolls = 0
        elapsedSec = 0
        readStatus = "skipped"
        lastProcessedAtUtc = ""
        polledAtUtc = ""
    }
}
else {
    Wait-PostTurnAuditDrain -ApiBase $ApiBase -Headers $controlPlaneHeaders -AuditTracePath $auditTraceSourcePath -ExpectedTraceEntries $resultsExpectedTrace.Count -TimeoutSec $AuditDrainTimeoutSec -PollIntervalMs $AuditDrainPollIntervalMs
}

$auditTracePayload = @{
    generated = $timestamp
    sourcePath = $auditTraceSourcePath
    runtimeMetadataPath = $RuntimeMetadataPath
    runtimeMetadataStatus = if ($null -eq $runtimeMetadata) { "missing_or_invalid" } else { [string]$runtimeMetadata.status }
    runtimeMetadataLauncherMode = if ($null -eq $runtimeMetadata) { "" } else { [string]$runtimeMetadata.launcherMode }
    runtimeMetadataLogsRoot = if ($null -eq $runtimeMetadata) { "" } else { [string]$runtimeMetadata.logsRoot }
    runtimeMetadataAuditTracePath = $runtimeMetadataAuditPath
    pathMismatch = $traceabilityPathMismatch
    drainBarrier = $traceDrainStatus
    totalCompletedTurns = $completedTurnResults.Count
    totalEligibleAuditTurns = $eligibleAuditResults.Count
    totalExpectedTraceTurns = $resultsExpectedTrace.Count
    totalSkippedEligibleAuditTurns = $skippedEligibleAuditResults.Count
    missingAuditDecisionCount = $missingAuditDecisionCount
    strictAuditObserved = $strictAuditObserved
}
$joinedAuditTracePayload = Invoke-AuditTraceJoin -SourcePath $auditTraceSourcePath -Results $resultsExpectedTrace -RunStartedAtUtc $runStartedAtUtc -RunCompletedAtUtc $runCompletedAtUtc
foreach ($property in $joinedAuditTracePayload.PSObject.Properties) {
    $auditTracePayload[$property.Name] = $property.Value
}

$traceabilityStatus = "FAIL_JOIN_UNVERIFIED"
if ($DryRun.IsPresent) {
    $traceabilityStatus = "SKIPPED_DRY_RUN"
}
elseif ($traceabilityPathMismatch) {
    $traceabilityStatus = "FAIL_PATH_MISMATCH"
}
elseif ($missingAuditDecisionCount -gt 0) {
    $traceabilityStatus = "FAIL_DECISION_UNAVAILABLE"
}
elseif ($joinedAuditTracePayload.readStatus -in @("missing_file", "failed", "not_available")) {
    $traceabilityStatus = "FAIL_JOIN_UNVERIFIED"
}
elseif ($resultsExpectedTrace.Count -eq 0) {
    $traceabilityStatus = if ($eligibleAuditResults.Count -eq 0) { "SKIPPED_NO_AUDIT_EXPECTED" } else { "WARN_PARTIAL_COVERAGE" }
}
elseif (($joinedAuditTracePayload.matched -eq $resultsExpectedTrace.Count) -and ($traceDrainStatus.status -eq "PASS") -and ($skippedEligibleAuditResults.Count -eq 0)) {
    $traceabilityStatus = "PASS"
}
elseif (($joinedAuditTracePayload.matched -eq $resultsExpectedTrace.Count) -and ($traceDrainStatus.status -eq "PASS")) {
    $traceabilityStatus = "WARN_PARTIAL_COVERAGE"
}
else {
    $traceabilityStatus = if ($joinedAuditTracePayload.matched -gt 0) { "WARN_PARTIAL_COVERAGE" } else { "WARN_RUNTIME_PARTIAL" }
}

$auditTracePayload.traceabilityStatus = $traceabilityStatus
$auditTracePayload.runWindow = [ordered]@{
    startedAtUtc = $runStartedAtUtc.ToString("o")
    completedAtUtc = $runCompletedAtUtc.ToString("o")
}

$auditTraceDir = [System.IO.Path]::GetDirectoryName($auditTraceOutputPath)
if (-not [string]::IsNullOrWhiteSpace($auditTraceDir)) {
    New-Item -ItemType Directory -Force -Path $auditTraceDir | Out-Null
}

$auditTracePayload | ConvertTo-Json -Depth 8 | Set-Content -Path $auditTraceOutputPath -Encoding UTF8

$requestedEvidenceLevelDisplay = if ([string]::IsNullOrWhiteSpace($requestedEvidenceLevel)) { "auto" } else { $requestedEvidenceLevel }
$authoritativeRequested = ($requestedEvidenceLevel -eq "authoritative") -or (($requestedEvidenceLevelDisplay -eq "auto") -and ($EvidenceLevel -eq "authoritative"))
$datasetGateOk = $datasetValidationStatus -eq "PASS"
$apiReadinessGateOk = $apiReadinessStatus -eq "PASS"
$apiHealthGateOk = $apiHealthPreflightStatus -eq "PASS"
$knowledgeGateOk = ($knowledgePreflightStatus -eq "PASS") -or ($knowledgePreflightStatus -eq "WAIVED")
$runtimeGateOk = $runtimeErrors -eq 0
$qualityGateOk = $passRate -ge 0.85
$traceabilityGateOk = $traceabilityStatus -in @("PASS", "SKIPPED_NO_AUDIT_EXPECTED")
$authoritativeEligible = (-not $DryRun.IsPresent) -and ($EvidenceLevel -eq "authoritative") -and $datasetGateOk -and $apiReadinessGateOk -and $apiHealthGateOk -and $knowledgeGateOk -and $runtimeGateOk -and $qualityGateOk -and $traceabilityGateOk
$nonAuthoritativeReasons = New-Object System.Collections.Generic.List[string]

if ($DryRun.IsPresent) {
    $nonAuthoritativeReasons.Add("dry_run_mode")
}

if ($EvidenceLevel -ne "authoritative") {
    $nonAuthoritativeReasons.Add("effective_evidence_level_" + $EvidenceLevel)
}

if (-not $datasetGateOk) {
    $nonAuthoritativeReasons.Add("dataset_validation_" + $datasetValidationStatus.ToLowerInvariant())
}

if (-not $apiReadinessGateOk) {
    $nonAuthoritativeReasons.Add("api_readiness_" + $apiReadinessStatus.ToLowerInvariant())
}

if (-not $apiHealthGateOk) {
    $nonAuthoritativeReasons.Add("api_health_" + $apiHealthPreflightStatus.ToLowerInvariant())
}

if (-not $knowledgeGateOk) {
    $nonAuthoritativeReasons.Add("knowledge_preflight_" + $knowledgePreflightStatus.ToLowerInvariant())
}

if (-not $runtimeGateOk) {
    $nonAuthoritativeReasons.Add("runtime_errors_present")
}

if (-not $qualityGateOk) {
    $nonAuthoritativeReasons.Add("pass_rate_below_threshold")
}

if (-not $traceabilityGateOk) {
    $nonAuthoritativeReasons.Add("traceability_" + $traceabilityStatus.ToLowerInvariant())
}

if ($authoritativeEligible) {
    $nonAuthoritativeReasons.Clear()
}

$lines = @()
$lines += "# Real-Model Eval Report"
$lines += "Generated: $timestamp"
$lines += "Mode: $(if ($DryRun.IsPresent) { "dry-run" } else { "live" })"
$lines += "Requested evidence level: $requestedEvidenceLevelDisplay"
$lines += "Evidence level: $EvidenceLevel"
$lines += "Dry-run demotion: $(if ($dryRunDemotion) { "YES" } else { "NO" })"
$lines += "Authoritative mode requested: $(if ($authoritativeRequested) { "YES" } else { "NO" })"
$lines += "Authoritative gate status: $(if ($authoritativeEligible) { "PASS" } else { "FAIL" })"
$lines += "Authoritative evidence: $(if ($authoritativeEligible) { "YES" } else { "NO" })"
$lines += "Non-authoritative reasons: $(if ($nonAuthoritativeReasons.Count -eq 0) { "none" } else { ($nonAuthoritativeReasons -join ", ") })"
$lines += "API: $ApiBase"
$lines += "Dataset: $DatasetPath"
$lines += "Scenarios run: $total"
$lines += "Runtime errors: $runtimeErrors"
$lines += "Quality failures (non-runtime): $qualityFailures"
$lines += "Pass rate: $([math]::Round($passRate * 100, 2))%"
$lines += "Dataset validation status: $datasetValidationStatus"
$lines += "Dataset validation report: $datasetReportPath"
$lines += "API readiness status: $apiReadinessStatus"
$lines += "API readiness report: $(if ([string]::IsNullOrWhiteSpace($ReadinessReportPath)) { "n/a" } else { $ReadinessReportPath })"
$lines += "API health preflight status: $apiHealthPreflightStatus"
$lines += "Knowledge preflight status: $knowledgePreflightStatus"
$lines += "Knowledge preflight report: $knowledgeReportPath"
$lines += "Knowledge preflight detail: $(if ([string]::IsNullOrWhiteSpace($knowledgePreflightDetail)) { "n/a" } else { $knowledgePreflightDetail })"
$lines += "Session bootstrap status: $sessionBootstrapStatus"
$lines += "Error log: $ErrorLogPath"
$lines += "Runtime metadata: $(if ([string]::IsNullOrWhiteSpace($RuntimeMetadataPath)) { "n/a" } else { $RuntimeMetadataPath })"
$lines += "Audit trace: $auditTraceOutputPath"
$lines += "Audit trace join status: $($auditTracePayload.readStatus)"
$lines += "Audit trace matched entries: $($auditTracePayload.matched)"
$lines += "Audit trace expected entries: $($auditTracePayload.totalExpectedTraceTurns)"
$lines += "Audit eligible turns: $($auditTracePayload.totalEligibleAuditTurns)"
$lines += "Audit skipped eligible turns: $($auditTracePayload.totalSkippedEligibleAuditTurns)"
$lines += "Audit decisions missing: $($auditTracePayload.missingAuditDecisionCount)"
$lines += "Strict audit observed: $(if ($auditTracePayload.strictAuditObserved) { "YES" } else { "NO" })"
$lines += "Traceability status: $traceabilityStatus"
$lines += "Audit drain barrier: $($traceDrainStatus.status)"
$lines += "Audit drain reason: $($traceDrainStatus.reason)"
$lines += ""
$lines += "| Kind | Total | Passed | Pass Rate |"
$lines += "|---|---:|---:|---:|"

$byKind = $results | Group-Object kind
foreach ($group in $byKind) {
    $kindTotal = $group.Count
    $kindPass = @($group.Group | Where-Object { $_.passed }).Count
    $kindRate = if ($kindTotal -eq 0) { 0 } else { [math]::Round(($kindPass / [double]$kindTotal) * 100, 2) }
    $lines += "| $($group.Name) | $kindTotal | $kindPass | $kindRate% |"
}

$lines += ""
$lines += "## Runtime Error Summary"
if ($errors.Count -eq 0) {
    $lines += "- none"
}
else {
    $lines += "### By status code"
    $byStatus = $errors | Group-Object statusCode | Sort-Object Count -Descending
    foreach ($group in $byStatus) {
        $lines += "- HTTP $($group.Name): $($group.Count)"
    }
    $lines += ""
    $lines += "### By exception type"
    $byType = $errors | Group-Object exceptionType | Sort-Object Count -Descending
    foreach ($group in $byType) {
        $lines += "- $($group.Name): $($group.Count)"
    }
}

$lines += ""
$lines += "## Quality Failure Summary"
if ($qualityFailuresDetailed.Count -eq 0) {
    $lines += "- none"
}
else {
    $lines += "### By reason"
    $byQualityReason = $qualityFailuresDetailed | Group-Object failReason | Sort-Object Count -Descending
    foreach ($group in $byQualityReason) {
        $lines += "- $($group.Name): $($group.Count)"
    }
}

$lines += ""
$lines += "## Timeout/5xx Taxonomy"
$lines += "- Transport timeout (status=n/a + timeout message): $transportTimeoutCount"
$lines += "- HTTP timeout/canceled (408/499/504): $httpTimeoutCount"
$lines += "- Server 5xx: $server5xxCount"
$lines += "- Quality fail (non-runtime): $qualityFailures"

$lines += ""
$lines += "## Failure Classification"
$lines += "- Runtime/transport failure rate: $([math]::Round($runtimeFailureRate * 100, 2))%"
$lines += "- Model quality failure rate: $([math]::Round($qualityFailureRate * 100, 2))%"
$lines += "- Runtime gate (must be 0 errors before quality threshold): $(if ($runtimeErrors -eq 0) { "PASS" } else { "FAIL" })"
$lines += ""
$lines += "Threshold: >= 85% overall."
$lines += "Status: $(if ($passRate -ge 0.85) { "PASS" } else { "FAIL" })"

$outDir = [System.IO.Path]::GetDirectoryName($OutputReport)
if (-not [string]::IsNullOrWhiteSpace($outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

Set-Content -Path $OutputReport -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[EvalRealModel] Report saved to $OutputReport"
Write-Host "[EvalRealModel] Error log saved to $ErrorLogPath"

if ((-not $DryRun.IsPresent) -and ($runtimeErrors -gt 0) -and (-not $NoFailOnRuntimeErrors.IsPresent)) {
    throw "[EvalRealModel] Runtime errors detected: $runtimeErrors"
}

if ((-not $DryRun.IsPresent) -and ($passRate -lt 0.85) -and (-not $NoFailOnThreshold.IsPresent)) {
    throw "[EvalRealModel] Pass rate below threshold: $([math]::Round($passRate * 100, 2))%"
}

Write-Host "[EvalRealModel] Completed." -ForegroundColor Green
