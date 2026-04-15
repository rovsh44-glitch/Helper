param(
    [string]$ResultsJsonlPath = "",
    [string]$OutputJsonPath = "",
    [string]$OutputMdPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-LatestResultsPath {
    param([Parameter(Mandatory = $true)][string]$Root)

    $candidate = Get-ChildItem -Path $Root -Recurse -Filter "results.jsonl" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "Could not find results.jsonl under $Root"
    }

    return $candidate.FullName
}

function Test-Property {
    param(
        [Parameter(Mandatory = $false)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    return $null -ne $Object -and $Object.PSObject.Properties.Match($Name).Count -gt 0
}

function Get-ArrayProperty {
    param(
        [Parameter(Mandatory = $false)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not (Test-Property -Object $Object -Name $Name) -or $null -eq $Object.$Name) {
        return @()
    }

    return @($Object.$Name | Where-Object { $null -ne $_ })
}

function Get-SourceLayer {
    param([Parameter(Mandatory = $false)]$Source)

    if ($null -eq $Source) {
        return ""
    }

    if ($Source -isnot [string] -and
        (Test-Property -Object $Source -Name "sourceLayer") -and
        -not [string]::IsNullOrWhiteSpace([string]$Source.sourceLayer)) {
        return ([string]$Source.sourceLayer).Trim().ToLowerInvariant()
    }

    $url = if ($Source -is [string]) {
        [string]$Source
    }
    elseif (Test-Property -Object $Source -Name "url") {
        [string]$Source.url
    }
    else {
        ""
    }

    if ($url.StartsWith("http://", [StringComparison]::OrdinalIgnoreCase) -or
        $url.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)) {
        return "web"
    }

    if ([string]::IsNullOrWhiteSpace($url)) {
        return ""
    }

    return "local_library"
}

function Test-LocalPathLeak {
    param([Parameter(Mandatory = $true)][string]$Text)

    $legacyRoot = 'GE' + 'MINI'
    return [regex]::IsMatch($Text, "(?i)\b[A-Z]:\\(?:Users|LIB|$legacyRoot|Desktop|Documents|Downloads)\\")
}

if ([string]::IsNullOrWhiteSpace($ResultsJsonlPath)) {
    $ResultsJsonlPath = Resolve-LatestResultsPath -Root "artifacts/eval"
}
if (-not (Test-Path -LiteralPath $ResultsJsonlPath)) {
    throw "Results file not found: $ResultsJsonlPath"
}

$runRoot = Split-Path -Path $ResultsJsonlPath -Parent
if ([string]::IsNullOrWhiteSpace($OutputJsonPath)) {
    $OutputJsonPath = Join-Path $runRoot "reports\evidence_fusion_audit.json"
}
if ([string]::IsNullOrWhiteSpace($OutputMdPath)) {
    $OutputMdPath = Join-Path $runRoot "reports\evidence_fusion_audit.md"
}

$reportDir = Split-Path -Path $OutputJsonPath -Parent
if (-not [string]::IsNullOrWhiteSpace($reportDir)) {
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
}

$cases = Get-Content -LiteralPath $ResultsJsonlPath -Encoding UTF8 |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { $_ | ConvertFrom-Json }

$summary = [ordered]@{
    resultsPath = [IO.Path]::GetFullPath($ResultsJsonlPath)
    totalCases = 0
    webSources = 0
    localSources = 0
    attachmentSources = 0
    localSourcesMissingFormat = 0
    localSourcesMissingStableId = 0
    localSourcesMissingDisplayTitle = 0
    localSourcesMissingLocator = 0
    publicPathLeakCases = New-Object System.Collections.Generic.List[string]
    formatCounts = [ordered]@{}
    layerCounts = [ordered]@{}
}

foreach ($case in $cases) {
    $summary.totalCases++
    $sources = if ((Test-Property -Object $case -Name "searchTrace") -and $null -ne $case.searchTrace) {
        Get-ArrayProperty -Object $case.searchTrace -Name "sources"
    }
    else {
        @()
    }

    $caseText = New-Object System.Collections.Generic.List[string]
    if (Test-Property -Object $case -Name "response") {
        $caseText.Add([string]$case.response)
    }
    foreach ($rawSource in (Get-ArrayProperty -Object $case -Name "sources")) {
        $caseText.Add([string]$rawSource)
    }

    foreach ($source in $sources) {
        $layer = Get-SourceLayer -Source $source
        if ([string]::IsNullOrWhiteSpace($layer)) {
            $layer = "unknown"
        }

        if (-not $summary.layerCounts.Contains($layer)) {
            $summary.layerCounts[$layer] = 0
        }
        $summary.layerCounts[$layer]++

        switch ($layer) {
            "web" { $summary.webSources++ }
            "local_library" { $summary.localSources++ }
            "attachment" { $summary.attachmentSources++ }
        }

        if ($source -isnot [string]) {
            foreach ($field in @("url", "displayTitle")) {
                if (Test-Property -Object $source -Name $field) {
                    $caseText.Add([string]$source.$field)
                }
            }
        }

        if ($layer -eq "local_library" -and $source -isnot [string]) {
            $format = if (Test-Property -Object $source -Name "sourceFormat") { [string]$source.sourceFormat } else { "" }
            if ([string]::IsNullOrWhiteSpace($format)) {
                $summary.localSourcesMissingFormat++
            }
            else {
                $key = $format.Trim().ToLowerInvariant()
                if (-not $summary.formatCounts.Contains($key)) {
                    $summary.formatCounts[$key] = 0
                }
                $summary.formatCounts[$key]++
            }

            if (-not (Test-Property -Object $source -Name "sourceId") -or [string]::IsNullOrWhiteSpace([string]$source.sourceId)) {
                $summary.localSourcesMissingStableId++
            }
            if (-not (Test-Property -Object $source -Name "displayTitle") -or [string]::IsNullOrWhiteSpace([string]$source.displayTitle)) {
                $summary.localSourcesMissingDisplayTitle++
            }
            if (-not (Test-Property -Object $source -Name "locator") -or [string]::IsNullOrWhiteSpace([string]$source.locator)) {
                $summary.localSourcesMissingLocator++
            }
        }
    }

    if (Test-LocalPathLeak -Text ($caseText -join "`n")) {
        $summary.publicPathLeakCases.Add([string]$case.id)
    }
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8

$md = New-Object System.Collections.Generic.List[string]
$md.Add("# Local Library Evidence Fusion Audit")
$md.Add("")
$md.Add("- Results: ``$([IO.Path]::GetFullPath($ResultsJsonlPath))``")
$md.Add("- Cases: ``$($summary.totalCases)``")
$md.Add("- Web sources: ``$($summary.webSources)``")
$md.Add("- Local library sources: ``$($summary.localSources)``")
$md.Add("- Attachment sources: ``$($summary.attachmentSources)``")
$md.Add("- Local missing format: ``$($summary.localSourcesMissingFormat)``")
$md.Add("- Local missing stable ID: ``$($summary.localSourcesMissingStableId)``")
$md.Add("- Local missing display title: ``$($summary.localSourcesMissingDisplayTitle)``")
$md.Add("- Local missing locator: ``$($summary.localSourcesMissingLocator)``")
$md.Add("- Public path leak cases: ``$($summary.publicPathLeakCases.Count)``")
$md.Add("")
$md.Add("## Format Counts")
$md.Add("")
foreach ($key in ($summary.formatCounts.Keys | Sort-Object)) {
    $md.Add("- ``$key``: ``$($summary.formatCounts[$key])``")
}
$md.Add("")
$md.Add("## Layer Counts")
$md.Add("")
foreach ($key in ($summary.layerCounts.Keys | Sort-Object)) {
    $md.Add("- ``$key``: ``$($summary.layerCounts[$key])``")
}
if ($summary.publicPathLeakCases.Count -gt 0) {
    $md.Add("")
    $md.Add("## Path Leak Cases")
    $md.Add("")
    foreach ($caseId in $summary.publicPathLeakCases) {
        $md.Add("- ``$caseId``")
    }
}

$md | Set-Content -LiteralPath $OutputMdPath -Encoding UTF8

Write-Host "Evidence fusion audit written:"
Write-Host "  JSON: $OutputJsonPath"
Write-Host "  MD:   $OutputMdPath"
