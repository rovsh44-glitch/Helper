param(
    [string]$WorkspaceRoot = ".",
    [string]$CycleRoot = "doc/pre_certification/cycles",
    [string]$StartDateUtc = "",
    [int]$WindowDays = 14,
    [switch]$ResetBeforeStart,
    [switch]$ResetDryRun
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath($WorkspaceRoot)
if (-not (Test-Path $root)) {
    throw "[PreCertInit] Workspace root not found: $root"
}

if ($WindowDays -lt 1 -or $WindowDays -gt 30) {
    throw "[PreCertInit] WindowDays must be between 1 and 30."
}

if ([string]::IsNullOrWhiteSpace($StartDateUtc)) {
    $start = (Get-Date).ToUniversalTime().Date
}
else {
    $parsedDate = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact($StartDateUtc, "yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal, [ref]$parsedDate)) {
        throw "[PreCertInit] Invalid StartDateUtc '$StartDateUtc'. Expected yyyy-MM-dd."
    }

    $start = $parsedDate.ToUniversalTime().Date
}

if ($ResetBeforeStart.IsPresent) {
    Write-Host "[PreCertInit] Resetting previous runs before cycle start..."
    $resetArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", "scripts/reset_precert_runs.ps1",
        "-WorkspaceRoot", $root
    )
    if ($ResetDryRun.IsPresent) {
        $resetArgs += "-DryRun"
    }

    powershell @resetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "[PreCertInit] Pre-reset failed."
    }
}

$startIso = $start.ToString("yyyy-MM-dd")
$cycleId = "precert_" + $startIso
$cycleRootAbs = Join-Path $root $CycleRoot
$cycleDir = Join-Path $cycleRootAbs $cycleId

New-Item -ItemType Directory -Force -Path $cycleDir | Out-Null

for ($i = 1; $i -le $WindowDays; $i++) {
    $dayDir = Join-Path $cycleDir ("day-" + $i.ToString("00"))
    New-Item -ItemType Directory -Force -Path $dayDir | Out-Null
}

$officialStart = $start.AddDays($WindowDays)
$officialDay14 = $officialStart.AddDays($WindowDays - 1)

$state = [ordered]@{
    cycleId = $cycleId
    status = "initialized"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    startDateUtc = $startIso
    windowDays = $WindowDays
    preCertFinishDateUtc = $start.AddDays($WindowDays - 1).ToString("yyyy-MM-dd")
    earliestOfficialDay01Utc = $officialStart.ToString("yyyy-MM-dd")
    earliestOfficialDay14Utc = $officialDay14.ToString("yyyy-MM-dd")
    strict = @{
        HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE = "false"
        noSoftBypassFlags = $true
    }
    progress = @{
        closedDays = 0
        lastClosedDay = ""
    }
    lastAssessment = @{
        day = ""
        result = ""
        rawWindowGate = ""
        summaryPath = ""
    }
    directories = @{
        cycleDir = $cycleDir
        parityDaily = (Join-Path $root "doc/parity_nightly/daily")
        parityHistory = (Join-Path $root "doc/parity_nightly/history")
    }
}

$statePath = Join-Path $cycleDir "PRECERT_CYCLE_STATE.json"
$state | ConvertTo-Json -Depth 8 | Set-Content -Path $statePath -Encoding UTF8

$readme = @()
$readme += "# Pre-Cert Cycle"
$readme += ""
$readme += "- CycleId: $cycleId"
$readme += "- StartDateUtc: $startIso"
$readme += "- WindowDays: $WindowDays"
$readme += "- EarliestOfficialDay01Utc: $($officialStart.ToString('yyyy-MM-dd'))"
$readme += "- EarliestOfficialDay14Utc: $($officialDay14.ToString('yyyy-MM-dd'))"
$readme += ""
$readme += "## Daily minimum sequence"
$readme += "1. scripts/run_generation_parity_gate.ps1"
$readme += "2. scripts/run_generation_parity_window_gate.ps1 -WindowDays 14 (strict)"
$readme += "3. scripts/run_smoke_generation_compile_pass.ps1 -Runs 50"
$readme += "4. scripts/run_closed_loop_predictability.ps1"
$readme += "5. scripts/run_eval_gate.ps1 + scripts/run_eval_real_model.ps1"
$readme += ""
$readme += "## Notes"
$readme += "- A strict 14-day window cannot be completed in fewer than 14 calendar dates without synthetic data."
$readme += "- Pre-cert Day 01-13 may close as GREEN_ANCHOR_PENDING when 3.1/3.3/3.4/3.5 are green and raw 3.2 is red only because the anchor window is still incomplete."
$readme += "- Pre-cert Day 14 must still close with strict 3.2 PASS and WindowComplete=true."
$readme += "- Diagnostic preview runs are allowed only under preview-specific artifact roots and preview-specific workload classes; they are never counted as closed days."
$readme += "- If any day fails, official counter must reset by policy."

$readmePath = Join-Path $cycleDir "README.md"
Set-Content -Path $readmePath -Value ($readme -join "`r`n") -Encoding UTF8

Write-Host "[PreCertInit] Cycle initialized: $cycleId" -ForegroundColor Green
Write-Host "[PreCertInit] State: $statePath"
Write-Host "[PreCertInit] Earliest official Day 01 (UTC): $($officialStart.ToString('yyyy-MM-dd'))"
