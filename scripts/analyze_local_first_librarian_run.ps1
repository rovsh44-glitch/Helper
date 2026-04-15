param(
    [string]$ResultsJsonlPath = "",
    [string]$SummaryJsonPath = "",
    [string]$SummaryMdPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\LocalFirstBenchmarkSlices.ps1")

function ConvertFrom-JsonCompat {
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [string]$Json
    )

    process {
        return $Json | ConvertFrom-Json
    }
}

function Resolve-LatestResultsPath {
    param(
        [Parameter(Mandatory = $true)][string]$Root
    )

    $candidate = Get-ChildItem -Path $Root -Recurse -Filter "results.jsonl" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $candidate) {
        throw "Could not find results.jsonl under $Root"
    }

    return $candidate.FullName
}

function Rate {
    param(
        [Parameter(Mandatory = $true)][int]$Numerator,
        [Parameter(Mandatory = $true)][int]$Denominator
    )

    if ($Denominator -le 0) {
        return 0.0
    }

    return [double]$Numerator / [double]$Denominator
}

function Test-Heading {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Heading
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    return [regex]::IsMatch($Text, "(?im)^\s*##\s*$([regex]::Escape($Heading))\s*$")
}

function Get-SectionText {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Heading
    )

    $pattern = "(?ims)^\s*##\s*$([regex]::Escape($Heading))\s*$\s*(?<body>.*?)(?=^\s*##\s+\w|\z)"
    $match = [regex]::Match($Text, $pattern)
    if (-not $match.Success) {
        return ""
    }

    return $match.Groups["body"].Value.Trim()
}

