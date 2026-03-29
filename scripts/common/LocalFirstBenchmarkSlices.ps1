Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-LocalFirstSlicePropertyValue {
    param(
        [Parameter(Mandatory = $false)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($PropertyName)) {
            return $InputObject[$PropertyName]
        }

        return $null
    }

    $property = $InputObject.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-LocalFirstSliceStringValue {
    param(
        [Parameter(Mandatory = $false)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $value = Get-LocalFirstSlicePropertyValue -InputObject $InputObject -PropertyName $PropertyName
    if ($null -eq $value) {
        return ""
    }

    return [string]$value
}

function Get-LocalFirstSliceArrayValue {
    param(
        [Parameter(Mandatory = $false)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $value = Get-LocalFirstSlicePropertyValue -InputObject $InputObject -PropertyName $PropertyName
    if ($null -eq $value) {
        return @()
    }

    return @($value | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ })
}

function Test-LocalFirstSliceRegexAny {
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

function Get-LocalFirstBenchmarkSliceCatalog {
    return @(
        [ordered]@{
            id = "medical_conflict"
            title = "Medical Conflict"
            description = "Conflict-heavy health-and-medicine cases where source reconciliation quality is the main risk."
        }
        [ordered]@{
            id = "regulation_freshness"
            title = "Regulation Freshness"
            description = "Current rules, thresholds, filing, visa, customs, policy, and compliance cases where freshness is mandatory."
        }
        [ordered]@{
            id = "paper_analysis"
            title = "Paper Analysis"
            description = "Research-paper, article-status, literature-review, and evidence-evaluation cases."
        }
        [ordered]@{
            id = "multilingual_local_first"
            title = "Multilingual Local-First"
            description = "Non-English local-first benchmark cases; current frozen corpus is Russian-first."
        }
        [ordered]@{
            id = "sparse_evidence"
            title = "Sparse Evidence"
            description = "Cases where uncertainty handling and evidence limits matter more than decisive resolution."
        }
        [ordered]@{
            id = "local_only_strength"
            title = "Local-Only Strength"
            description = "Local-sufficient cases used to measure whether the local library can answer strongly before web escalation."
        }
    )
}

function Get-LocalFirstBenchmarkSliceIds {
    param(
        [Parameter(Mandatory = $true)]$Case
    )

    $domain = Get-LocalFirstSliceStringValue -InputObject $Case -PropertyName "domain"
    $taskType = Get-LocalFirstSliceStringValue -InputObject $Case -PropertyName "taskType"
    $evidenceMode = Get-LocalFirstSliceStringValue -InputObject $Case -PropertyName "evidenceMode"
    $language = Get-LocalFirstSliceStringValue -InputObject $Case -PropertyName "language"
    $prompt = Get-LocalFirstSliceStringValue -InputObject $Case -PropertyName "prompt"
    $labels = @(Get-LocalFirstSliceArrayValue -InputObject $Case -PropertyName "labels")

    $sliceIds = New-Object System.Collections.Generic.List[string]
    $promptSignals = "$prompt`n$($labels -join ' ')"

    if ([string]::Equals($domain, "health_and_medicine", [System.StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals($evidenceMode, "conflict_check", [System.StringComparison]::OrdinalIgnoreCase)) {
        $sliceIds.Add("medical_conflict")
    }

    if ([string]::Equals($evidenceMode, "uncertain_sparse", [System.StringComparison]::OrdinalIgnoreCase)) {
        $sliceIds.Add("sparse_evidence")
    }

    if ([string]::Equals($evidenceMode, "local_sufficient", [System.StringComparison]::OrdinalIgnoreCase)) {
        $sliceIds.Add("local_only_strength")
    }

    if (-not [string]::IsNullOrWhiteSpace($language) -and
        -not [string]::Equals($language, "en", [System.StringComparison]::OrdinalIgnoreCase)) {
        $sliceIds.Add("multilingual_local_first")
    }

    $paperPatterns = @(
        '(?i)\barxiv\b',
        '(?i)\bpreprint\b',
        '(?i)\bpeer review\b',
        '(?i)\bliterature review\b',
        '(?i)\bsystematic review\b',
        '(?i)\bmeta-analysis\b',
        '(?i)\bjournal\b',
        '(?i)\bpaper\b',
        '(?i)\bopen-access publication\b',
        '(?i)\bpeer-reviewed\b',
        '(?i)\bpeer reviewed\b',
        '(?i)\bconference\b',
        '(?i)\barticle\b',
        '(?i)\bretracted\b',
        '(?i)\bpeer-review\b'
    )
    if ([string]::Equals($domain, "science_and_research", [System.StringComparison]::OrdinalIgnoreCase) -or
        (Test-LocalFirstSliceRegexAny -Text $promptSignals -Patterns $paperPatterns) -or
        ([string]::Equals($taskType, "review_diagnose_or_critique", [System.StringComparison]::OrdinalIgnoreCase) -and
            (Test-LocalFirstSliceRegexAny -Text $promptSignals -Patterns @(
                    '(?i)\bpaper\b',
                    '(?i)\bjournal\b',
                    '(?i)\bpreprint\b',
                    '(?i)\bretracted\b'
                )))) {
        $sliceIds.Add("paper_analysis")
    }

    $regulationPatterns = @(
        '(?i)\bregulat',
        '(?i)\bcompliance\b',
        '(?i)\bprivacy\b',
        '(?i)\bvisa\b',
        '(?i)\bcustoms\b',
        '(?i)\bimport restrictions?\b',
        '(?i)\bentry-rule',
        '(?i)\bthresholds?\b',
        '(?i)\breporting deadlines?\b',
        '(?i)\bfiling\b',
        '(?i)\bpermit',
        '(?i)\blicen',
        '(?i)\bsubsid',
        '(?i)\btariff',
        '(?i)\bnet-metering\b',
        '(?i)\btax(?:es|ation)?\b',
        '(?i)\bprivacy notice\b'
    )
    if ([string]::Equals($evidenceMode, "web_required_fresh", [System.StringComparison]::OrdinalIgnoreCase) -and
        (([string]::Equals($domain, "law_civic_and_compliance", [System.StringComparison]::OrdinalIgnoreCase)) -or
            (Test-LocalFirstSliceRegexAny -Text $promptSignals -Patterns $regulationPatterns))) {
        $sliceIds.Add("regulation_freshness")
    }

    return [string[]]@($sliceIds | Select-Object -Unique)
}
