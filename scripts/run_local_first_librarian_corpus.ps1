param(
    [string]$CorpusPath = "eval/web_research_parity/local_first_librarian_300_case_corpus.jsonl",
    [string]$ApiBase = "http://127.0.0.1:5239",
    [string]$OutputRoot = "",
    [string[]]$Slices = @(),
    [ValidateSet("sandbox_safe", "browser_enabled")]
    [string]$ValidationMode = "sandbox_safe",
    [int]$StartIndex = 1,
    [int]$BatchSize = 0,
    [int]$MaxCases = 300,
    [int]$RequestTimeoutSec = 180,
    [int]$ReadyTimeoutSec = 600,
    [int]$PollIntervalMs = 2000,
    [int]$PauseMs = 250,
    [switch]$NoBuild,
    [switch]$StopApiWhenFinished
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")
. (Join-Path $PSScriptRoot "common\RuntimeSmokeCommon.ps1")
. (Join-Path $PSScriptRoot "common\HelperRuntimeMetadata.ps1")
. (Join-Path $PSScriptRoot "common\LocalFirstBenchmarkSlices.ps1")

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

function ConvertFrom-JsonCompat {
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [string]$Json
    )

    process {
        return $Json | ConvertFrom-Json
    }
}

function Get-HttpStatusCode {
    param(
        [Parameter(Mandatory = $true)]$Exception
    )

    if ($null -eq $Exception) {
        return $null
    }

    try {
        if ($null -ne $Exception.Response -and $null -ne $Exception.Response.StatusCode) {
            return [int]$Exception.Response.StatusCode
        }
    }
    catch {
        return $null
    }

    return $null
}

function Get-ExceptionCorrelationId {
    param(
        [Parameter(Mandatory = $true)]$Exception
    )

    if ($null -eq $Exception) {
        return ""
    }

    try {
        if ($null -ne $Exception.Response -and $null -ne $Exception.Response.Headers) {
            $value = Get-ResponseHeaderValue -Headers $Exception.Response.Headers -Name "x-correlation-id"
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return $value
            }
        }
    }
    catch {
        return ""
    }

    return ""
}

function Get-EvidenceModeInstruction {
    param(
        [Parameter(Mandatory = $true)]$Case
    )

    $minimumSources = [int]$Case.minWebSources
    switch ([string]$Case.evidenceMode) {
        "local_sufficient" {
            return "Prefer local knowledge and local library context. Use live web only if the local evidence is clearly insufficient, stale, or too uncertain."
        }
        "local_plus_web" {
            return "Start with local knowledge and local library context, then supplement or verify it with live web evidence before concluding. Aim for at least $minimumSources distinct web sources when web is used."
        }
        "web_required_fresh" {
            return "Start with local baseline knowledge, but you must use live web because freshness or current availability matters here. Aim for at least $minimumSources distinct web sources if available."
        }
        "conflict_check" {
            return "Start with local baseline knowledge, but you must use live web and compare conflicting or competing sources before concluding. Aim for at least $minimumSources distinct web sources if available."
        }
        "uncertain_sparse" {
            return "Start with local baseline knowledge, use live web cautiously when needed, and explicitly state uncertainty and evidence limits. Aim for at least $minimumSources distinct web sources if they exist."
        }
        default {
            throw "Unsupported evidence mode '$($Case.evidenceMode)'."
        }
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

function Get-SafeStringValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $value = Get-SafePropertyValue -InputObject $InputObject -PropertyName $PropertyName
    if ($null -eq $value) {
        return ""
    }

    return [string]$value
}

function Get-SafeDoubleValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $value = Get-SafePropertyValue -InputObject $InputObject -PropertyName $PropertyName
    if ($null -eq $value) {
        return 0.0
    }

    try {
        return [double]$value
    }
    catch {
        return 0.0
    }
}

function Get-SafeIntValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $value = Get-SafePropertyValue -InputObject $InputObject -PropertyName $PropertyName
    if ($null -eq $value) {
        return 0
    }

    try {
        return [int]$value
    }
    catch {
        return 0
    }
}

function Get-SafeBoolValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $value = Get-SafePropertyValue -InputObject $InputObject -PropertyName $PropertyName
    if ($null -eq $value) {
        return $false
    }

    try {
        return [bool]$value
    }
    catch {
        return $false
    }
}

