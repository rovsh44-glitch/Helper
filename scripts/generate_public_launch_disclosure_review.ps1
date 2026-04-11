param(
    [string]$OutputDirectory = "",
    [switch]$FailOnUnsafeTrackedSet
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\PublicLaunchBoundaryCommon.ps1")

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "temp\public_launch_review"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$internalReportDirectory = Join-Path $repoRoot "temp\public_launch_review_internal"
New-Item -ItemType Directory -Path $internalReportDirectory -Force | Out-Null

$secretReportPath = Join-Path $internalReportDirectory "secret_scan_report.json"
$rootLayoutReportPath = Join-Path $internalReportDirectory "root_layout_report.json"
$jsonReportPath = Join-Path $OutputDirectory "public_launch_disclosure_review.json"
$markdownReportPath = Join-Path $OutputDirectory "PUBLIC_LAUNCH_DISCLOSURE_REVIEW.md"

$policy = Get-PublicShowcaseBoundaryPolicy

$candidateFiles = @(
    & git -C $repoRoot -c core.quotePath=false ls-files --cached --others --exclude-standard
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique

$classified = foreach ($path in $candidateFiles) {
    $classification = Get-PublicShowcasePathClassification -RelativePath $path
    [PSCustomObject]@{
        path = ($path -replace '\\', '/')
        topLevel = Get-RepoRelativeTopLevel -RelativePath $path
        category = $classification.category
        reason = $classification.reason
        rule = $classification.rule
    }
}

$publicSafe = @($classified | Where-Object { $_.category -eq "public_safe" } | Sort-Object path)
$privateOnly = @($classified | Where-Object { $_.category -eq "private_only" } | Sort-Object path)
$reviewRequired = @($classified | Where-Object { $_.category -eq "review_required" } | Sort-Object path)

$secretExitCode = 0
$rootLayoutExitCode = 0

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "secret_scan.ps1") -ScanMode repo -ReportPath $secretReportPath
$secretExitCode = $LASTEXITCODE

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check_root_layout.ps1") -ReportPath $rootLayoutReportPath
$rootLayoutExitCode = $LASTEXITCODE

$secretReport = Get-Content -Path $secretReportPath -Raw | ConvertFrom-Json
$rootLayoutReport = Get-Content -Path $rootLayoutReportPath -Raw | ConvertFrom-Json

function Get-RepoRelativePath {
    param([string]$Path)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $normalizedRepoRoot = $repoRoot.TrimEnd('\', '/')
    if ($resolvedPath.StartsWith($normalizedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $repoRootWithSeparator = $normalizedRepoRoot
        if (-not $repoRootWithSeparator.EndsWith('\')) {
            $repoRootWithSeparator += '\'
        }

        $repoUri = [System.Uri]$repoRootWithSeparator
        $pathUri = [System.Uri]$resolvedPath
        return [System.Uri]::UnescapeDataString($repoUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
    }

    return $resolvedPath.Replace('\', '/')
}

$groupSummary = foreach ($group in ($classified | Group-Object topLevel | Sort-Object Count -Descending)) {
    [PSCustomObject]@{
        topLevel = $group.Name
        total = $group.Count
        publicSafe = @($group.Group | Where-Object { $_.category -eq "public_safe" }).Count
        privateOnly = @($group.Group | Where-Object { $_.category -eq "private_only" }).Count
        reviewRequired = @($group.Group | Where-Object { $_.category -eq "review_required" }).Count
    }
}

$directPublishAllowed = ($privateOnly.Count -eq 0 -and $reviewRequired.Count -eq 0 -and $secretExitCode -eq 0 -and $rootLayoutExitCode -eq 0)
$verdict = if ($directPublishAllowed) { "ALLOW_DIRECT_PUBLISH" } else { "BLOCK_DIRECT_PUBLISH" }
$notes = [System.Collections.Generic.List[string]]::new()

if ($privateOnly.Count -gt 0) {
    $notes.Add("Candidate files contain private-core or operator-only surfaces that are outside the public showcase allowlist.")
}
if ($reviewRequired.Count -gt 0) {
    $notes.Add("Candidate files remain under default-deny review and are not approved for public publication.")
}
if ($secretExitCode -ne 0) {
    $notes.Add("Secret scan failed.")
}
if ($rootLayoutExitCode -ne 0) {
    $notes.Add("Root layout hygiene failed.")
}
if ($notes.Count -eq 0) {
    $notes.Add("Tracked set matches the public showcase allowlist and hygiene gates passed.")
}

$report = [PSCustomObject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    policyVersion = $policy.policyVersion
    publicationModel = $policy.publicationModel
    verdict = $verdict
    directPublishAllowed = $directPublishAllowed
    candidateFileCount = $candidateFiles.Count
    publicSafeCount = $publicSafe.Count
    privateOnlyCount = $privateOnly.Count
    reviewRequiredCount = $reviewRequired.Count
    secretScan = [PSCustomObject]@{
        exitCode = $secretExitCode
        hitCount = @($secretReport.hits).Count
        reportPath = Get-RepoRelativePath -Path $secretReportPath
    }
    rootLayout = [PSCustomObject]@{
        exitCode = $rootLayoutExitCode
        rootViolationCount = @($rootLayoutReport.rootViolations).Count
        sourceFatalViolationCount = @($rootLayoutReport.sourceFatalViolations).Count
        sourceWarningCount = @($rootLayoutReport.sourceWarnings).Count
        reportPath = Get-RepoRelativePath -Path $rootLayoutReportPath
    }
    notes = @($notes)
    samplePublicSafePaths = @($publicSafe | Select-Object -First 20 -ExpandProperty path)
    samplePrivateOnlyPaths = @($privateOnly | Select-Object -First 40 -ExpandProperty path)
    sampleReviewRequiredPaths = @($reviewRequired | Select-Object -First 40 -ExpandProperty path)
    groupedByTopLevel = @($groupSummary)
}

$report | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonReportPath -Encoding UTF8
$directPublishAllowedText = if ($report.directPublishAllowed) { "YES" } else { "NO" }

$markdown = [System.Collections.Generic.List[string]]::new()
$markdown.Add("# Public Launch Disclosure Review")
$markdown.Add("")
$markdown.Add(("Generated: {0}" -f $report.generatedAtUtc))
$markdown.Add(("Policy version: {0}" -f $report.policyVersion))
$markdown.Add(("Publication model: {0}" -f $report.publicationModel))
$markdown.Add("")
$markdown.Add("## Verdict")
$markdown.Add("")
$markdown.Add("- Verdict: $($report.verdict)")
$markdown.Add("- Direct publish allowed: $directPublishAllowedText")
$markdown.Add("- Candidate files reviewed: $($report.candidateFileCount)")
$markdown.Add("- Public-safe files: $($report.publicSafeCount)")
$markdown.Add("- Private-only files: $($report.privateOnlyCount)")
$markdown.Add("- Review-required files: $($report.reviewRequiredCount)")
$markdown.Add("")
$markdown.Add("## Hygiene Gates")
$markdown.Add("")
$markdown.Add("- Secret scan exit code: $($report.secretScan.exitCode)")
$markdown.Add("- Secret hits: $($report.secretScan.hitCount)")
$markdown.Add("- Root layout exit code: $($report.rootLayout.exitCode)")
$markdown.Add("- Root violations: $($report.rootLayout.rootViolationCount)")
$markdown.Add("- Source fatal violations: $($report.rootLayout.sourceFatalViolationCount)")
$markdown.Add("- Source warnings: $($report.rootLayout.sourceWarningCount)")
$markdown.Add("")
$markdown.Add("## Review Notes")
$markdown.Add("")

foreach ($note in $report.notes) {
    $markdown += ("- {0}" -f $note)
}

$markdown += ""
$markdown += "## Public-Safe Sample"
$markdown += ""

if ($report.samplePublicSafePaths.Count -eq 0) {
    $markdown += "- None"
} else {
    foreach ($path in $report.samplePublicSafePaths) {
        $markdown += ("- {0}" -f $path)
    }
}

$markdown += ""
$markdown += "## Private-Only Sample"
$markdown += ""

if ($report.samplePrivateOnlyPaths.Count -eq 0) {
    $markdown += "- None"
} else {
    foreach ($path in $report.samplePrivateOnlyPaths) {
        $markdown += ("- {0}" -f $path)
    }
}

$markdown += ""
$markdown += "## Review-Required Sample"
$markdown += ""

if ($report.sampleReviewRequiredPaths.Count -eq 0) {
    $markdown += "- None"
} else {
    foreach ($path in $report.sampleReviewRequiredPaths) {
        $markdown += ("- {0}" -f $path)
    }
}

$markdown += ""
$markdown += "## Top-Level Summary"
$markdown += ""

foreach ($item in $report.groupedByTopLevel) {
    $markdown += ("- {0} total={1} public_safe={2} private_only={3} review_required={4}" -f $item.topLevel, $item.total, $item.publicSafe, $item.privateOnly, $item.reviewRequired)
}

$markdown | Set-Content -Path $markdownReportPath -Encoding UTF8

Write-Host "[PublicLaunch] Wrote $jsonReportPath"
Write-Host "[PublicLaunch] Wrote $markdownReportPath"
Write-Host "[PublicLaunch] Verdict: $verdict"

if ($FailOnUnsafeTrackedSet -and -not $directPublishAllowed) {
    exit 1
}
