param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "temp\hygiene"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$secretReportPath = Join-Path $OutputDirectory "secret_scan_report.json"
$layoutReportPath = Join-Path $OutputDirectory "root_layout_report.json"
$markdownReportPath = Join-Path $OutputDirectory "REPO_HYGIENE_REPORT.md"

$secretExitCode = 0
$layoutExitCode = 0

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "secret_scan.ps1") -ScanMode repo -ReportPath $secretReportPath
$secretExitCode = $LASTEXITCODE

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check_root_layout.ps1") -ReportPath $layoutReportPath
$layoutExitCode = $LASTEXITCODE

$secretReport = Get-Content -Path $secretReportPath -Raw | ConvertFrom-Json
$layoutReport = Get-Content -Path $layoutReportPath -Raw | ConvertFrom-Json

$markdown = @(
    "# Repo Hygiene Report",
    "",
    ("Generated: {0}" -f (Get-Date -Format o)),
    "",
    "## Summary",
    "",
    ("- Secret scan exit code: {0}" -f $secretExitCode),
    ("- Root layout exit code: {0}" -f $layoutExitCode),
    ("- Secret findings: {0}" -f $secretReport.hits.Count),
    ("- Root violations: {0}" -f $layoutReport.rootViolations.Count),
    ("- Source fatal violations: {0}" -f $layoutReport.sourceFatalViolations.Count),
    ("- Source warnings: {0}" -f $layoutReport.sourceWarnings.Count),
    "",
    "## Secret Findings",
    ""
)

if ($secretReport.hits.Count -eq 0) {
    $markdown += "- None"
} else {
    foreach ($hit in $secretReport.hits) {
        $markdown += ("- {0}:{1} pattern {2}" -f $hit.File, $hit.Line, $hit.Pattern)
    }
}

$markdown += ""
$markdown += "## Root Layout Violations"
$markdown += ""

if ($layoutReport.rootViolations.Count -eq 0 -and $layoutReport.sourceFatalViolations.Count -eq 0 -and $layoutReport.sourceWarnings.Count -eq 0) {
    $markdown += "- None"
} else {
    foreach ($item in @($layoutReport.rootViolations + $layoutReport.sourceFatalViolations + $layoutReport.sourceWarnings)) {
        $markdown += ("- {0} :: {1}" -f $item.path, $item.detail)
    }
}

$markdown | Set-Content -Path $markdownReportPath -Encoding UTF8

Write-Host "[Hygiene] Wrote $markdownReportPath"
Write-Host "[Hygiene] Wrote $secretReportPath"
Write-Host "[Hygiene] Wrote $layoutReportPath"

if ($secretExitCode -ne 0 -or $layoutExitCode -ne 0) {
    exit 1
}