function Test-DuplicateBlocks {
    param(
        [Parameter(Mandatory = $true)][string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    $paragraphs = $Text -split "(\r?\n){2,}" |
        ForEach-Object { ($_ -replace "\s+", " ").Trim().ToLowerInvariant() } |
        Where-Object { $_.Length -ge 80 }

    if (@($paragraphs).Count -lt 2) {
        return $false
    }

    return @(@($paragraphs) | Group-Object | Where-Object { $_.Count -gt 1 }).Count -gt 0
}

function Test-ContainsAny {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    foreach ($pattern in $Patterns) {
        if ($Text.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Test-ContainsRegexAny {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    foreach ($pattern in $Patterns) {
        if ([regex]::IsMatch($Text, $pattern)) {
            return $true
        }
    }

    return $false
}

function Test-PublicArtifactExposesLocalPath {
    param(
        [Parameter(Mandatory = $true)]$Result
    )

    $textParts = New-Object System.Collections.Generic.List[string]
    $textParts.Add([string]$Result.response)
    foreach ($source in (Get-StringArray -Value $Result.sources)) {
        $textParts.Add([string]$source)
    }

    foreach ($source in (Get-SearchTraceSources -Result $Result)) {
        if ($source -is [string]) {
            $textParts.Add([string]$source)
        }
        else {
            if (Test-ObjectProperty -Object $source -Name "url") {
                $textParts.Add([string]$source.url)
            }
            if (Test-ObjectProperty -Object $source -Name "displayTitle") {
                $textParts.Add([string]$source.displayTitle)
            }
        }
    }

    $joined = $textParts -join "`n"
    return [regex]::IsMatch($joined, '(?i)\b[A-Z]:\\(?:Users|LIB|GEMINI|Desktop|Documents|Downloads)\\')
}

function Test-MixedSourcesLabeled {
    param(
        [Parameter(Mandatory = $true)][string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    return ($Text.IndexOf("web:", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $Text.IndexOf("Web sources", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -and
           ($Text.IndexOf("local:", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $Text.IndexOf("Local library", [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
}

function Get-StringArray {
    param(
        [Parameter(Mandatory = $false)]$Value
    )

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [string]) {
        return @([string]$Value)
    }

    return @($Value | ForEach-Object {
        if ($null -ne $_) {
            [string]$_
        }
    })
}

function Test-SparseUncertaintySatisfied {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$ResponseText
    )

    $sectionBodies = @(
        Get-SectionText -Text $ResponseText -Heading "Analysis"
        Get-SectionText -Text $ResponseText -Heading "Conclusion"
        Get-SectionText -Text $ResponseText -Heading "Opinion"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $textToCheck = if (@($sectionBodies).Count -gt 0) {
        @($sectionBodies) -join "`n"
    }
    else {
        $ResponseText
    }

    $lexicalPatterns = @(
        '(?i)\buncertain\b',
        '(?i)\blimited evidence\b',
        '(?i)\bunresolved\b',
        '(?i)\bnot (?:verified|confirmed)\b',
        '(?i)\u043d\u0435\u043e\u043f\u0440\u0435\u0434',
        '(?i)\u043e\u0433\u0440\u0430\u043d\u0438\u0447',
        '(?i)\u043d\u0435\u044f\u0441',
        '(?i)\u043d\u0435\u0434\u043e\u0441\u0442',
        '(?i)\u043d\u0435\u043f\u043e\u0434\u0442\u0432\u0435\u0440\u0436',
        '(?i)\u043d\u0435\u043f\u0440\u043e\u0432\u0435\u0440',
        '(?i)\u0432\u044b\u0441\u043e\u043a(?:[^\r\n]{0,30})\u043d\u0435\u043e\u043f\u0440'
    )

    if (Test-ContainsRegexAny -Text $textToCheck -Patterns $lexicalPatterns) {
        return $true
    }

    $uncertaintyFlags = Get-StringArray -Value $Result.uncertaintyFlags
    if (@($uncertaintyFlags | Where-Object {
                $_ -match '^(uncertainty\.|missing_sources_for_factual_claims$|factual_without_sources$)'
            }).Count -gt 0) {
        $groundingStatus = [string]$Result.groundingStatus
        return [string]::Equals($groundingStatus, "unverified", [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($groundingStatus, "grounded_with_contradictions", [System.StringComparison]::OrdinalIgnoreCase)
    }

    return $false
}

function Test-ObjectProperty {
    param(
        [Parameter(Mandatory = $false)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $false
    }

    return $Object.PSObject.Properties.Match($Name).Count -gt 0
}

function Get-SearchTraceSources {
    param(
        [Parameter(Mandatory = $true)]$Result
    )

    if (-not (Test-ObjectProperty -Object $Result -Name "searchTrace") -or
        $null -eq $Result.searchTrace -or
        -not (Test-ObjectProperty -Object $Result.searchTrace -Name "sources") -or
        $null -eq $Result.searchTrace.sources) {
        return @()
    }

    return @($Result.searchTrace.sources)
}

function Get-SearchTraceSourceLayer {
    param(
        [Parameter(Mandatory = $false)]$Source
    )

    if ($null -eq $Source) {
        return ""
    }

    if ($Source -isnot [string] -and
        (Test-ObjectProperty -Object $Source -Name "sourceLayer") -and
        -not [string]::IsNullOrWhiteSpace([string]$Source.sourceLayer)) {
        return ([string]$Source.sourceLayer).Trim().ToLowerInvariant()
    }

    $url = if ($Source -is [string]) {
        [string]$Source
    }
    elseif (Test-ObjectProperty -Object $Source -Name "url") {
        [string]$Source.url
    }
    else {
        ""
    }

    if ($url.StartsWith("http://", [System.StringComparison]::OrdinalIgnoreCase) -or
        $url.StartsWith("https://", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "web"
    }

    if (-not [string]::IsNullOrWhiteSpace($url)) {
        return "local_library"
    }

    return ""
}

function Get-LayeredSourceCount {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$Layer
    )

    $propertyName = switch ($Layer) {
        "web" { "webSourcesCount" }
        "local_library" { "localSourcesCount" }
        "attachment" { "attachmentSourcesCount" }
        default { "" }
    }

    if (-not [string]::IsNullOrWhiteSpace($propertyName) -and
        (Test-ObjectProperty -Object $Result -Name $propertyName)) {
        return [int]$Result.$propertyName
    }

    return @((Get-SearchTraceSources -Result $Result) | Where-Object {
            [string]::Equals((Get-SearchTraceSourceLayer -Source $_), $Layer, [System.StringComparison]::OrdinalIgnoreCase)
        }).Count
}

function Get-EpistemicAnswerMode {
    param(
        [Parameter(Mandatory = $true)]$Result
    )

    $mode = ""
    if (Test-ObjectProperty -Object $Result -Name "epistemicAnswerMode") {
        $mode = [string]$Result.epistemicAnswerMode
    }

    if ([string]::IsNullOrWhiteSpace($mode) -and
        (Test-ObjectProperty -Object $Result -Name "epistemicRisk") -and
        $null -ne $Result.epistemicRisk -and
        (Test-ObjectProperty -Object $Result.epistemicRisk -Name "answerMode")) {
        $mode = [string]$Result.epistemicRisk.answerMode
    }

    return $mode.Trim().ToLowerInvariant()
}

function Get-InteractionStateValue {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    if (-not (Test-ObjectProperty -Object $Result -Name "interactionState") -or
        $null -eq $Result.interactionState -or
        -not (Test-ObjectProperty -Object $Result.interactionState -Name $PropertyName)) {
        return ""
    }

    return [string]$Result.interactionState.$PropertyName
}

function Test-InteractionLevelAtLeastModerate {
    param(
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$Value = ""
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    return [string]::Equals($Value, "moderate", [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($Value, "high", [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-ReassuranceLanguagePresent {
    param(
        [Parameter(Mandatory = $true)][string]$Text
    )

    $patterns = @(
        '(?i)\bwe can\b',
        '(?i)\bit''s okay\b',
        '(?i)\byou''re not stuck\b',
        '(?i)\bthat''s manageable\b',
        '(?i)\b\u043f\u043e\u043d\u0438\u043c\u0430\u044e\b',
        '(?i)\b\u0441\u043f\u043e\u043a\u043e\u0439\u043d\u043e\b',
        '(?i)\b\u044d\u0442\u043e \u043f\u043e\u043f\u0440\u0430\u0432\u0438\u043c\u043e\b',
        '(?i)\b\u043d\u0435 \u043f\u0435\u0440\u0435\u0436\u0438\u0432\u0430\u0439\b',
        '(?i)\b\u043d\u0435 \u0441\u0442\u0440\u0430\u0448\u043d\u043e\b'
    )

    return Test-ContainsRegexAny -Text $Text -Patterns $patterns
}

function Get-SearchTraceEvents {
    param(
        [Parameter(Mandatory = $true)]$Result
    )

    if (-not (Test-ObjectProperty -Object $Result -Name "searchTrace") -or
        $null -eq $Result.searchTrace -or
        -not (Test-ObjectProperty -Object $Result.searchTrace -Name "events") -or
        $null -eq $Result.searchTrace.events) {
        return @()
    }

    return Get-StringArray -Value $Result.searchTrace.events
}

function Get-SearchTraceEventValue {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$Prefix
    )

    foreach ($event in (Get-SearchTraceEvents -Result $Result)) {
        if ($event.StartsWith("$Prefix=", [System.StringComparison]::OrdinalIgnoreCase)) {
            return $event.Substring($Prefix.Length + 1)
        }
    }

    return $null
}

function Get-SearchTraceEventIntValue {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$Prefix,
        [int]$Default = 0
    )

    $rawValue = Get-SearchTraceEventValue -Result $Result -Prefix $Prefix
    if ([string]::IsNullOrWhiteSpace($rawValue)) {
        return $Default
    }

    $value = 0
    if ([int]::TryParse($rawValue, [ref]$value)) {
        return $value
    }

    return $Default
}

function Get-NormalizedHost {
    param(
        [Parameter(Mandatory = $true)][string]$Url
    )

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return ""
    }

    $uri = $null
    if ([System.Uri]::TryCreate($Url, [System.UriKind]::Absolute, [ref]$uri)) {
        return $uri.Host.ToLowerInvariant()
    }

    return ""
}

function Get-ContradictionClaimCount {
    param(
        [Parameter(Mandatory = $true)]$Result
    )

    if (-not (Test-ObjectProperty -Object $Result -Name "claimGroundings") -or $null -eq $Result.claimGroundings) {
        return 0
    }

    return @($Result.claimGroundings | Where-Object { $_.contradictionDetected -eq $true }).Count
}

function Test-GroundedWithoutPassageEvidence {
    param(
        [Parameter(Mandatory = $true)]$Result
    )

    $groundingStatus = [string]$Result.groundingStatus
    if (@("grounded", "grounded_with_contradictions") -notcontains $groundingStatus) {
        return $false
    }

    $sources = @(Get-SearchTraceSources -Result $Result)
    if ($sources.Count -eq 0) {
        return $false
    }

    $webSources = @($sources | Where-Object {
            [string]::Equals((Get-SearchTraceSourceLayer -Source $_), "web", [System.StringComparison]::OrdinalIgnoreCase)
        })
    if ($webSources.Count -eq 0) {
        return $false
    }

    $hasPassages = @($webSources | Where-Object { [int]$_.passageCount -gt 0 }).Count -gt 0
    $extractedCount = Get-SearchTraceEventIntValue -Result $Result -Prefix "web_page_fetch.extracted_count"

    return (-not $hasPassages) -and ($extractedCount -le 0)
}

function Test-FetchFailureDespiteRelevantSearchHits {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][bool]$MandatoryWeb
    )

    if (-not $MandatoryWeb) {
        return $false
    }

    $searchTraceStatus = if ((Test-ObjectProperty -Object $Result -Name "searchTrace") -and
        $null -ne $Result.searchTrace -and
        (Test-ObjectProperty -Object $Result.searchTrace -Name "status") -and
        $null -ne $Result.searchTrace.status) {
        [string]$Result.searchTrace.status
    }
    else {
        ""
    }

    if ([string]::IsNullOrWhiteSpace($searchTraceStatus) -or
        ($searchTraceStatus.IndexOf("live", [System.StringComparison]::OrdinalIgnoreCase) -lt 0)) {
        return $false
    }

    $candidateCount = Get-LayeredSourceCount -Result $Result -Layer "web"
    if ($candidateCount -lt [math]::Max(1, [int]$Result.minWebSources)) {
        return $false
    }

    if (-not (Test-GroundedWithoutPassageEvidence -Result $Result)) {
        return $false
    }

    $events = @(Get-SearchTraceEvents -Result $Result)
    return @($events | Where-Object {
            $_ -like "web_page_fetch.error*" -or
            $_ -like "web_page_fetch.transport_retry_failed*" -or
            $_ -like "web_page_fetch.transport_failed*" -or
            $_ -like "web_page_fetch.transport_retry*"
        }).Count -gt 0
}

function Test-FalseContradictionWithoutContradictingClaims {
    param(
        [Parameter(Mandatory = $true)]$Result
    )

    if (-not [string]::Equals([string]$Result.groundingStatus, "grounded_with_contradictions", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    if ((Get-ContradictionClaimCount -Result $Result) -gt 0) {
        return $false
    }

    return Test-GroundedWithoutPassageEvidence -Result $Result
}

function Test-LooksLikeRenderableEvidenceSource {
    param(
        [Parameter(Mandatory = $true)]$Source
    )

    $url = ""
    $title = ""
    if ($null -ne $Source) {
        if ($Source -is [string]) {
            $url = [string]$Source
        }
        else {
            if (Test-ObjectProperty -Object $Source -Name "url") {
                $url = [string]$Source.url
            }
            if (Test-ObjectProperty -Object $Source -Name "title") {
                $title = [string]$Source.title
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($url)) {
        return $false
    }

    $sourceHost = Get-NormalizedHost -Url $url
    $renderableHosts = @(
        "ncbi.nlm.nih.gov",
        "pubmed.ncbi.nlm.nih.gov",
        "springer.com",
        "sciencedirect.com",
        "mayoclinic.org",
        "health.harvard.edu",
        "who.int",
        "cdc.gov",
        "nih.gov",
        "cochrane.org",
        "medelement.com"
    )
    foreach ($renderableHost in $renderableHosts) {
        if ($sourceHost.Equals($renderableHost, [System.StringComparison]::OrdinalIgnoreCase) -or
            $sourceHost.EndsWith(".$renderableHost", [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    $descriptor = "$title`n$url"
    $patterns = @(
        '(?i)/article/',
        '(?i)/articles/',
        '(?i)/abstract/',
        '(?i)/guideline',
        '(?i)/recommendation',
        '(?i)/healthy-aging',
        '(?i)/post/',
        '(?i)/study/',
        '(?i)\bPMC\d+\b',
        '(?i)\bsystematic review\b',
        '(?i)\bmeta-analysis\b'
    )
    return (Test-ContainsRegexAny -Text $descriptor -Patterns $patterns)
}

function Test-BrowserOrFetchRecoveryUnresolved {
    param(
        [Parameter(Mandatory = $true)]$Result
    )

    $transportFailureCount = Get-SearchTraceEventIntValue -Result $Result -Prefix "web_page_fetch.transport_failure_count"
    $extractedCount = Get-SearchTraceEventIntValue -Result $Result -Prefix "web_page_fetch.extracted_count"
    if ($transportFailureCount -le 0 -or $extractedCount -gt 0) {
        return $false
    }

    $sources = @(Get-SearchTraceSources -Result $Result)
    $hasPassageBackedSource = @($sources | Where-Object {
            ($null -ne $_.passageCount -and [int]$_.passageCount -gt 0) -or
            [string]::Equals([string]$_.evidenceKind, "fetched_page", [System.StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0
    if ($hasPassageBackedSource) {
        return $false
    }

    $events = @(Get-SearchTraceEvents -Result $Result)
    $hasBrowserAttempt = @($events | Where-Object {
            $_ -like "web_page_fetch.render_recovery*" -or
            $_ -like "browser_render.*"
        }).Count -gt 0

    $hasRenderableSources = @($sources | Where-Object {
            Test-LooksLikeRenderableEvidenceSource -Source $_
        }).Count -gt 0

    if (-not $hasRenderableSources) {
        $rawSources = Get-StringArray -Value $Result.sources
        $hasRenderableSources = @($rawSources | Where-Object {
                Test-LooksLikeRenderableEvidenceSource -Source $_
            }).Count -gt 0
    }

    if (-not $hasRenderableSources) {
        return $false
    }

    if (Test-AcceptedSparseSearchHitOnlyRecovery -Result $Result) {
        return $false
    }

    return (
        $hasBrowserAttempt -or
        [string]::Equals([string]$Result.groundingStatus, "grounded_with_limits", [System.StringComparison]::OrdinalIgnoreCase) -or
        (Test-GroundedWithoutPassageEvidence -Result $Result)
    )
}

function Test-AcceptedSparseSearchHitOnlyRecovery {
    param(
        [Parameter(Mandatory = $true)]$Result
    )

    if (-not [string]::Equals([string]$Result.evidenceMode, "uncertain_sparse", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $mode = Get-EpistemicAnswerMode -Result $Result
    if (@("needs_verification", "abstain") -notcontains $mode) {
        return $false
    }

    $groundingStatus = [string]$Result.groundingStatus
    if (-not [string]::Equals($groundingStatus, "grounded_with_limits", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $uncertaintyFlags = Get-StringArray -Value $Result.uncertaintyFlags
    if (-not ($uncertaintyFlags -contains "uncertainty.search_hit_only_evidence")) {
        return $false
    }

    $sourceCount = Get-LayeredSourceCount -Result $Result -Layer "web"
    $minimumSources = [math]::Max(1, [int]$Result.minWebSources)
    return $sourceCount -ge $minimumSources -and [double]$Result.citationCoverage -ge 0.70
}

function Get-MedicalAuthorityMix {
    param(
        [Parameter(Mandatory = $true)]$Result
    )

    $strongHosts = @(
        "who.int",
        "cdc.gov",
        "nih.gov",
        "ncbi.nlm.nih.gov",
        "pubmed.ncbi.nlm.nih.gov",
        "cochrane.org",
        "nice.org.uk",
        "medelement.com",
        "headache.ru",
        "painrussia.ru",
        "emcmos.ru",
        "unicef.org"
    )
    $weakHosts = @(
        "doctor.rambler.ru",
        "glamour.ru",
        "fitstars.ru",
        "fitness-pro.ru",
        "atlas-zdorovya.ru"
    )
    $strongTextPatterns = @(
        '(?i)\bwho\b',
        '(?i)\bcdc\b',
        '(?i)\bnih\b',
        '(?i)\bcochrane\b',
        '(?i)\bpubmed\b',
        '(?i)\bguideline\b',
        '(?i)\bmeta-analys(?:is|es)\b',
        '(?i)\bsystematic review\b',
        '(?i)\bfact sheet\b'
    )
    $weakTextPatterns = @(
        '(?i)\bglamour\b',
        '(?i)\brambler\b',
        '(?i)\bfitstars\b',
        '(?i)\bfitness[- ]?pro\b',
        '(?i)\bbestseller'
    )

    $strongCount = 0
    $weakCount = 0
    foreach ($source in (Get-SearchTraceSources -Result $Result)) {
        $url = [string]$source.url
        $title = [string]$source.title
        $sourceHost = Get-NormalizedHost -Url $url
        $descriptor = "$title`n$url"

        $isStrong = $false
        $isWeak = $false
        foreach ($pattern in $strongHosts) {
            if ($sourceHost.Equals($pattern, [System.StringComparison]::OrdinalIgnoreCase) -or
                $sourceHost.EndsWith(".$pattern", [System.StringComparison]::OrdinalIgnoreCase)) {
                $isStrong = $true
                break
            }
        }
        if (-not $isStrong -and (Test-ContainsRegexAny -Text $descriptor -Patterns $strongTextPatterns)) {
            $isStrong = $true
        }

        foreach ($pattern in $weakHosts) {
            if ($sourceHost.Equals($pattern, [System.StringComparison]::OrdinalIgnoreCase) -or
                $sourceHost.EndsWith(".$pattern", [System.StringComparison]::OrdinalIgnoreCase)) {
                $isWeak = $true
                break
            }
        }
        if (-not $isWeak -and (Test-ContainsRegexAny -Text $descriptor -Patterns $weakTextPatterns)) {
            $isWeak = $true
        }

        if ($isStrong) {
            $strongCount++
        }
        if ($isWeak) {
            $weakCount++
        }
    }

    return [ordered]@{
        strong = $strongCount
        weak = $weakCount
    }
}

function Get-IssueList {
    param(
        [Parameter(Mandatory = $true)]$Result
    )

    $issues = New-Object System.Collections.Generic.List[string]
    if ([string]$Result.status -ne "ok") {
        $issues.Add("runtime_error")
        return $issues
    }

    $responseText = [string]$Result.response
    $mandatoryWeb = ([string]$Result.evidenceMode) -in @("local_plus_web", "web_required_fresh", "conflict_check")
    $sparseEvidence = [string]$Result.evidenceMode -eq "uncertain_sparse"
    $sourcesCount = [int]$Result.sourcesCount
    $webSourcesCount = Get-LayeredSourceCount -Result $Result -Layer "web"
    $localSourcesCount = Get-LayeredSourceCount -Result $Result -Layer "local_library"
    $minWebSources = [int]$Result.minWebSources
    $groundingStatus = [string]$Result.groundingStatus
    $citationCoverage = [double]$Result.citationCoverage
    $epistemicMode = Get-EpistemicAnswerMode -Result $Result
    $searchTrace = if (Test-ObjectProperty -Object $Result -Name "searchTrace") {
        $Result.searchTrace
    }
    else {
        $null
    }
    $searchTraceStatus = ""
    if ($null -ne $searchTrace -and $null -ne $searchTrace.status) {
        $searchTraceStatus = [string]$searchTrace.status
    }
    $acceptedMandatoryWebAbstainWithoutSources =
        $mandatoryWeb -and
        [string]::Equals($epistemicMode, "abstain", [System.StringComparison]::OrdinalIgnoreCase) -and
        $webSourcesCount -eq 0 -and
        [string]::Equals($groundingStatus, "unverified", [System.StringComparison]::OrdinalIgnoreCase) -and
        $searchTraceStatus.IndexOf("live", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    $acceptedMandatoryWebUnderSourcedAbstain =
        $mandatoryWeb -and
        [string]::Equals($epistemicMode, "abstain", [System.StringComparison]::OrdinalIgnoreCase) -and
        $webSourcesCount -lt $minWebSources -and
        (@("unverified", "grounded_with_limits") -contains $groundingStatus) -and
        $searchTraceStatus.IndexOf("live", [System.StringComparison]::OrdinalIgnoreCase) -ge 0

    foreach ($heading in @("Local Findings", "Web Findings", "Sources", "Analysis", "Conclusion", "Opinion")) {
        if (-not (Test-Heading -Text $responseText -Heading $heading)) {
            $issues.Add("missing_heading_$((ConvertTo-LowerHeadingId -Heading $heading))")
        }
    }

    if ($mandatoryWeb -and -not $acceptedMandatoryWebAbstainWithoutSources -and -not $acceptedMandatoryWebUnderSourcedAbstain -and $webSourcesCount -lt $minWebSources) {
        $issues.Add("web_sources_below_minimum")
    }

    if ($mandatoryWeb -and -not $acceptedMandatoryWebUnderSourcedAbstain -and $sourcesCount -ge $minWebSources -and $webSourcesCount -lt $minWebSources -and $localSourcesCount -gt 0) {
        $issues.Add("local_sources_miscounted_as_web")
    }

    if ($mandatoryWeb -and [string]::IsNullOrWhiteSpace($searchTraceStatus)) {
        $issues.Add("missing_search_trace_for_web_case")
    }

    if ($mandatoryWeb -and -not $acceptedMandatoryWebAbstainWithoutSources -and -not $acceptedMandatoryWebUnderSourcedAbstain -and $webSourcesCount -eq 0) {
        $issues.Add("no_sources_in_mandatory_web_case")
    }

    if ($mandatoryWeb -and -not $acceptedMandatoryWebAbstainWithoutSources -and -not $acceptedMandatoryWebUnderSourcedAbstain -and $citationCoverage -lt 0.5) {
        $issues.Add("low_citation_coverage_for_web_case")
    }

    if ([string]::Equals($groundingStatus, "clarification_required", [System.StringComparison]::OrdinalIgnoreCase) -or
        [bool]$Result.requiresConfirmation) {
        $issues.Add("clarification_instead_of_answer")
    }

    if ([string]::IsNullOrWhiteSpace($groundingStatus) -or
        [string]::Equals($groundingStatus, "unknown", [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($groundingStatus, "degraded", [System.StringComparison]::OrdinalIgnoreCase)) {
        $issues.Add("weak_grounding_status")
    }

    if ([int]$Result.responseLength -lt 450) {
        $issues.Add("response_too_short")
    }

    if ((Test-ObjectProperty -Object $Result -Name "localOnlyFreshClaimCount") -and
        [int]$Result.localOnlyFreshClaimCount -gt 0) {
        $issues.Add("fresh_claim_supported_only_by_local_library")
    }

    if (Test-PublicArtifactExposesLocalPath -Result $Result) {
        $issues.Add("public_artifact_exposes_local_path")
    }

    if ($webSourcesCount -gt 0 -and $localSourcesCount -gt 0 -and -not (Test-MixedSourcesLabeled -Text $responseText)) {
        $issues.Add("mixed_sources_not_labeled")
    }

    if (Test-DuplicateBlocks -Text $responseText) {
        $issues.Add("duplicated_response_blocks")
    }

    if (Test-ContainsAny -Text $responseText -Patterns @(
        "unexpected token",
        "!doctype",
        "github advanced security",
        "enterprise platform",
        "ai-powered developer platform",
        "syntaxerror"
    )) {
        $issues.Add("tooling_or_site_chrome_noise")
    }

    if ($sparseEvidence -and -not (Test-SparseUncertaintySatisfied -Result $Result -ResponseText $responseText)) {
        $issues.Add("missing_explicit_uncertainty_for_sparse_case")
    }

    if (Test-GroundedWithoutPassageEvidence -Result $Result) {
        $issues.Add("grounded_without_passage_evidence")
    }

    if (Test-FetchFailureDespiteRelevantSearchHits -Result $Result -MandatoryWeb $mandatoryWeb) {
        $issues.Add("fetch_failure_despite_relevant_search_hits")
    }

    if (Test-FalseContradictionWithoutContradictingClaims -Result $Result) {
        $issues.Add("false_contradiction_without_contradicting_claims")
    }

    if (Test-BrowserOrFetchRecoveryUnresolved -Result $Result) {
        $issues.Add("browser_or_fetch_recovery_unresolved")
    }

    if ([string]::Equals([string]$Result.domain, "health_and_medicine", [System.StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals([string]$Result.evidenceMode, "conflict_check", [System.StringComparison]::OrdinalIgnoreCase)) {
        $authorityMix = Get-MedicalAuthorityMix -Result $Result
        if ($authorityMix.weak -ge 2 -and $authorityMix.strong -lt 2) {
            $issues.Add("weak_medical_source_mix_for_conflict_case")
        }
    }

    return $issues
}

function ConvertTo-LowerHeadingId {
    param(
        [Parameter(Mandatory = $true)][string]$Heading
    )

    return (($Heading.ToLowerInvariant() -replace "[^a-z0-9]+", "_").Trim("_"))
}

function Add-GroupSummary {
    param(
        [Parameter(Mandatory = $true)]$Accumulator,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)]$Result,
        [string[]]$Issues = @()
    )

    if (-not $Accumulator.ContainsKey($Key)) {
        $Accumulator[$Key] = [ordered]@{
            total = 0
            ok = 0
            errors = 0
            avgSources = 0.0
            avgWebSources = 0.0
            avgLocalSources = 0.0
            avgCitationCoverage = 0.0
            casesWithIssues = 0
        }
    }

    $entry = $Accumulator[$Key]
    $entry.total++
    if ([string]$Result.status -eq "ok") {
        $entry.ok++
    }
    else {
        $entry.errors++
    }

    $entry.avgSources += [double]$Result.sourcesCount
    $entry.avgWebSources += [double](Get-LayeredSourceCount -Result $Result -Layer "web")
    $entry.avgLocalSources += [double](Get-LayeredSourceCount -Result $Result -Layer "local_library")
    $entry.avgCitationCoverage += [double]$Result.citationCoverage
    if (@($Issues).Count -gt 0) {
        $entry.casesWithIssues++
    }
}

if ([string]::IsNullOrWhiteSpace($ResultsJsonlPath)) {
    $ResultsJsonlPath = Resolve-LatestResultsPath -Root "artifacts/eval/local_first_librarian_300"
}

if (-not (Test-Path -LiteralPath $ResultsJsonlPath)) {
    throw "Results file not found: $ResultsJsonlPath"
}

$runRoot = Split-Path -Path $ResultsJsonlPath -Parent
if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $runRoot "reports\analysis_summary.json"
}
if ([string]::IsNullOrWhiteSpace($SummaryMdPath)) {
    $SummaryMdPath = Join-Path $runRoot "reports\analysis_summary.md"
}

$summaryDir = Split-Path -Path $SummaryJsonPath -Parent
if (-not [string]::IsNullOrWhiteSpace($summaryDir)) {
    New-Item -ItemType Directory -Force -Path $summaryDir | Out-Null
}

$lines = Get-Content -LiteralPath $ResultsJsonlPath -Encoding UTF8 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$results = @()
foreach ($line in $lines) {
    $results += ($line | ConvertFrom-JsonCompat)
}

if ($results.Count -eq 0) {
    throw "Results are empty: $ResultsJsonlPath"
}

$issueCounts = @{}
$issueSamples = @{}
$byEvidenceMode = @{}
$byTaskType = @{}
$byDomain = @{}
$bySlice = @{}
$perCase = New-Object System.Collections.Generic.List[object]

foreach ($result in $results) {
    $issues = @((Get-IssueList -Result $result))
    foreach ($issue in $issues) {
        if (-not $issueCounts.ContainsKey($issue)) {
            $issueCounts[$issue] = 0
            $issueSamples[$issue] = New-Object System.Collections.Generic.List[string]
        }

        $issueCounts[$issue]++
        if ($issueSamples[$issue].Count -lt 5) {
            $issueSamples[$issue].Add([string]$result.id)
        }
    }

    Add-GroupSummary -Accumulator $byEvidenceMode -Key ([string]$result.evidenceMode) -Result $result -Issues $issues
    Add-GroupSummary -Accumulator $byTaskType -Key ([string]$result.taskType) -Result $result -Issues $issues
    Add-GroupSummary -Accumulator $byDomain -Key ([string]$result.domain) -Result $result -Issues $issues
    $sliceIds = @(Get-LocalFirstSliceArrayValue -InputObject $result -PropertyName "sliceIds")
    if ($sliceIds.Count -eq 0) {
        $sliceIds = @(Get-LocalFirstBenchmarkSliceIds -Case $result)
    }
    foreach ($sliceId in $sliceIds) {
        Add-GroupSummary -Accumulator $bySlice -Key ([string]$sliceId) -Result $result -Issues $issues
    }

    $perCase.Add([ordered]@{
        id = [string]$result.id
        domain = [string]$result.domain
        taskType = [string]$result.taskType
        evidenceMode = [string]$result.evidenceMode
        sliceIds = [string[]]$sliceIds
        status = [string]$result.status
        sourcesCount = [int]$result.sourcesCount
        webSourcesCount = Get-LayeredSourceCount -Result $result -Layer "web"
        localSourcesCount = Get-LayeredSourceCount -Result $result -Layer "local_library"
        attachmentSourcesCount = Get-LayeredSourceCount -Result $result -Layer "attachment"
        freshClaimWebCoverage = if (Test-ObjectProperty -Object $result -Name "freshClaimWebCoverage") { [double]$result.freshClaimWebCoverage } else { 0.0 }
        localOnlyFreshClaimCount = if (Test-ObjectProperty -Object $result -Name "localOnlyFreshClaimCount") { [int]$result.localOnlyFreshClaimCount } else { 0 }
        citationCoverage = [double]$result.citationCoverage
        groundingStatus = [string]$result.groundingStatus
        epistemicAnswerMode = Get-EpistemicAnswerMode -Result $result
        repairDriver = if (Test-ObjectProperty -Object $result -Name "repairDriver") { [string]$result.repairDriver } else { "" }
        issues = [string[]]$issues
    })
}

foreach ($group in @($byEvidenceMode.Keys)) {
    $entry = $byEvidenceMode[$group]
    if ($entry.total -gt 0) {
        $entry.avgSources = [math]::Round($entry.avgSources / $entry.total, 2)
        $entry.avgWebSources = [math]::Round($entry.avgWebSources / $entry.total, 2)
        $entry.avgLocalSources = [math]::Round($entry.avgLocalSources / $entry.total, 2)
        $entry.avgCitationCoverage = [math]::Round($entry.avgCitationCoverage / $entry.total, 3)
    }
}

foreach ($group in @($byTaskType.Keys)) {
    $entry = $byTaskType[$group]
    if ($entry.total -gt 0) {
        $entry.avgSources = [math]::Round($entry.avgSources / $entry.total, 2)
        $entry.avgWebSources = [math]::Round($entry.avgWebSources / $entry.total, 2)
        $entry.avgLocalSources = [math]::Round($entry.avgLocalSources / $entry.total, 2)
        $entry.avgCitationCoverage = [math]::Round($entry.avgCitationCoverage / $entry.total, 3)
    }
}

foreach ($group in @($byDomain.Keys)) {
    $entry = $byDomain[$group]
    if ($entry.total -gt 0) {
        $entry.avgSources = [math]::Round($entry.avgSources / $entry.total, 2)
        $entry.avgWebSources = [math]::Round($entry.avgWebSources / $entry.total, 2)
        $entry.avgLocalSources = [math]::Round($entry.avgLocalSources / $entry.total, 2)
        $entry.avgCitationCoverage = [math]::Round($entry.avgCitationCoverage / $entry.total, 3)
    }
}

foreach ($slice in (Get-LocalFirstBenchmarkSliceCatalog)) {
    $sliceId = [string]$slice.id
    if (-not $bySlice.ContainsKey($sliceId)) {
        $bySlice[$sliceId] = [ordered]@{
            total = 0
            ok = 0
            errors = 0
            avgSources = 0.0
            avgWebSources = 0.0
            avgLocalSources = 0.0
            avgCitationCoverage = 0.0
            casesWithIssues = 0
        }
    }
}

foreach ($group in @($bySlice.Keys)) {
    $entry = $bySlice[$group]
    if ($entry.total -gt 0) {
        $entry.avgSources = [math]::Round($entry.avgSources / $entry.total, 2)
        $entry.avgWebSources = [math]::Round($entry.avgWebSources / $entry.total, 2)
        $entry.avgLocalSources = [math]::Round($entry.avgLocalSources / $entry.total, 2)
        $entry.avgCitationCoverage = [math]::Round($entry.avgCitationCoverage / $entry.total, 3)
    }
}

$sortedIssues = $issueCounts.GetEnumerator() |
    Sort-Object -Property @{ Expression = "Value"; Descending = $true }, @{ Expression = "Name"; Descending = $false } |
    ForEach-Object {
        [ordered]@{
            issue = $_.Key
            count = [int]$_.Value
            sampleIds = [string[]]$issueSamples[$_.Key]
        }
    }

$worstCases = $perCase |
    Sort-Object -Property @{ Expression = { @($_.issues).Count }; Descending = $true }, @{ Expression = "id"; Descending = $false } |
    Select-Object -First 20

$unsupportedAssertionCases = 0
$abstainCases = 0
$abstainAppropriateCases = 0
$abstainPotentialOveruseCases = 0
$bestEffortHypothesisCases = 0
$bestEffortPotentialOveruseCases = 0
$interactionPressureCases = 0
$reassuranceUnderuseCases = 0
$reassuranceOveruseCases = 0
$repairDriverCounts = [ordered]@{
    interaction = 0
    epistemic = 0
    structural = 0
}

foreach ($case in $perCase) {
    $mode = [string]$case.epistemicAnswerMode
    $issueList = @($case.issues)
    $strongAssertionMode = @("direct", "grounded") -contains $mode
    $weakEvidenceIssue = @($issueList | Where-Object {
            $_ -in @(
                "no_sources_in_mandatory_web_case",
                "low_citation_coverage_for_web_case",
                "grounded_without_passage_evidence",
                "weak_grounding_status",
                "fetch_failure_despite_relevant_search_hits"
            )
        }).Count -gt 0
    if ($strongAssertionMode -and $weakEvidenceIssue) {
        $unsupportedAssertionCases++
    }

    if ($mode -eq "abstain") {
        $abstainCases++
        $caseRequiresWeb = ([string]$case.evidenceMode) -in @("local_plus_web", "web_required_fresh", "conflict_check")
        $caseSourceCountForCalibration = if ($caseRequiresWeb) { [int]$case.webSourcesCount } else { [int]$case.sourcesCount }
        if (
            $case.groundingStatus -in @("unverified", "grounded_with_contradictions", "grounded_with_limits") -or
            $caseSourceCountForCalibration -eq 0 -or
            [double]$case.citationCoverage -lt 0.5
        ) {
            $abstainAppropriateCases++
        }
        elseif ($caseSourceCountForCalibration -gt 0 -and [double]$case.citationCoverage -ge 0.5) {
            $abstainPotentialOveruseCases++
        }
    }

    if ($mode -eq "best_effort_hypothesis") {
        $bestEffortHypothesisCases++
        $caseRequiresWeb = ([string]$case.evidenceMode) -in @("local_plus_web", "web_required_fresh", "conflict_check")
        $caseSourceCountForCalibration = if ($caseRequiresWeb) { [int]$case.webSourcesCount } else { [int]$case.sourcesCount }
        if ($caseSourceCountForCalibration -gt 0 -and [double]$case.citationCoverage -ge 0.5) {
            $bestEffortPotentialOveruseCases++
        }
    }

    $interactionPressure = Get-InteractionStateValue -Result ($results | Where-Object { [string]$_.id -eq [string]$case.id } | Select-Object -First 1) -PropertyName "assistantPressureRisk"
    if (Test-InteractionLevelAtLeastModerate -Value $interactionPressure) {
        $interactionPressureCases++
    }

    $reassuranceNeed = Get-InteractionStateValue -Result ($results | Where-Object { [string]$_.id -eq [string]$case.id } | Select-Object -First 1) -PropertyName "reassuranceNeed"
    $fullResult = $results | Where-Object { [string]$_.id -eq [string]$case.id } | Select-Object -First 1
    $responseText = if ($null -ne $fullResult) { [string]$fullResult.response } else { "" }
    $hasReassuranceLanguage = Test-ReassuranceLanguagePresent -Text $responseText
    if (Test-InteractionLevelAtLeastModerate -Value $reassuranceNeed) {
        if (-not $hasReassuranceLanguage) {
            $reassuranceUnderuseCases++
        }
    }
    elseif ($hasReassuranceLanguage) {
        $reassuranceOveruseCases++
    }

    $repairDriver = [string]$case.repairDriver
    if ($repairDriverCounts.Contains($repairDriver)) {
        $repairDriverCounts[$repairDriver]++
    }
}

$epistemicAndInteractionMetrics = [ordered]@{
    unsupportedAssertionCases = $unsupportedAssertionCases
    unsupportedAssertionRate = [math]::Round((Rate $unsupportedAssertionCases $results.Count), 3)
    abstainCases = $abstainCases
    abstainAppropriateCases = $abstainAppropriateCases
    abstainPotentialOveruseCases = $abstainPotentialOveruseCases
    bestEffortHypothesisCases = $bestEffortHypothesisCases
    bestEffortPotentialOveruseCases = $bestEffortPotentialOveruseCases
    clarificationInsteadOfAnswerCases = if ($issueCounts.ContainsKey("clarification_instead_of_answer")) { [int]$issueCounts["clarification_instead_of_answer"] } else { 0 }
    interactionPressureCases = $interactionPressureCases
    reassuranceUnderuseCases = $reassuranceUnderuseCases
    reassuranceOveruseCases = $reassuranceOveruseCases
    repairDriverCounts = $repairDriverCounts
}

$validationModes = @(
    $results |
        ForEach-Object {
            $validationMode = ""
            $validationProperty = if ($null -ne $_) { $_.PSObject.Properties["validationMode"] } else { $null }
            if ($null -ne $validationProperty -and $null -ne $validationProperty.Value) {
                $validationMode = [string]$validationProperty.Value
            }
            if (-not [string]::IsNullOrWhiteSpace($validationMode)) {
                $validationMode
            }
        } |
        Select-Object -Unique
)

$summary = [ordered]@{
    resultsPath = [System.IO.Path]::GetFullPath($ResultsJsonlPath)
    validationModes = [string[]]$validationModes
    totalCases = $results.Count
    okCases = @($results | Where-Object { $_.status -eq "ok" }).Count
    errorCases = @($results | Where-Object { $_.status -ne "ok" }).Count
    epistemicAndInteractionMetrics = $epistemicAndInteractionMetrics
    issueCounts = $sortedIssues
    byEvidenceMode = $byEvidenceMode
    byTaskType = $byTaskType
    byDomain = $byDomain
    bySlice = $bySlice
    worstCases = $worstCases
}

$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $SummaryJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add("# Local-First Librarian 300 Analysis")
$mdLines.Add("")
$mdLines.Add([string]::Format('- Results: `{0}`', [System.IO.Path]::GetFullPath($ResultsJsonlPath)))
if (@($validationModes).Count -gt 0) {
    $mdLines.Add([string]::Format('- Validation modes: `{0}`', ($validationModes -join '`, `')))
}
$mdLines.Add([string]::Format('- Total cases: `{0}`', $summary.totalCases))
$mdLines.Add([string]::Format('- OK cases: `{0}`', $summary.okCases))
$mdLines.Add([string]::Format('- Error cases: `{0}`', $summary.errorCases))
$mdLines.Add("")
$mdLines.Add("## Epistemic And Interaction Metrics")
$mdLines.Add("")
$mdLines.Add([string]::Format('- Unsupported assertion rate: `{0}` ({1}/{2})', $summary.epistemicAndInteractionMetrics.unsupportedAssertionRate, $summary.epistemicAndInteractionMetrics.unsupportedAssertionCases, $summary.totalCases))
$mdLines.Add([string]::Format('- Abstain cases: `{0}` | appropriate: `{1}` | potential overuse: `{2}`', $summary.epistemicAndInteractionMetrics.abstainCases, $summary.epistemicAndInteractionMetrics.abstainAppropriateCases, $summary.epistemicAndInteractionMetrics.abstainPotentialOveruseCases))
$mdLines.Add([string]::Format('- Best-effort hypothesis cases: `{0}` | potential overuse: `{1}`', $summary.epistemicAndInteractionMetrics.bestEffortHypothesisCases, $summary.epistemicAndInteractionMetrics.bestEffortPotentialOveruseCases))
$mdLines.Add([string]::Format('- Clarification instead of answer cases: `{0}`', $summary.epistemicAndInteractionMetrics.clarificationInsteadOfAnswerCases))
$mdLines.Add([string]::Format('- Interaction pressure cases: `{0}`', $summary.epistemicAndInteractionMetrics.interactionPressureCases))
$mdLines.Add([string]::Format('- Reassurance underuse cases: `{0}` | overuse cases: `{1}`', $summary.epistemicAndInteractionMetrics.reassuranceUnderuseCases, $summary.epistemicAndInteractionMetrics.reassuranceOveruseCases))
$mdLines.Add([string]::Format('- Repair driver counts: `interaction={0}`, `epistemic={1}`, `structural={2}`', $summary.epistemicAndInteractionMetrics.repairDriverCounts.interaction, $summary.epistemicAndInteractionMetrics.repairDriverCounts.epistemic, $summary.epistemicAndInteractionMetrics.repairDriverCounts.structural))
$mdLines.Add("")
$mdLines.Add("## Top Issues")
$mdLines.Add("")
$mdLines.Add("| Issue | Count | Sample IDs |")
$mdLines.Add("| --- | ---: | --- |")
foreach ($issue in $sortedIssues) {
    $mdLines.Add(('| `{0}` | {1} | `{2}` |' -f $issue.issue, $issue.count, ($issue.sampleIds -join '`, `')))
}
$mdLines.Add("")
$mdLines.Add("## By Evidence Mode")
$mdLines.Add("")
$mdLines.Add("| Evidence Mode | Total | Errors | Avg Sources | Avg Web | Avg Local | Avg Citation Coverage | Cases With Issues |")
$mdLines.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
foreach ($key in ($byEvidenceMode.Keys | Sort-Object)) {
    $entry = $byEvidenceMode[$key]
    $mdLines.Add(('| `{0}` | {1} | {2} | {3} | {4} | {5} | {6} | {7} |' -f $key, $entry.total, $entry.errors, $entry.avgSources, $entry.avgWebSources, $entry.avgLocalSources, $entry.avgCitationCoverage, $entry.casesWithIssues))
}
$mdLines.Add("")
$mdLines.Add("## By Task Type")
$mdLines.Add("")
$mdLines.Add("| Task Type | Total | Errors | Avg Sources | Avg Web | Avg Local | Avg Citation Coverage | Cases With Issues |")
$mdLines.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
foreach ($key in ($byTaskType.Keys | Sort-Object)) {
    $entry = $byTaskType[$key]
    $mdLines.Add(('| `{0}` | {1} | {2} | {3} | {4} | {5} | {6} | {7} |' -f $key, $entry.total, $entry.errors, $entry.avgSources, $entry.avgWebSources, $entry.avgLocalSources, $entry.avgCitationCoverage, $entry.casesWithIssues))
}
$mdLines.Add("")
$mdLines.Add("## By Domain")
$mdLines.Add("")
$mdLines.Add("| Domain | Total | Errors | Avg Sources | Avg Web | Avg Local | Avg Citation Coverage | Cases With Issues |")
$mdLines.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
foreach ($key in ($byDomain.Keys | Sort-Object)) {
    $entry = $byDomain[$key]
    $mdLines.Add(('| `{0}` | {1} | {2} | {3} | {4} | {5} | {6} | {7} |' -f $key, $entry.total, $entry.errors, $entry.avgSources, $entry.avgWebSources, $entry.avgLocalSources, $entry.avgCitationCoverage, $entry.casesWithIssues))
}
$mdLines.Add("")
$mdLines.Add("## By Slice")
$mdLines.Add("")
$mdLines.Add("| Slice | Total | Errors | Avg Sources | Avg Web | Avg Local | Avg Citation Coverage | Cases With Issues |")
$mdLines.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
foreach ($key in ($bySlice.Keys | Sort-Object)) {
    $entry = $bySlice[$key]
    $mdLines.Add(('| `{0}` | {1} | {2} | {3} | {4} | {5} | {6} | {7} |' -f $key, $entry.total, $entry.errors, $entry.avgSources, $entry.avgWebSources, $entry.avgLocalSources, $entry.avgCitationCoverage, $entry.casesWithIssues))
}
$mdLines.Add("")
$mdLines.Add("## Worst Cases")
$mdLines.Add("")
foreach ($case in $worstCases) {
    $mdLines.Add([string]::Format('- `{0}` | `{1}` | `{2}` | slices: `{3}` | issues: `{4}`', $case.id, $case.evidenceMode, $case.taskType, ($case.sliceIds -join '`, `'), ($case.issues -join '`, `')))
}

Set-Content -LiteralPath $SummaryMdPath -Value ($mdLines -join "`r`n") -Encoding UTF8

Write-Host "[LocalFirstAnalysis] Summary -> $SummaryMdPath"