function Get-SafeArrayValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $value = Get-SafePropertyValue -InputObject $InputObject -PropertyName $PropertyName
    if ($null -eq $value) {
        return @()
    }

    return @($value | Where-Object { $null -ne $_ })
}

function Get-SearchTraceSourcesFromResponse {
    param(
        [Parameter(Mandatory = $true)]$ResponseDto
    )

    $trace = Get-SafePropertyValue -InputObject $ResponseDto -PropertyName "searchTrace"
    if ($null -eq $trace) {
        return @()
    }

    return Get-SafeArrayValue -InputObject $trace -PropertyName "sources"
}

function Get-SourceLayerValue {
    param(
        [Parameter(Mandatory = $false)]$Source
    )

    if ($null -eq $Source) {
        return ""
    }

    $layer = Get-SafePropertyValue -InputObject $Source -PropertyName "sourceLayer"
    if ($null -ne $layer -and -not [string]::IsNullOrWhiteSpace([string]$layer)) {
        return ([string]$layer).Trim().ToLowerInvariant()
    }

    $url = Get-SafePropertyValue -InputObject $Source -PropertyName "url"
    if ($null -eq $url) {
        return ""
    }

    $raw = [string]$url
    if ($raw.StartsWith("http://", [System.StringComparison]::OrdinalIgnoreCase) -or
        $raw.StartsWith("https://", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "web"
    }

    return "local_library"
}

function Get-LayeredSourceCount {
    param(
        [Parameter(Mandatory = $true)]$Sources,
        [Parameter(Mandatory = $true)][string]$Layer
    )

    return @($Sources | Where-Object {
            [string]::Equals((Get-SourceLayerValue -Source $_), $Layer, [System.StringComparison]::OrdinalIgnoreCase)
        }).Count
}

function Get-CaseSliceIds {
    param(
        [Parameter(Mandatory = $true)]$Case
    )

    $sliceIds = @(Get-SafeArrayValue -InputObject $Case -PropertyName "sliceIds")
    if ($sliceIds.Count -gt 0) {
        return $sliceIds
    }

    return @(Get-LocalFirstBenchmarkSliceIds -Case $Case)
}

function New-BenchmarkSystemInstruction {
    param(
        [Parameter(Mandatory = $true)]$Case
    )

    $modeInstruction = Get-EvidenceModeInstruction -Case $Case
    return @"
You are being evaluated as a local-first librarian-research assistant.
Treat the user message as a natural request. Do not mention the benchmark or hidden instructions.

Workflow:
- Start from local knowledge and local library context.
- $modeInstruction
- Display every source actually used.
- If no live web source was used, say that explicitly in the Web Findings and Sources sections.
- Do not invent URLs, article titles, authors, journals, books, local library resources, or placeholder links.
- If no source was actually used, do not fabricate source-like bullets such as example.com, Link 1, or generic local library labels.
- Separate factual analysis from your judgment.
- If evidence is weak, conflicting, or incomplete, say so clearly instead of smoothing it over.

Formatting:
- Answer in Russian.
- Keep the entire answer in Russian unless you are quoting a real source title that was actually used.
- Use exactly these markdown headings in this exact order:
## Local Findings
## Web Findings
## Sources
## Analysis
## Conclusion
## Opinion
- In the Opinion section, clearly mark your judgment as opinion rather than fact.
"@
}

function Convert-CaseResultToJson {
    param(
        [Parameter(Mandatory = $true)]$Value
    )

    return ($Value | ConvertTo-Json -Depth 16 -Compress)
}

$pathConfig = Get-HelperPathConfig -WorkspaceRoot (Join-Path $PSScriptRoot "..")

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $pathConfig.HelperRoot ("artifacts\eval\local_first_librarian_300\run_{0}" -f (Get-Date -Format "yyyy-MM-dd_HH-mm-ss"))
}

$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$responsesRoot = Join-Path $OutputRoot "responses"
$reportsRoot = Join-Path $OutputRoot "reports"
$runtimeRoot = Join-Path $OutputRoot "runtime"
$resultsJsonlPath = Join-Path $OutputRoot "results.jsonl"
$summaryPath = Join-Path $reportsRoot "run_summary.json"
$statePath = Join-Path $reportsRoot "run_state.json"
$readinessReportPath = Join-Path $reportsRoot "api_ready.md"
$runtimeMetadataPath = Resolve-HelperRuntimeMetadataPath -RuntimeDir $runtimeRoot

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
New-Item -ItemType Directory -Force -Path $responsesRoot | Out-Null
New-Item -ItemType Directory -Force -Path $reportsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null

