param(
    [string]$Solution = "Helper.sln",
    [string]$ReportPath = "doc/load_streaming_chaos_report.md"
)

$ErrorActionPreference = "Stop"

Write-Host "[LoadStreamingChaos] Running load/chaos suite..."
$output = dotnet test $Solution --no-build --filter "Category=Load" 2>&1
$exitCode = $LASTEXITCODE

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
$report = @"
# Streaming Load/Chaos Report
Generated: $timestamp

## Command
dotnet test $Solution --no-build --filter Category=Load

## Exit Code
$exitCode

## Raw Output
$output
"@

Set-Content -Path $ReportPath -Value $report -Encoding UTF8
Write-Host "[LoadStreamingChaos] Report saved to $ReportPath"

if ($exitCode -ne 0) {
    throw "[LoadStreamingChaos] Load/chaos suite failed."
}

Write-Host "[LoadStreamingChaos] Passed."

