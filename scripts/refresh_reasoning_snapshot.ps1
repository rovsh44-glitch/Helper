param(
    [string]$CurrentStatePath = "doc/CURRENT_STATE.md",
    [string]$BaselineMarkdownPath = "doc/reasoning/active/CURRENT_REASONING_BASELINE.md",
    [string]$BaselineJsonPath = "doc/reasoning/active/CURRENT_REASONING_BASELINE.json",
    [string]$OutputMarkdownPath = "doc/reasoning/active/CURRENT_REASONING_STATE.md",
    [string]$OutputJsonPath = "doc/reasoning/active/CURRENT_REASONING_STATE.json"
)

$ErrorActionPreference = "Stop"

function Read-ReportLineValue {
    param(
        [string[]]$Lines,
        [string]$Prefix
    )

    foreach ($line in $Lines) {
        if ($line.StartsWith($Prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $line.Substring($Prefix.Length).Trim()
        }
    }

    return ""
}

$status = "NOT_STARTED"
$details = "Reasoning benchmark has not been run yet."
$mode = "missing"
$passRate = ""

if (Test-Path $BaselineMarkdownPath) {
    $lines = Get-Content $BaselineMarkdownPath
    $mode = Read-ReportLineValue -Lines $lines -Prefix "Benchmark mode:"
    $baselineStatus = Read-ReportLineValue -Lines $lines -Prefix "Status:"
    $passRate = Read-ReportLineValue -Lines $lines -Prefix "Pass rate:"

    $status = switch ($baselineStatus.ToUpperInvariant()) {
        "PASS" { "BASELINE_READY" }
        "FAIL" { "BASELINE_DEGRADED" }
        "NOT_EXECUTED" { "BASELINE_DEFINED_NOT_EXECUTED" }
        default { "BASELINE_DEFINED_NOT_EXECUTED" }
    }

    $details = switch ($status) {
        "BASELINE_READY" { "Reasoning benchmark baseline exists and last run passed." }
        "BASELINE_DEGRADED" { "Reasoning benchmark baseline exists, but the last run failed." }
        default { "Reasoning benchmark dataset exists, but no live passing baseline was captured yet." }
    }
}

$payload = [ordered]@{
    generated = (Get-Date -Format "yyyy-MM-dd HH:mm:ss K")
    status = $status
    details = $details
    baselineMarkdownPath = $BaselineMarkdownPath
    baselineJsonPath = $BaselineJsonPath
    benchmarkMode = $mode
    lastPassRate = $passRate
}

$jsonDir = [System.IO.Path]::GetDirectoryName($OutputJsonPath)
if (-not [string]::IsNullOrWhiteSpace($jsonDir)) {
    New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null
}
$payload | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputJsonPath -Encoding UTF8

$lines = @()
$lines += "# Current Reasoning State"
$lines += "Generated: $($payload.generated)"
$lines += "Status: $status"
$lines += "Details: $details"
$lines += ""
$lines += "## Topline"
$lines += "- Baseline markdown: $BaselineMarkdownPath"
$lines += "- Baseline json: $BaselineJsonPath"
$lines += "- Benchmark mode: $mode"
$lines += "- Last pass rate: $(if ([string]::IsNullOrWhiteSpace($passRate)) { "n/a" } else { $passRate })"

Set-Content -Path $OutputMarkdownPath -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[ReasoningSnapshot] Snapshot refreshed: $OutputJsonPath"
