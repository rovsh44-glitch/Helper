param(
    [Parameter(Mandatory = $true)][string]$CycleId,
    [int]$DayFrom = 2,
    [int]$DayTo = 14,
    [string]$ApiBase = "http://127.0.0.1:5000",
    [string]$ApiKey = "",
    [int]$ParityRuns = 24,
    [int]$SmokeRuns = 50,
    [int]$EvalScenarios = 200,
    [int]$EvalMinScenarioCount = 200,
    [int]$TimeoutSec = 120,
    [switch]$SkipEvalRealModel,
    [switch]$SkipHumanParity,
    [switch]$SkipClosedLoop,
    [switch]$SkipLlmPreflight
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

if ($DayFrom -lt 1 -or $DayTo -gt 14 -or $DayFrom -gt $DayTo) {
    throw "[PreCertPreviewSweep] Invalid day range."
}

$results = New-Object System.Collections.Generic.List[object]
for ($day = $DayFrom; $day -le $DayTo; $day++) {
    Write-Host ("[PreCertPreviewSweep] Running preview day {0:00}..." -f $day)
    $args = @(
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $root "scripts\run_precert_preview_day.ps1"),
        "-CycleId", $CycleId,
        "-Day", $day,
        "-ApiBase", $ApiBase,
        "-ParityRuns", $ParityRuns,
        "-SmokeRuns", $SmokeRuns,
        "-EvalScenarios", $EvalScenarios,
        "-EvalMinScenarioCount", $EvalMinScenarioCount,
        "-TimeoutSec", $TimeoutSec
    )

    if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
        $args += @("-ApiKey", $ApiKey)
    }

    if ($SkipEvalRealModel.IsPresent) {
        $args += "-SkipEvalRealModel"
    }

    if ($SkipHumanParity.IsPresent) {
        $args += "-SkipHumanParity"
    }

    if ($SkipClosedLoop.IsPresent) {
        $args += "-SkipClosedLoop"
    }

    if ($SkipLlmPreflight.IsPresent) {
        $args += "-SkipLlmPreflight"
    }

    powershell @args
    $results.Add([pscustomobject]@{
        Day = $day.ToString("00")
        ExitCode = $LASTEXITCODE
        Status = if ($LASTEXITCODE -eq 0) { "PASS" } else { "FAIL" }
        SummaryPath = Join-Path $root ("doc\pre_certification\cycles\{0}\preview\day-{1}\PRECERT_PREVIEW_SUMMARY.md" -f $CycleId, $day.ToString("00"))
    })
}

$sweepDir = Join-Path $root ("doc\pre_certification\cycles\" + $CycleId + "\preview")
New-Item -ItemType Directory -Force -Path $sweepDir | Out-Null

$summaryPath = Join-Path $sweepDir "PRECERT_PREVIEW_SWEEP_SUMMARY.md"
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Pre-Cert Preview Sweep Summary")
$lines.Add("")
$lines.Add(('- Cycle: `{0}`' -f $CycleId))
$lines.Add(('- Range: `day-{0} ... day-{1}`' -f $DayFrom.ToString("00"), $DayTo.ToString("00")))
$lines.Add('- Status: `NOT_COUNTED_PREVIEW`')
$lines.Add("")
$lines.Add("| Day | Status | ExitCode | Summary |")
$lines.Add("|---|---|---:|---|")
foreach ($result in $results) {
    $lines.Add("| day-$($result.Day) | $($result.Status) | $($result.ExitCode) | $($result.SummaryPath.Replace($root, '.')) |")
}

Set-Content -Path $summaryPath -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "[PreCertPreviewSweep] Summary: $summaryPath"

if (@($results | Where-Object { $_.ExitCode -ne 0 }).Count -gt 0) {
    exit 1
}
