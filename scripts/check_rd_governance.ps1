param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
else {
    (Resolve-Path $RepoRoot).Path
}

function Join-RepoPath {
    param([string]$RelativePath)
    return Join-Path $repoRoot $RelativePath
}

function Normalize-DocLinkTarget {
    param(
        [Parameter(Mandatory = $true)][string]$DocumentRelativePath,
        [Parameter(Mandatory = $true)][string]$Target
    )

    $trimmed = $Target.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $null
    }

    if ($trimmed -match '^(#|mailto:|https?:)') {
        return $null
    }

    $trimmed = $trimmed.Split('#')[0].Split('?')[0]
    $trimmed = $trimmed.Replace('\', '/')

    $repoRootNormalized = $repoRoot.Replace('\', '/').TrimEnd('/')
    if ($trimmed.StartsWith($repoRootNormalized + '/', [System.StringComparison]::OrdinalIgnoreCase)) {
        $trimmed = $trimmed.Substring($repoRootNormalized.Length + 1)
    }

    if ([System.IO.Path]::IsPathRooted($trimmed)) {
        return $trimmed.Replace('\', '/')
    }

    $documentDirectory = Split-Path $DocumentRelativePath -Parent
    if ([string]::IsNullOrWhiteSpace($documentDirectory)) {
        $documentDirectory = "."
    }

    $candidate = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $repoRoot $documentDirectory) $trimmed))
    $normalizedCandidate = $candidate.Replace('\', '/')
    if ($normalizedCandidate.StartsWith($repoRootNormalized + '/', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $normalizedCandidate.Substring($repoRootNormalized.Length + 1)
    }

    return $normalizedCandidate
}

function Get-NormalizedDocumentReferences {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $fullPath = Join-RepoPath $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        return [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    }

    $content = Get-Content -Path $fullPath -Raw
    $targets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    $markdownLinks = [System.Text.RegularExpressions.Regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')
    foreach ($match in $markdownLinks) {
        $normalized = Normalize-DocLinkTarget -DocumentRelativePath $RelativePath -Target $match.Groups[1].Value
        if (-not [string]::IsNullOrWhiteSpace($normalized)) {
            [void]$targets.Add($normalized)
        }
    }

    $inlineCodeSpans = [System.Text.RegularExpressions.Regex]::Matches($content, '`([^`]+)`')
    foreach ($match in $inlineCodeSpans) {
        $candidate = $match.Groups[1].Value
        if ($candidate -notmatch '[/\\]' -and $candidate -notmatch '\.md$') {
            continue
        }

        $normalized = Normalize-DocLinkTarget -DocumentRelativePath $RelativePath -Target $candidate
        if (-not [string]::IsNullOrWhiteSpace($normalized)) {
            [void]$targets.Add($normalized)
        }
    }

    return $targets
}

function Read-JsonFile {
    param([string]$RelativePath)

    $fullPath = Join-RepoPath $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "[R&D Governance] Missing required JSON file: $RelativePath"
    }

    return Get-Content -Path $fullPath -Raw | ConvertFrom-Json
}

$failures = New-Object System.Collections.Generic.List[string]

$requiredFiles = @(
    "doc/research/README.md",
    "doc/research/MODEL_EXPERIMENT_TRACK_POLICY.md",
    "doc/research/MODEL_EXPERIMENT_TRACK_REGISTRY.json",
    "doc/research/active/CURRENT_MODEL_EXPERIMENT_TRACK.json",
    "doc/research/active/CURRENT_MODEL_EXPERIMENT_TRACK.md",
    "doc/research/notes/README.md",
    "doc/archive/comparative/HELPER_EXECUTION_ORDER_TABLE_LFL300_2026-03-22.md",
    "doc/archive/comparative/HELPER_EXECUTION_DASHBOARD_LFL300_2026-03-22.md",
    "scripts/run_eval_runner_v2.ps1"
)

foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-RepoPath $relativePath))) {
        $failures.Add("Missing required R&D governance artifact: $relativePath")
    }
}

$registry = $null
$activeState = $null
try {
    $registry = Read-JsonFile "doc/research/MODEL_EXPERIMENT_TRACK_REGISTRY.json"
    $activeState = Read-JsonFile "doc/research/active/CURRENT_MODEL_EXPERIMENT_TRACK.json"
}
catch {
    $failures.Add($_.Exception.Message)
}