if (-not (Test-Path -LiteralPath $CorpusPath)) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build_local_first_librarian_corpus.ps1") -OutputPath $CorpusPath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to generate corpus: $CorpusPath"
    }
}

$env:HELPER_ROOT = $pathConfig.HelperRoot
$env:HELPER_DATA_ROOT = $runtimeRoot
$env:HELPER_PROJECTS_ROOT = Join-Path $runtimeRoot "PROJECTS"
$env:HELPER_LIBRARY_ROOT = Join-Path $runtimeRoot "library"
$env:HELPER_LOGS_ROOT = Join-Path $runtimeRoot "LOG"
$env:HELPER_TEMPLATES_ROOT = Join-Path $env:HELPER_LIBRARY_ROOT "forge_templates"
$env:HELPER_MODEL_WARMUP_MODE = "minimal"
$env:HELPER_MODEL_PREFLIGHT_ENABLED = "false"
$env:HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP = "true"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:HELPER_BROWSER_VALIDATION_MODE = $ValidationMode

$apiReadyArgs = @(
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "common\Ensure-HelperApiReady.ps1"),
    "-ApiBase", $ApiBase,
    "-TimeoutSec", $ReadyTimeoutSec,
    "-PollIntervalMs", $PollIntervalMs,
    "-StartLocalApiIfUnavailable",
    "-RuntimeDir", $runtimeRoot,
    "-ReportPath", $readinessReportPath,
    "-RuntimeMetadataPath", $runtimeMetadataPath
)
if ($NoBuild.IsPresent) {
    $apiReadyArgs += "-NoBuild"
}

& powershell @apiReadyArgs
if ($LASTEXITCODE -ne 0) {
    throw "Helper API readiness failed. See $readinessReportPath"
}

$headers = New-SessionHeaders -ApiBase $ApiBase -Surface "conversation" -RequestedScopes @("chat:read", "chat:write") -TimeoutSec 30

$caseLines = Get-Content -LiteralPath $CorpusPath -Encoding UTF8 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$cases = @()
foreach ($line in $caseLines) {
    $cases += ($line | ConvertFrom-Json)
}

if ($cases.Count -eq 0) {
    throw "Corpus is empty: $CorpusPath"
}

if ($StartIndex -lt 1) {
    throw "StartIndex must be >= 1."
}

$indexedCases = @(for ($index = 0; $index -lt $cases.Count; $index++) {
    [PSCustomObject]@{
        GlobalSequence = $index + 1
        Case = $cases[$index]
    }
})

if ($MaxCases -lt $indexedCases.Count) {
    $indexedCases = $indexedCases | Select-Object -First $MaxCases
}

$requestedSlices = @($Slices |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { $_.Trim().ToLowerInvariant() } |
    Select-Object -Unique)

$sliceFilteredCases = @($indexedCases)
if ($requestedSlices.Count -gt 0) {
    $sliceFilteredCases = @(
        $indexedCases | Where-Object {
            $caseSliceIds = @(Get-CaseSliceIds -Case $_.Case)
            @($caseSliceIds | Where-Object { $requestedSlices -contains ([string]$_).ToLowerInvariant() }).Count -gt 0
        }
    )
}

$selectedUniverse = @()
for ($index = 0; $index -lt $sliceFilteredCases.Count; $index++) {
    $selectedUniverse += [PSCustomObject]@{
        Sequence = $index + 1
        GlobalSequence = [int]$sliceFilteredCases[$index].GlobalSequence
        Case = $sliceFilteredCases[$index].Case
    }
}

$selectedCases = $selectedUniverse | Where-Object { $_.Sequence -ge $StartIndex }
if ($BatchSize -gt 0) {
    $selectedCases = $selectedCases | Select-Object -First $BatchSize
}

