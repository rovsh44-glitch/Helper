param(
    [int]$Runs = 20,
    [int]$TimeoutSec = 120,
    [string]$Prompt = "Generate a minimal C# WPF TODO app with model, interface and service. Keep blueprint compact and compile-oriented.",
    [string]$WorkloadClass = "smoke",
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root
. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")
. (Join-Path $PSScriptRoot "common\GenerationArtifactDetection.ps1")
$pathConfig = Get-HelperPathConfig -WorkspaceRoot $root
$helperRuntimeCliInvokerPath = Join-Path $PSScriptRoot "invoke_helper_runtime_cli.ps1"

$docDir = Join-Path $root "doc"
New-Item -ItemType Directory -Path $docDir -Force | Out-Null

$utcNow = [DateTime]::UtcNow
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportPath = Join-Path $docDir ("smoke_compile_baseline_{0:yyyy-MM-dd}.md" -f $utcNow)
}
else {
    $reportPath = [System.IO.Path]::GetFullPath($ReportPath)
    $reportDir = Split-Path -Parent $reportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDir)) {
        New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
    }
}

$env:HELPER_SMOKE_PROFILE = "true"
$env:HELPER_METHOD_GEN_TIMEOUT_SEC = "8"
$env:HELPER_CRITIC_TIMEOUT_SEC = "8"
$env:HELPER_ENABLE_METACOGNITIVE_DEBUG = "false"
$env:HELPER_ENABLE_SUCCESS_REFLECTION = "false"
$env:HELPER_MAX_HEAL_ITERATIONS = "0"
$env:HELPER_GENERATION_WORKLOAD_CLASS = $WorkloadClass
$env:HELPER_CREATE_TIMEOUT_SEC = $TimeoutSec.ToString()
$synthesisBudgetSec = [Math]::Max(10, $TimeoutSec - 12)
$env:HELPER_CREATE_TIMEOUT_SYNTHESIS_SEC = $synthesisBudgetSec.ToString()
$env:HELPER_REQUIRE_GENERATION_FORMAT = "false"
$env:HELPER_COMPILE_GATE_MAX_REPAIRS = "2"

function Summarize-StageDurations {
    param($StageDurationsSec)

    if ($null -eq $StageDurationsSec) {
        return ""
    }

    $parts = New-Object System.Collections.Generic.List[string]
    $total = 0.0
    foreach ($prop in $StageDurationsSec.PSObject.Properties) {
        $value = 0.0
        try {
            $value = [double]$prop.Value
        }
        catch {
            continue
        }

        $total += $value
        $parts.Add(("{0}={1}s" -f $prop.Name, [Math]::Round($value, 2)))
    }

    return [PSCustomObject]@{
        TotalSeconds = [Math]::Round($total, 2)
        Text = ($parts -join ", ")
    }
}

$results = @()
$projectsRoots = Get-GenerationArtifactProjectRoots -WorkspaceRoot $root -PathConfig $pathConfig

for ($i = 1; $i -le $Runs; $i++) {
    Write-Host ("[smoke] run {0}/{1}" -f $i, $Runs)
    $started = Get-Date
    $startedUtc = [DateTime]::UtcNow
    $previousLatest = Find-LatestValidationReport -ProjectsRoots $projectsRoots
    $output = & $helperRuntimeCliInvokerPath -CliArgs @("create", $Prompt) 2>&1 | Out-String
    $durationSec = [Math]::Round(((Get-Date) - $started).TotalSeconds, 2)

    $reportFile = Find-ValidationReportForRun -ProjectsRoots $projectsRoots -RunStartedUtc $startedUtc -PreviousLatestPath $(if ($null -ne $previousLatest) { $previousLatest.FullName } else { "" })

    $compileGatePassed = $false
    $errorCodes = @()
    $retryCount = 0
    $stageSummary = ""
    $stageTotalSec = 0.0
    $reportFound = $false

    if ($null -ne $reportFile) {
        $reportFound = $true
        $report = Get-Content -Raw $reportFile.FullName | ConvertFrom-Json
        $compileGatePassed = [bool]$report.CompileGatePassed
        try {
            $retryCount = [int]$report.RetryCount
        }
        catch {
            $retryCount = 0
        }

        $stageInfo = Summarize-StageDurations -StageDurationsSec $report.StageDurationsSec
        if ($null -ne $stageInfo) {
            $stageSummary = [string]$stageInfo.Text
            $stageTotalSec = [double]$stageInfo.TotalSeconds
        }

        foreach ($err in @($report.Errors)) {
            $matches = [regex]::Matches([string]$err, "CS\d{4}|DUPLICATE_SIGNATURE|BLUEPRINT_CONTRACT_FAIL|FORMAT|GENERATION_TIMEOUT")
            foreach ($match in $matches) {
                $errorCodes += $match.Value
            }
        }
    }
    else {
        $errorCodes += "REPORT_NOT_FOUND"
    }

    $results += [pscustomobject]@{
        Run            = $i
        Success        = $compileGatePassed
        Duration       = $durationSec
        RetryCount     = $retryCount
        StageTotalSec  = $stageTotalSec
        StageDurations = $stageSummary
        ReportFound    = $reportFound
        ErrorCodes     = @($errorCodes)
        ReportPath     = if ($null -ne $reportFile) { $reportFile.FullName } else { "" }
        OutputTail     = ($output -split "`r?`n" | Select-Object -Last 8) -join "`n"
    }
}

