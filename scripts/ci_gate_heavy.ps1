param(
    [int]$ParityRuns = 24,
    [int]$ParityLookbackHours = 24,
    [int]$ParityWindowDays = 7,
    [string]$ParitySnapshotRoot = "",
    [string]$HeavyEvidenceRoot = "temp/verification/heavy",
    [switch]$RequireParityWindow
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-CiHeavyStep {
    param(
        [string]$Name,
        [scriptblock]$Command
    )

    Write-Host "[CI Heavy] $Name..."
    $global:LASTEXITCODE = 0
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "[CI Heavy] Step failed: $Name (exit code: $LASTEXITCODE)."
    }
}

function Resolve-HeavyPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $PathValue))
}

function Read-BoolFromEnv {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $raw = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $false
    }

    switch ($raw.Trim().ToLowerInvariant()) {
        "1" { return $true }
        "true" { return $true }
        "yes" { return $true }
        "on" { return $true }
        default { return $false }
    }
}

function Get-ParityDailyDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [string]$SnapshotRoot
    )

    $resolvedRoot = if ([string]::IsNullOrWhiteSpace($SnapshotRoot)) {
        Join-Path $RepoRoot "doc\parity_nightly"
    }
    else {
        Resolve-HeavyPath -RepoRoot $RepoRoot -PathValue $SnapshotRoot
    }

    return Join-Path $resolvedRoot "daily"
}

function Get-ParityWindowAvailableDayCount {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DailyDirectory
    )

    if (-not (Test-Path -LiteralPath $DailyDirectory)) {
        return 0
    }

    return @(Get-ChildItem -LiteralPath $DailyDirectory -Filter "*.json" -File -ErrorAction SilentlyContinue).Count
}

function Write-HeavySkipReport {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string[]]$Lines
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    Set-Content -LiteralPath $Path -Value $Lines -Encoding UTF8
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$resolvedHeavyEvidenceRoot = Resolve-HeavyPath -RepoRoot $repoRoot -PathValue $HeavyEvidenceRoot
New-Item -ItemType Directory -Force -Path $resolvedHeavyEvidenceRoot | Out-Null

$resolvedParityBatchReport = Join-Path $resolvedHeavyEvidenceRoot ("PARITY_GOLDEN_BATCH_" + $timestamp + ".md")
$resolvedParityGateReport = Join-Path $resolvedHeavyEvidenceRoot ("HELPER_PARITY_GATE_" + $timestamp + ".md")
$resolvedParityBenchmarkReport = Join-Path $resolvedHeavyEvidenceRoot ("HELPER_GENERATION_PARITY_BENCHMARK_" + $timestamp + ".md")
$resolvedParityWindowGateReport = Join-Path $resolvedHeavyEvidenceRoot ("HELPER_PARITY_WINDOW_GATE_" + $timestamp + ".md")
$resolvedParityWindowSkipReport = Join-Path $resolvedHeavyEvidenceRoot ("HELPER_PARITY_WINDOW_GATE_SKIPPED_" + $timestamp + ".md")
$resolvedParityBatchLogsRoot = Join-Path $resolvedHeavyEvidenceRoot "parity_batch\LOG"

$ParityRuns = [Math]::Max(1, $ParityRuns)
$ParityLookbackHours = [Math]::Max(1, $ParityLookbackHours)
$ParityWindowDays = [Math]::Max(1, [Math]::Min(30, $ParityWindowDays))
$effectiveRequireParityWindow = $RequireParityWindow -or (Read-BoolFromEnv -Name "HELPER_CI_HEAVY_REQUIRE_PARITY_WINDOW")
$parityDailyDirectory = Get-ParityDailyDirectory -RepoRoot $repoRoot -SnapshotRoot $ParitySnapshotRoot

Invoke-CiHeavyStep "Load/Chaos smoke" {
    powershell -ExecutionPolicy Bypass -File scripts/run_load_chaos_smoke.ps1 -Configuration Debug
}