$existingResultLines = New-Object System.Collections.Generic.List[string]
$completedIds = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
if (Test-Path -LiteralPath $resultsJsonlPath) {
    foreach ($existingLine in (Get-Content -LiteralPath $resultsJsonlPath -Encoding UTF8 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $existingResultLines.Add($existingLine)
        try {
            $existingRecord = $existingLine | ConvertFrom-JsonCompat
            if (-not [string]::IsNullOrWhiteSpace([string]$existingRecord.id)) {
                $null = $completedIds.Add([string]$existingRecord.id)
            }
        }
        catch {
        }
    }
}

$resultLines = New-Object System.Collections.Generic.List[string]
$okCount = 0
$errorCount = 0
$startedAtUtc = [DateTimeOffset]::UtcNow
$attemptedCases = 0
$skippedCompletedCases = 0
$lastProcessedSequence = 0

foreach ($entry in $selectedCases) {
    $case = $entry.Case
    $caseId = [string]$case.id
    $lastProcessedSequence = [int]$entry.Sequence
    if ($completedIds.Contains($caseId)) {
        $skippedCompletedCases++
        Write-Host ("[LocalFirstRun] {0} | {1}/{2} | global={3} | skipped_existing" -f $caseId, $entry.Sequence, $selectedUniverse.Count, $entry.GlobalSequence)
        continue
    }

    $attemptedCases++
    $responsePath = Join-Path $responsesRoot ("{0}.json" -f $caseId)
    $systemInstruction = New-BenchmarkSystemInstruction -Case $case
    $body = [ordered]@{
        message = [string]$case.prompt
        conversationId = $null
        maxHistory = 24
        systemInstruction = $systemInstruction
    }
    $bodyJson = $body | ConvertTo-Json -Depth 12 -Compress
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)
    $requestStartedAtUtc = [DateTimeOffset]::UtcNow

    try {
        $responseDto = Invoke-RestMethod `
            -Method Post `
            -Uri "$ApiBase/api/chat" `
            -Headers $headers `
            -ContentType "application/json; charset=utf-8" `
            -Body $bodyBytes `
            -TimeoutSec $RequestTimeoutSec
        $requestCompletedAtUtc = [DateTimeOffset]::UtcNow
        $sources = Get-SafeArrayValue -InputObject $responseDto -PropertyName "sources"
        $uncertaintyFlags = Get-SafeArrayValue -InputObject $responseDto -PropertyName "uncertaintyFlags"
        $messages = Get-SafeArrayValue -InputObject $responseDto -PropertyName "messages"
        $searchTraceSources = @(Get-SearchTraceSourcesFromResponse -ResponseDto $responseDto)
        $evidenceFusion = Get-SafePropertyValue -InputObject $responseDto -PropertyName "evidenceFusion"
        $webSourcesCount = if ($null -ne $evidenceFusion) {
            Get-SafeIntValue -InputObject $evidenceFusion -PropertyName "webSourceCount"
        }
        else {
            Get-LayeredSourceCount -Sources $searchTraceSources -Layer "web"
        }
        $localSourcesCount = if ($null -ne $evidenceFusion) {
            Get-SafeIntValue -InputObject $evidenceFusion -PropertyName "localSourceCount"
        }
        else {
            Get-LayeredSourceCount -Sources $searchTraceSources -Layer "local_library"
        }
        $attachmentSourcesCount = if ($null -ne $evidenceFusion) {
            Get-SafeIntValue -InputObject $evidenceFusion -PropertyName "attachmentSourceCount"
        }
        else {
            Get-LayeredSourceCount -Sources $searchTraceSources -Layer "attachment"
        }
        $result = [ordered]@{
            id = $caseId
            externalId = [string]$case.externalId
            validationMode = $ValidationMode
            language = [string]$case.language
            domain = [string]$case.domain
            domainTitle = [string]$case.domainTitle
            taskType = [string]$case.taskType
            evidenceMode = [string]$case.evidenceMode
            minWebSources = [int]$case.minWebSources
            prompt = [string]$case.prompt
            localFirst = [string]$case.localFirst
            web = [string]$case.web
            sourcesPolicy = [string]$case.sources
            analysisPolicy = [string]$case.analysis
            conclusionPolicy = [string]$case.conclusion
            opinionPolicy = [string]$case.opinion
            responseSections = @($case.responseSections)
            benchmarkPolicy = [string]$case.benchmarkPolicy
            sliceIds = @(Get-CaseSliceIds -Case $case)
            requestStartedAtUtc = $requestStartedAtUtc.ToString("o")
            requestCompletedAtUtc = $requestCompletedAtUtc.ToString("o")
            durationMs = [int]([TimeSpan]($requestCompletedAtUtc - $requestStartedAtUtc)).TotalMilliseconds
            status = "ok"
            httpStatus = 200
            correlationId = ""
            conversationId = Get-SafeStringValue -InputObject $responseDto -PropertyName "conversationId"
            turnId = Get-SafeStringValue -InputObject $responseDto -PropertyName "turnId"
            response = Get-SafeStringValue -InputObject $responseDto -PropertyName "response"
            responseLength = (Get-SafeStringValue -InputObject $responseDto -PropertyName "response").Length
            messagesCount = @($messages).Count
            confidence = Get-SafeDoubleValue -InputObject $responseDto -PropertyName "confidence"
            sources = $sources
            sourcesCount = @($sources).Count
            webSourcesCount = $webSourcesCount
            localSourcesCount = $localSourcesCount
            attachmentSourcesCount = $attachmentSourcesCount
            groundingStatus = Get-SafeStringValue -InputObject $responseDto -PropertyName "groundingStatus"
            citationCoverage = Get-SafeDoubleValue -InputObject $responseDto -PropertyName "citationCoverage"
            verifiedClaims = Get-SafeIntValue -InputObject $responseDto -PropertyName "verifiedClaims"
            totalClaims = Get-SafeIntValue -InputObject $responseDto -PropertyName "totalClaims"
            epistemicAnswerMode = Get-SafeStringValue -InputObject $responseDto -PropertyName "epistemicAnswerMode"
            epistemicRisk = Get-SafePropertyValue -InputObject $responseDto -PropertyName "epistemicRisk"
            evidenceFusion = $evidenceFusion
            freshClaimWebCoverage = if ($null -ne $evidenceFusion) { Get-SafeDoubleValue -InputObject $evidenceFusion -PropertyName "freshClaimWebCoverage" } else { 0.0 }
            localOnlyFreshClaimCount = if ($null -ne $evidenceFusion) { Get-SafeIntValue -InputObject $evidenceFusion -PropertyName "localOnlyFreshClaimCount" } else { 0 }
            interactionState = Get-SafePropertyValue -InputObject $responseDto -PropertyName "interactionState"
            repairDriver = Get-SafeStringValue -InputObject $responseDto -PropertyName "repairDriver"
            uncertaintyFlags = $uncertaintyFlags
            requiresConfirmation = Get-SafeBoolValue -InputObject $responseDto -PropertyName "requiresConfirmation"
            nextStep = Get-SafeStringValue -InputObject $responseDto -PropertyName "nextStep"
            searchTrace = Get-SafePropertyValue -InputObject $responseDto -PropertyName "searchTrace"
            rawResponse = $responseDto
            requestEnvelope = $body
        }
        $okCount++
    }
    catch {
        $requestCompletedAtUtc = [DateTimeOffset]::UtcNow
        $result = [ordered]@{
            id = $caseId
            externalId = [string]$case.externalId
            validationMode = $ValidationMode
            language = [string]$case.language
            domain = [string]$case.domain
            domainTitle = [string]$case.domainTitle
            taskType = [string]$case.taskType
            evidenceMode = [string]$case.evidenceMode
            minWebSources = [int]$case.minWebSources
            prompt = [string]$case.prompt
            localFirst = [string]$case.localFirst
            web = [string]$case.web
            sourcesPolicy = [string]$case.sources
            analysisPolicy = [string]$case.analysis
            conclusionPolicy = [string]$case.conclusion
            opinionPolicy = [string]$case.opinion
            responseSections = @($case.responseSections)
            benchmarkPolicy = [string]$case.benchmarkPolicy
            sliceIds = @(Get-CaseSliceIds -Case $case)
            requestStartedAtUtc = $requestStartedAtUtc.ToString("o")
            requestCompletedAtUtc = $requestCompletedAtUtc.ToString("o")
            durationMs = [int]([TimeSpan]($requestCompletedAtUtc - $requestStartedAtUtc)).TotalMilliseconds
            status = "error"
            httpStatus = Get-HttpStatusCode -Exception $_.Exception
            correlationId = Get-ExceptionCorrelationId -Exception $_.Exception
            conversationId = ""
            turnId = ""
            response = ""
            responseLength = 0
            messagesCount = 0
            confidence = 0.0
            sources = @()
            sourcesCount = 0
            webSourcesCount = 0
            localSourcesCount = 0
            attachmentSourcesCount = 0
            groundingStatus = ""
            citationCoverage = 0.0
            verifiedClaims = 0
            totalClaims = 0
            epistemicAnswerMode = ""
            epistemicRisk = $null
            evidenceFusion = $null
            freshClaimWebCoverage = 0.0
            localOnlyFreshClaimCount = 0
            interactionState = $null
            repairDriver = ""
            uncertaintyFlags = @()
            requiresConfirmation = $false
            nextStep = ""
            searchTrace = $null
            rawResponse = $null
            requestEnvelope = $body
            errorType = $_.Exception.GetType().FullName
            errorMessage = $_.Exception.Message
        }
        $errorCount++
    }

    $resultJson = Convert-CaseResultToJson -Value $result
    Set-Content -LiteralPath $responsePath -Value $resultJson -Encoding UTF8
    $resultLines.Add($resultJson)
    $null = $completedIds.Add($caseId)
    Write-Host ("[LocalFirstRun] {0} | {1}/{2} | global={3} | {4}" -f $caseId, $entry.Sequence, $selectedUniverse.Count, $entry.GlobalSequence, $result.status)

    if ($PauseMs -gt 0) {
        Start-Sleep -Milliseconds $PauseMs
    }
}