$passed = @($results | Where-Object { $_.Success }).Count
$passRate = if ($Runs -gt 0) { [Math]::Round($passed / $Runs, 3) } else { 0.0 }

$durations = @($results | Sort-Object Duration | Select-Object -ExpandProperty Duration)
$p95 = 0.0
if ($durations.Count -gt 0) {
    $index = [Math]::Ceiling($durations.Count * 0.95) - 1
    if ($index -lt 0) { $index = 0 }
    $p95 = [Math]::Round([double]$durations[$index], 2)
}

$codeCounter = @{}
foreach ($row in $results) {
    foreach ($code in $row.ErrorCodes) {
        if (-not $codeCounter.ContainsKey($code)) {
            $codeCounter[$code] = 0
        }

        $codeCounter[$code] += 1
    }
}

$topCodes = @($codeCounter.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 10)

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Smoke Compile Baseline ({0})" -f $utcNow.ToString("yyyy-MM-dd"))
$lines.Add("")
$lines.Add("| Metric | Value |")
$lines.Add("|---|---|")
$lines.Add("| Runs | $Runs |")
$lines.Add("| Compile pass | $passed |")
$lines.Add("| Pass rate | $passRate |")
$lines.Add("| P95 duration (sec) | $p95 |")
$lines.Add("")
$lines.Add("## Runs")
$lines.Add("")
$lines.Add("| Run | Success | DurationSec | RetryCount | StageTotalSec | ReportFound | Report |")
$lines.Add("|---:|---|---:|---:|---:|---|---|")

foreach ($row in $results) {
    $status = if ($row.Success) { "pass" } else { "fail" }
    $reportShort = if ([string]::IsNullOrWhiteSpace($row.ReportPath)) { "-" } else { $row.ReportPath.Replace($root, ".") }
    $durationText = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.##}", [double]$row.Duration)
    $stageTotalText = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.##}", [double]$row.StageTotalSec)
    $reportFoundText = if ($row.ReportFound) { "yes" } else { "no" }
    $rowLine = "| {0} | {1} | {2} | {3} | {4} | {5} | {6} |" -f $row.Run, $status, $durationText, $row.RetryCount, $stageTotalText, $reportFoundText, $reportShort
    $lines.Add($rowLine)
}

$lines.Add("")
$lines.Add("## Top Error Codes")
$lines.Add("")

if ($topCodes.Count -eq 0) {
    $lines.Add("- none")
}
else {
    foreach ($entry in $topCodes) {
        $codeLine = "- {0}: {1}" -f $entry.Key, $entry.Value
        $lines.Add($codeLine)
    }
}

$lines.Add("")
$lines.Add("## Acceptance Snapshot")
$lines.Add("")
$lines.Add("- smoke_compile_pass_rate >= 0.90: {0}" -f ($(if ($passRate -ge 0.90) { "yes" } else { "no" })))
$lines.Add("- smoke_p95_duration_sec <= 120: {0}" -f ($(if ($p95 -le 120) { "yes" } else { "no" })))

$outliers = @($results | Where-Object { [double]$_.Duration -ge 120 } | Sort-Object Duration -Descending)
$lines.Add("")
$lines.Add("## Latency Outliers (>=120s)")
if ($outliers.Count -eq 0) {
    $lines.Add("- none")
}
else {
    foreach ($row in $outliers) {
        $reportShort = if ([string]::IsNullOrWhiteSpace($row.ReportPath)) { "-" } else { $row.ReportPath.Replace($root, ".") }
        $lines.Add("- run $($row.Run): duration=$([Math]::Round([double]$row.Duration,2))s, retry=$($row.RetryCount), stageTotal=$([Math]::Round([double]$row.StageTotalSec,2))s, report=$reportShort")
        if (-not [string]::IsNullOrWhiteSpace([string]$row.StageDurations)) {
            $lines.Add("  stageDurations: $([string]$row.StageDurations)")
        }
    }
}

Set-Content -Path $reportPath -Value ($lines -join [Environment]::NewLine) -Encoding UTF8
Write-Host ("[smoke] report saved: {0}" -f $reportPath)
