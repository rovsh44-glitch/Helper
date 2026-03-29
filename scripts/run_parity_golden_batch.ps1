param(
    [int]$Runs = 24,
    [int]$TimeoutSec = 120,
    [string]$ReportPath = "",
    [string]$WorkloadClass = "parity",
    [string]$LogsRoot = "",
    [switch]$FailOnThresholds
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root
. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")
. (Join-Path $PSScriptRoot "common\GenerationArtifactDetection.ps1")
$pathConfig = Get-HelperPathConfig -WorkspaceRoot $root
$helperRuntimeCliInvokerPath = Join-Path $PSScriptRoot "invoke_helper_runtime_cli.ps1"

$dayDir = Join-Path $root "doc\certification_2026-03-15\day-01"
New-Item -ItemType Directory -Path $dayDir -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $dayDir "PARITY_GOLDEN_BATCH_day01.md"
}

$resolvedLogsRoot = if ([string]::IsNullOrWhiteSpace($LogsRoot)) {
    Join-Path $root "temp\verification\parity_batch_runtime\LOG"
}
else {
    if ([System.IO.Path]::IsPathRooted($LogsRoot)) {
        $LogsRoot
    }
    else {
        Join-Path $root $LogsRoot
    }
}
$resolvedTelemetryPath = Join-Path $resolvedLogsRoot "template_routing_decisions.jsonl"
New-Item -ItemType Directory -Force -Path $resolvedLogsRoot | Out-Null

$previousLogsRoot = $env:HELPER_LOGS_ROOT
$previousTelemetryPath = $env:HELPER_TEMPLATE_ROUTING_TELEMETRY_PATH

$env:HELPER_SMOKE_PROFILE = "false"
$env:HELPER_ENABLE_METACOGNITIVE_DEBUG = "false"
$env:HELPER_ENABLE_SUCCESS_REFLECTION = "false"
$env:HELPER_MAX_HEAL_ITERATIONS = "0"
$env:HELPER_CREATE_TIMEOUT_SEC = $TimeoutSec.ToString()
$env:HELPER_CREATE_TIMEOUT_SYNTHESIS_SEC = [Math]::Max(10, $TimeoutSec - 12).ToString()
$env:HELPER_GENERATION_WORKLOAD_CLASS = $WorkloadClass
$env:HELPER_LOGS_ROOT = $resolvedLogsRoot
$env:HELPER_TEMPLATE_ROUTING_TELEMETRY_PATH = $resolvedTelemetryPath

$prompts = @(
    "Generate an engineering calculator in C# WPF with scientific functions.",
    "Generate a chess game in C# WPF with legal move validation.",
    "Generate a PDF to EPUB and EPUB to PDF converter in C#."
)

$projectsRoots = Get-GenerationArtifactProjectRoots -WorkspaceRoot $root -PathConfig $pathConfig
$results = @()

try {
    for ($i = 1; $i -le $Runs; $i++) {
        $prompt = $prompts[($i - 1) % $prompts.Count]
        Write-Host ("[parity-batch] run {0}/{1}" -f $i, $Runs)
        $started = Get-Date
        $startedUtc = [DateTime]::UtcNow
        $previousLatest = Find-LatestValidationReport -ProjectsRoots $projectsRoots
        $cliArgs = @("create", $prompt)
        $output = & $helperRuntimeCliInvokerPath -CliArgs $cliArgs 2>&1 | Out-String
        $durationSec = [Math]::Round(((Get-Date) - $started).TotalSeconds, 2)

        $reportFile = Find-ValidationReportForRun -ProjectsRoots $projectsRoots -RunStartedUtc $startedUtc -PreviousLatestPath $(if ($null -ne $previousLatest) { $previousLatest.FullName } else { "" })
        $compileGatePassed = $false
        $goldenEligible = $false
        $goldenMatched = $false
        $reportFound = $false
        $reportShort = "-"

        if ($null -ne $reportFile) {
            $reportFound = $true
            $reportShort = $reportFile.FullName.Replace($root, ".")
            $report = Get-Content -Raw $reportFile.FullName | ConvertFrom-Json
            $compileGatePassed = [bool]$report.CompileGatePassed
            $goldenEligible = [bool]$report.GoldenTemplateEligible
            $goldenMatched = [bool]$report.GoldenTemplateMatched
        }

        $results += [pscustomobject]@{
            Run         = $i
            Prompt      = $prompt
            Success     = $compileGatePassed
            DurationSec = $durationSec
            Eligible    = $goldenEligible
            Hit         = $goldenMatched
            ReportFound = $reportFound
            Report      = $reportShort
            OutputTail  = ($output -split "`r?`n" | Select-Object -Last 6) -join "`n"
        }
    }
}
finally {
    if ([string]::IsNullOrWhiteSpace($previousLogsRoot)) {
        Remove-Item Env:HELPER_LOGS_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:HELPER_LOGS_ROOT = $previousLogsRoot
    }

    if ([string]::IsNullOrWhiteSpace($previousTelemetryPath)) {
        Remove-Item Env:HELPER_TEMPLATE_ROUTING_TELEMETRY_PATH -ErrorAction SilentlyContinue
    }
    else {
        $env:HELPER_TEMPLATE_ROUTING_TELEMETRY_PATH = $previousTelemetryPath
    }
}