Invoke-CiHeavyStep "Generation parity golden batch" {
    powershell -ExecutionPolicy Bypass -File scripts/run_parity_golden_batch.ps1 `
        -Runs $ParityRuns `
        -ReportPath $resolvedParityBatchReport `
        -LogsRoot $resolvedParityBatchLogsRoot `
        -FailOnThresholds
}

Invoke-CiHeavyStep "Generation parity gate" {
    $arguments = @(
        "-ExecutionPolicy", "Bypass",
        "-File", "scripts/run_generation_parity_gate.ps1",
        "-ReportPath", $resolvedParityGateReport,
        "-WorkloadClasses", "parity",
        "-LookbackHours", $ParityLookbackHours
    )

    if (-not [string]::IsNullOrWhiteSpace($ParitySnapshotRoot)) {
        $arguments += @("-SnapshotRoot", $ParitySnapshotRoot)
    }

    powershell @arguments
}

Invoke-CiHeavyStep "Generation parity benchmark" {
    powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_benchmark.ps1 -ReportPath $resolvedParityBenchmarkReport
}

$availableParityWindowDays = Get-ParityWindowAvailableDayCount -DailyDirectory $parityDailyDirectory
if ($effectiveRequireParityWindow -or $availableParityWindowDays -ge $ParityWindowDays) {
    Invoke-CiHeavyStep "Generation parity window gate" {
        $arguments = @(
            "-ExecutionPolicy", "Bypass",
            "-File", "scripts/run_generation_parity_window_gate.ps1",
            "-ReportPath", $resolvedParityWindowGateReport,
            "-WindowDays", $ParityWindowDays
        )

        if (-not [string]::IsNullOrWhiteSpace($ParitySnapshotRoot)) {
            $arguments += @("-SnapshotRoot", $ParitySnapshotRoot)
        }

        powershell @arguments
    }
}
else {
    $skipLines = @(
        "# Heavy parity window gate skipped",
        "",
        "- GeneratedAtUtc: $([DateTimeOffset]::UtcNow.UtcDateTime.ToString("O"))",
        "- Reason: insufficient daily snapshot window for same-day heavy closure",
        "- RequiredWindowDays: $ParityWindowDays",
        "- AvailableDailySnapshots: $availableParityWindowDays",
        "- DailyDirectory: $parityDailyDirectory",
        "- To force strict evaluation, rerun with `-RequireParityWindow` or set `HELPER_CI_HEAVY_REQUIRE_PARITY_WINDOW=1`."
    )
    Write-HeavySkipReport -Path $resolvedParityWindowSkipReport -Lines $skipLines
    Write-Host "[CI Heavy] Generation parity window gate skipped: required $ParityWindowDays day(s), found $availableParityWindowDays in $parityDailyDirectory"
    Write-Host "[CI Heavy] Skip report: $resolvedParityWindowSkipReport"
}

Invoke-CiHeavyStep "Control-plane thresholds" {
    powershell -ExecutionPolicy Bypass -File scripts/check_backend_control_plane.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE
}

Invoke-CiHeavyStep "Latency budget" {
    powershell -ExecutionPolicy Bypass -File scripts/check_latency_budget.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE
}

Invoke-CiHeavyStep "UI workflow smoke" {
    $uiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
    powershell -ExecutionPolicy Bypass -File scripts/run_ui_workflow_smoke.ps1 -RequireConfiguredRuntime -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE -UiUrl $uiUrl
}

Invoke-CiHeavyStep "UI perf regression" {
    $uiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
    powershell -ExecutionPolicy Bypass -File scripts/ui_perf_regression.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE -UiUrl $uiUrl
}

Invoke-CiHeavyStep "Release baseline capture" {
    $uiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
    powershell -ExecutionPolicy Bypass -File scripts/baseline_capture.ps1 -ApiBaseUrl $env:HELPER_RUNTIME_SMOKE_API_BASE -UiUrl $uiUrl -SkipUiSmoke -RefreshActiveGateSnapshot
}

Write-Host "[CI Heavy] All heavy checks passed."