if ($null -ne $registry) {
    if ($registry.trackId -ne "model_experiment_track") {
        $failures.Add("Registry trackId must be 'model_experiment_track'.")
    }

    if ($registry.status -ne "TRACK_DEFINED_RESEARCH_ONLY") {
        $failures.Add("Registry status must be TRACK_DEFINED_RESEARCH_ONLY.")
    }

    if ($registry.productExecutionTrack.scope -ne "product_quality_closure_only") {
        $failures.Add("Product execution track scope must be product_quality_closure_only.")
    }

    $requiredThemes = @(
        "selective_residual_memory",
        "evidence_aware_decoding",
        "retrieval_conditioned_latent_routing"
    )

    $actualThemes = @($registry.modelExperimentTrack.allowedThemes | ForEach-Object { $_.id })
    foreach ($theme in $requiredThemes) {
        if ($actualThemes -notcontains $theme) {
            $failures.Add("Registry missing allowed theme: $theme")
        }
    }

    if (-not $registry.modelExperimentTrack.nonAuthoritativeByDefault) {
        $failures.Add("Model experiment track must be non-authoritative by default.")
    }

    if (-not $registry.modelExperimentTrack.promotionRule.requiresSeparateRfc) {
        $failures.Add("Promotion rule must require a separate RFC.")
    }

    if (-not $registry.modelExperimentTrack.promotionRule.requiresReproductionOnProductTrack) {
        $failures.Add("Promotion rule must require reproduction on the product track.")
    }
}

if ($null -ne $activeState) {
    if ($activeState.status -ne "TRACK_DEFINED_NO_ACTIVE_EXPERIMENTS") {
        $failures.Add("Active state status must be TRACK_DEFINED_NO_ACTIVE_EXPERIMENTS.")
    }

    if (-not $activeState.productionRoadmapSeparated) {
        $failures.Add("Active state must assert productionRoadmapSeparated=true.")
    }

    if ($activeState.productRunnerScope -ne "product_quality_closure_only") {
        $failures.Add("Active state productRunnerScope must be product_quality_closure_only.")
    }
}

$docIndexPath = Join-RepoPath "doc/README.md"
if (Test-Path -LiteralPath $docIndexPath) {
    $docIndexLinks = Get-NormalizedDocumentReferences -RelativePath "doc/README.md"
    if (-not $docIndexLinks.Contains("doc/research/README.md")) {
        $failures.Add("doc/README.md must reference doc/research/README.md")
    }
}

$rootReadmePath = Join-RepoPath "README.md"
if (Test-Path -LiteralPath $rootReadmePath) {
    $rootReadmeLinks = Get-NormalizedDocumentReferences -RelativePath "README.md"
    if (-not $rootReadmeLinks.Contains("doc/research/README.md")) {
        $failures.Add("README.md must reference doc/research/README.md")
    }
}

$runnerPath = Join-RepoPath "scripts/run_eval_runner_v2.ps1"
if (Test-Path -LiteralPath $runnerPath) {
    $runnerText = Get-Content -Path $runnerPath -Raw
    if (-not $runnerText.Contains("Scope=product_quality_closure_only")) {
        $failures.Add("scripts/run_eval_runner_v2.ps1 must declare Scope=product_quality_closure_only.")
    }
}

$orderTablePath = Join-RepoPath "doc/archive/comparative/HELPER_EXECUTION_ORDER_TABLE_LFL300_2026-03-22.md"
if (Test-Path -LiteralPath $orderTablePath) {
    $orderText = Get-Content -Path $orderTablePath -Raw
    if (-not $orderText.Contains("STEP-001..STEP-016")) {
        $failures.Add("Execution order table must state STEP-001..STEP-016 implemented.")
    }
}

$dashboardPath = Join-RepoPath "doc/archive/comparative/HELPER_EXECUTION_DASHBOARD_LFL300_2026-03-22.md"
if (Test-Path -LiteralPath $dashboardPath) {
    $dashboardText = Get-Content -Path $dashboardPath -Raw
    if ($dashboardText -notmatch '\|\s+`Wave 5`\s+\|\s+`STEP-016`\s+R&D Governance\s+\|\s+`completed`\s+\|') {
        $failures.Add("Execution dashboard must mark STEP-016 as completed.")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "[R&D Governance] Check failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "[R&D Governance] Model experiment track is governed and separated from product execution." -ForegroundColor Green
