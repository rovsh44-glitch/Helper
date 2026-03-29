param(
    [Parameter(Mandatory = $true)][string]$CycleId,
    [Parameter(Mandatory = $true)][int]$Day,
    [string]$ApiBase = "http://127.0.0.1:5000",
    [string]$ApiKey = "",
    [int]$ParityRuns = 24,
    [int]$SmokeRuns = 50,
    [int]$EvalScenarios = 200,
    [int]$EvalMinScenarioCount = 200,
    [int]$TimeoutSec = 120,
    [int]$ApiReadyTimeoutSec = 600,
    [int]$ApiReadyPollIntervalMs = 2000,
    [int]$ParityLookbackHours = 24,
    [switch]$SkipEvalRealModel,
    [switch]$SkipHumanParity,
    [switch]$SkipClosedLoop,
    [switch]$SkipLlmPreflight
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "precert_day_runner_common.ps1")

function Write-PreviewReadme {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$CycleId,
        [Parameter(Mandatory = $true)][int]$Day,
        [Parameter(Mandatory = $true)][string]$ParityWorkload,
        [Parameter(Mandatory = $true)][string]$SmokeWorkload,
        [Parameter(Mandatory = $true)][string]$SnapshotRoot
    )

    $content = @()
    $content += "# Pre-Cert Preview Day"
    $content += ""
    $content += ('- Cycle: `{0}`' -f $CycleId)
    $content += ('- Day: `{0}`' -f $Day.ToString("00"))
    $content += '- Status: `NOT_COUNTED_PREVIEW`'
    $content += ('- PreviewParityWorkload: `{0}`' -f $ParityWorkload)
    $content += ('- PreviewSmokeWorkload: `{0}`' -f $SmokeWorkload)
    $content += ('- PreviewSnapshotRoot: `{0}`' -f $SnapshotRoot)
    $content += ""
    $content += "## Guards"
    $content += '1. No writes to `PRECERT_CYCLE_STATE.json`.'
    $content += '2. No writes to official `day-XX/DAILY_CERT_SUMMARY_dayXX.md`.'
    $content += "3. Parity history isolated by unique preview workload classes."
    $content += "4. Parity snapshots isolated by preview snapshot root."

    Set-Content -Path $Path -Value ($content -join "`r`n") -Encoding UTF8
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