foreach ($line in $resultLines) {
    $existingResultLines.Add($line)
}
Set-Content -LiteralPath $resultsJsonlPath -Value $existingResultLines -Encoding UTF8

$completedAtUtc = [DateTimeOffset]::UtcNow
$nextStartIndex = if ($lastProcessedSequence -gt 0) { $lastProcessedSequence + 1 } else { $StartIndex }
$remainingCases = @($selectedUniverse | Where-Object { $_.Sequence -ge $nextStartIndex }).Count
$selectedSliceCounts = [ordered]@{}
foreach ($sliceId in $requestedSlices) {
    $selectedSliceCounts[$sliceId] = @($selectedUniverse | Where-Object {
            @(Get-CaseSliceIds -Case $_.Case) -contains $sliceId
        }).Count
}
$summary = [ordered]@{
    corpusPath = [System.IO.Path]::GetFullPath($CorpusPath)
    apiBase = $ApiBase
    outputRoot = $OutputRoot
    resultsJsonlPath = $resultsJsonlPath
    validationMode = $ValidationMode
    browserEnabledValidation = [bool]($ValidationMode -eq "browser_enabled")
    validationModeNote = if ($ValidationMode -eq "browser_enabled") { "Browser render recovery should be validated outside sandbox in this mode." } else { "Sandbox-safe mode does not authoritatively validate browser render recovery." }
    totalCases = $indexedCases.Count
    selectedUniverseCases = $selectedUniverse.Count
    requestedSlices = $requestedSlices
    selectedSliceCounts = $selectedSliceCounts
    startIndex = $StartIndex
    batchSize = $BatchSize
    attemptedCases = $attemptedCases
    skippedCompletedCases = $skippedCompletedCases
    okCases = $okCount
    errorCases = $errorCount
    completedUniqueCases = $completedIds.Count
    nextStartIndex = $nextStartIndex
    remainingCases = $remainingCases
    startedAtUtc = $startedAtUtc.ToString("o")
    completedAtUtc = $completedAtUtc.ToString("o")
    durationSec = [math]::Round(([TimeSpan]($completedAtUtc - $startedAtUtc)).TotalSeconds, 2)
}
$summaryJson = $summary | ConvertTo-Json -Depth 8
Set-Content -LiteralPath $summaryPath -Value $summaryJson -Encoding UTF8
Set-Content -LiteralPath $statePath -Value $summaryJson -Encoding UTF8

if ($StopApiWhenFinished.IsPresent) {
    $metadata = $null
    if (Test-Path -LiteralPath $runtimeMetadataPath) {
        try {
            $metadata = Get-Content -LiteralPath $runtimeMetadataPath -Encoding UTF8 | ConvertFrom-JsonCompat
        }
        catch {
            $metadata = $null
        }
    }

    if ($null -ne $metadata -and $metadata.apiPid) {
        try {
            Stop-Process -Id ([int]$metadata.apiPid) -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "[LocalFirstRun] Failed to stop API pid $($metadata.apiPid): $($_.Exception.Message)"
        }
    }
}

Write-Host "[LocalFirstRun] Completed. Results -> $resultsJsonlPath"