$passed = @($results | Where-Object { $_.Success }).Count
$successRate = if ($Runs -gt 0) { [Math]::Round($passed / $Runs, 4) } else { 0.0 }

$durations = @($results | Sort-Object DurationSec | Select-Object -ExpandProperty DurationSec)
$p95 = 0.0
if ($durations.Count -gt 0) {
    $index = [Math]::Ceiling($durations.Count * 0.95) - 1
    if ($index -lt 0) { $index = 0 }
    $p95 = [Math]::Round([double]$durations[$index], 2)
}

$eligibleAttempts = @($results | Where-Object { $_.Eligible }).Count
$eligibleHits = @($results | Where-Object { $_.Eligible -and $_.Hit }).Count
$goldenHitRate = if ($eligibleAttempts -gt 0) { [Math]::Round($eligibleHits / $eligibleAttempts, 4) } else { 0.0 }

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Parity Golden Batch (Day 01)")
$lines.Add("")
$lines.Add("| Metric | Value |")
$lines.Add("|---|---|")
$lines.Add("| Runs | $Runs |")
$lines.Add("| Compile pass | $passed |")
$lines.Add("| Success rate | $successRate |")
$lines.Add("| P95 duration (sec) | $p95 |")
$lines.Add("| Golden eligible attempts | $eligibleAttempts |")
$lines.Add("| Golden hits | $eligibleHits |")
$lines.Add("| Golden hit rate | $goldenHitRate |")
$lines.Add("")
$lines.Add("## Runs")
$lines.Add("")
$lines.Add("| Run | Success | Eligible | Hit | DurationSec | ReportFound | Report |")
$lines.Add("|---:|---|---|---|---:|---|---|")
foreach ($row in $results) {
    $status = if ($row.Success) { "pass" } else { "fail" }
    $eligible = if ($row.Eligible) { "yes" } else { "no" }
    $hit = if ($row.Hit) { "yes" } else { "no" }
    $reportFound = if ($row.ReportFound) { "yes" } else { "no" }
    $durationText = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.##}", [double]$row.DurationSec)
    $lines.Add("| $($row.Run) | $status | $eligible | $hit | $durationText | $reportFound | $($row.Report) |")
}

$successOk = $successRate -ge 0.95
$p95Ok = $p95 -le 25
$attemptsOk = $eligibleAttempts -ge 20
$goldenHitOk = $goldenHitRate -ge 0.90

$lines.Add("")
$lines.Add("## Acceptance")
$lines.Add("")
$lines.Add("- success_rate >= 0.95: $($(if ($successOk) { 'yes' } else { 'no' }))")
$lines.Add("- p95_duration_sec <= 25: $($(if ($p95Ok) { 'yes' } else { 'no' }))")
$lines.Add("- golden_attempts >= 20: $($(if ($attemptsOk) { 'yes' } else { 'no' }))")
$lines.Add("- golden_hit_rate >= 0.90: $($(if ($goldenHitOk) { 'yes' } else { 'no' }))")

Set-Content -Path $ReportPath -Value ($lines -join [Environment]::NewLine) -Encoding UTF8
Write-Host ("[parity-batch] report saved: {0}" -f $ReportPath)

if ($FailOnThresholds -and -not ($successOk -and $p95Ok -and $attemptsOk -and $goldenHitOk)) {
    throw "[parity-batch] Thresholds failed."
}
