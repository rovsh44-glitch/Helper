param(
    [Alias("Solution", "Target")]
    [string]$ProjectPath = "test\Helper.Runtime.Certification.Tests\Helper.Runtime.Certification.Tests.csproj",
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore,
    [string]$ReportPath = "temp/verification/heavy/load_streaming_chaos_report.md"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "common\StrictDotnetFilteredTest.ps1")

Write-Host "[LoadStreamingChaos] Running load/chaos suite..."
$result = Invoke-StrictDotnetFilteredTest `
    -ProjectPath $ProjectPath `
    -Filter "Category=Load" `
    -Configuration $Configuration `
    -NoBuild:$NoBuild `
    -NoRestore:$NoRestore

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
$report = @"
# Streaming Load/Chaos Report
Generated: $timestamp

## Command
$($result.CommandDisplay)

## Exit Code
$($result.ExitCode)

## Raw Output
$($result.OutputText)
"@

$resolvedReportPath = Resolve-HelperRepoPath -Path $ReportPath
$reportDirectory = Split-Path -Parent $resolvedReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $resolvedReportPath -Value $report -Encoding UTF8
Write-Host "[LoadStreamingChaos] Report saved to $resolvedReportPath"

Assert-StrictDotnetFilteredTestSucceeded -Result $result -FailurePrefix "[LoadStreamingChaos]"

Write-Host "[LoadStreamingChaos] Passed."