$cycleDir = Join-Path $root ("doc\pre_certification\cycles\" + $CycleId)
if (-not (Test-Path $cycleDir)) {
    throw "[PreCertPreview] Cycle not found: $cycleDir"
}

if ($Day -lt 1 -or $Day -gt 14) {
    throw "[PreCertPreview] Day must be between 1 and 14."
}

$dayLabel = ConvertTo-PreCertDayLabel -Day $Day
$previewDir = Join-Path $cycleDir ("preview\" + $dayLabel)
$snapshotRoot = Join-Path $previewDir "parity_snapshots"
$previewRuntimeDir = Join-Path $cycleDir "preview\runtime"
$previewLogsRoot = Join-Path $previewDir "logs"
$previewRunId = Get-Date -Format "yyyyMMdd-HHmmss"
$previewRunLogDir = Join-Path $previewLogsRoot $previewRunId
New-Item -ItemType Directory -Force -Path $previewDir | Out-Null
New-Item -ItemType Directory -Force -Path $previewRuntimeDir | Out-Null
New-Item -ItemType Directory -Force -Path $previewRunLogDir | Out-Null

$script:PreCertTracePath = Join-Path $previewRunLogDir "preview_wrapper.trace.log"
Set-Content -Path $script:PreCertTracePath -Value @(
    "# Preview Wrapper Trace"
    ("cycle={0}" -f $CycleId)
    ("day={0}" -f $dayLabel)
    ("runId={0}" -f $previewRunId)
) -Encoding UTF8

$previewPrefix = ("preview-{0}-{1}" -f $CycleId.ToLowerInvariant().Replace("_", "-"), $dayLabel)
$parityWorkload = $previewPrefix + "-parity"
$smokeWorkload = $previewPrefix + "-smoke"

Copy-PreCertSeedParitySnapshots -WorkspaceRoot $root -SnapshotRoot $snapshotRoot
Write-PreviewReadme -Path (Join-Path $previewDir "README.md") -CycleId $CycleId -Day $Day -ParityWorkload $parityWorkload -SmokeWorkload $smokeWorkload -SnapshotRoot $snapshotRoot

$resolvedApiKey = Get-PreCertApiKey -WorkspaceRoot $root -ExplicitApiKey $ApiKey
$steps = Invoke-PreCertPackageSequence `
    -WorkspaceRoot $root `
    -LogDirectory $previewRunLogDir `
    -ParityWorkload $parityWorkload `
    -SmokeWorkload $smokeWorkload `
    -RuntimeDir $previewRuntimeDir `
    -LlmPreflightReportPath (Join-Path $previewDir "LLM_LATENCY_PREFLIGHT_preview.md") `
    -ParityBatchReportPath (Join-Path $previewDir "PARITY_GOLDEN_BATCH_preview.md") `
    -ParityGateReportPath (Join-Path $previewDir "HELPER_PARITY_GATE_preview.md") `
    -ParityWindowReportPath (Join-Path $previewDir "HELPER_PARITY_WINDOW_GATE_preview.md") `
    -SmokeCompileReportPath (Join-Path $previewDir "SMOKE_COMPILE_preview.md") `
    -ClosedLoopReportPath (Join-Path $previewDir "CLOSED_LOOP_PREDICTABILITY_preview.md") `
    -EvalGateLogPath (Join-Path $previewDir "EVAL_GATE_preview.log") `
    -EvalRealModelOutputPath (Join-Path $previewDir "EVAL_REAL_MODEL_preview.md") `
    -EvalRealModelErrorLogPath (Join-Path $previewDir "EVAL_REAL_MODEL_preview.errors.json") `
    -EvalRealModelReadinessReportPath (Join-Path $previewDir "API_READY_preview.md") `
    -HumanParityReportPath (Join-Path $previewDir "HUMAN_PARITY_preview.md") `
    -SnapshotRoot $snapshotRoot `
    -ApiBase $ApiBase `
    -ApiKey $resolvedApiKey `
    -ParityRuns $ParityRuns `
    -SmokeRuns $SmokeRuns `
    -EvalScenarios $EvalScenarios `
    -EvalMinScenarioCount $EvalMinScenarioCount `
    -TimeoutSec $TimeoutSec `
    -ApiReadyTimeoutSec $ApiReadyTimeoutSec `
    -ApiReadyPollIntervalMs $ApiReadyPollIntervalMs `
    -ParityLookbackHours $ParityLookbackHours `
    -SkipEvalRealModel:$SkipEvalRealModel `
    -SkipHumanParity:$SkipHumanParity `
    -SkipClosedLoop:$SkipClosedLoop `
    -SkipLlmPreflight:$SkipLlmPreflight

$failed = @($steps | Where-Object { $_.Status -eq "FAIL" })
$summary = New-Object System.Collections.Generic.List[string]
$summary.Add("# Pre-Cert Preview Summary")
$summary.Add("")
$summary.Add(('- Cycle: `{0}`' -f $CycleId))
$summary.Add(('- Day: `{0}`' -f $Day.ToString("00")))
$summary.Add('- Status: `NOT_COUNTED_PREVIEW`')
$summary.Add(('- Preview artifact root: `{0}`' -f $previewDir))
$summary.Add(('- Preview parity workload: `{0}`' -f $parityWorkload))
$summary.Add(('- Preview smoke workload: `{0}`' -f $smokeWorkload))
$summary.Add(('- Preview snapshot root: `{0}`' -f $snapshotRoot))
$summary.Add("")
$summary.Add("| Step | Status | Notes |")
$summary.Add("|---|---|---|")
foreach ($step in $steps) {
    $notes = if ([string]::IsNullOrWhiteSpace($step.Notes)) { "-" } else { $step.Notes.Replace("|", "/") }
    $summary.Add("| $($step.Name) | $($step.Status) | $notes |")
}
$summary.Add("")
$summary.Add('## Guardrail')
$summary.Add("1. This run is diagnostic only and does not modify pre-cert closed-day counters.")
$summary.Add("2. Official counted day must still run on the next eligible UTC calendar date.")
$summary.Add('3. Preview parity artifacts are isolated from official `3.1` and `3.2` sources of truth.')

$summaryPath = Join-Path $previewDir "PRECERT_PREVIEW_SUMMARY.md"
Set-Content -Path $summaryPath -Value ($summary -join "`r`n") -Encoding UTF8
Write-PreCertTrace -Message ("summary_written path={0} failedSteps={1}" -f $summaryPath, $failed.Count)

Write-Host "[PreCertPreview] Summary: $summaryPath"
if ($failed.Count -gt 0) {
    Write-PreCertTrace -Message ("preview_completed status=FAIL steps={0}" -f (($failed | Select-Object -ExpandProperty Name) -join ","))
    Write-Host ("[PreCertPreview] Failed steps: {0}" -f (($failed | Select-Object -ExpandProperty Name) -join ", ")) -ForegroundColor Yellow
    exit 1
}

Write-PreCertTrace -Message "preview_completed status=PASS"
Write-Host "[PreCertPreview] Completed successfully." -ForegroundColor Green
