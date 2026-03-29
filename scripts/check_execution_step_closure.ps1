param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Join-RepoPath {
    param([string]$RelativePath)
    return Join-Path $repoRoot $RelativePath
}

function Read-JsonFile {
    param([string]$RelativePath)

    $fullPath = Join-RepoPath $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "[Execution Closure] Missing required JSON file: $RelativePath"
    }

    return Get-Content -Path $fullPath -Raw | ConvertFrom-Json
}

$failures = New-Object System.Collections.Generic.List[string]

$requiredFiles = @(
    "doc/archive/comparative/HELPER_EXECUTION_STEP_CLOSURE_LFL300_2026-03-23.json",
    "doc/archive/comparative/HELPER_EXECUTION_STEP_CLOSURE_LFL300_2026-03-23.md",
    "doc/archive/comparative/HELPER_EXECUTION_ORDER_TABLE_LFL300_2026-03-22.md",
    "doc/archive/comparative/HELPER_EXECUTION_DASHBOARD_LFL300_2026-03-22.md"
)

foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-RepoPath $relativePath))) {
        $failures.Add("Missing required execution-closure artifact: $relativePath")
    }
}

$registry = $null
try {
    $registry = Read-JsonFile "doc/archive/comparative/HELPER_EXECUTION_STEP_CLOSURE_LFL300_2026-03-23.json"
}
catch {
    $failures.Add($_.Exception.Message)
}

$expectedStepIds = 1..16 | ForEach-Object { "STEP-{0:000}" -f $_ }

if ($null -ne $registry) {
    if ($registry.status -ne "ALL_STEPS_COMPLETED_AND_PROVED") {
        $failures.Add("Execution step closure registry status must be ALL_STEPS_COMPLETED_AND_PROVED.")
    }

    $actualSteps = @($registry.steps)
    if ($actualSteps.Count -ne 16) {
        $failures.Add("Execution step closure registry must contain exactly 16 steps.")
    }

    $actualIds = @($actualSteps | ForEach-Object { $_.id })
    foreach ($expectedId in $expectedStepIds) {
        if ($actualIds -notcontains $expectedId) {
            $failures.Add("Execution step closure registry is missing $expectedId.")
        }
    }

    foreach ($step in $actualSteps) {
        if ($step.status -ne "completed") {
            $failures.Add("$($step.id) must have status=completed in execution step closure registry.")
        }

        if ($null -eq $step.codeRefs -or @($step.codeRefs).Count -lt 1) {
            $failures.Add("$($step.id) must have at least one codeRef.")
        }

        if ($null -eq $step.proofRefs -or @($step.proofRefs).Count -lt 1) {
            $failures.Add("$($step.id) must have at least one proofRef.")
        }

        if ($null -eq $step.docRefs -or @($step.docRefs).Count -lt 1) {
            $failures.Add("$($step.id) must have at least one docRef.")
        }
    }
}

$orderTablePath = Join-RepoPath "doc/archive/comparative/HELPER_EXECUTION_ORDER_TABLE_LFL300_2026-03-22.md"
if (Test-Path -LiteralPath $orderTablePath) {
    $orderText = Get-Content -Path $orderTablePath -Raw
    foreach ($expectedId in $expectedStepIds) {
        $pattern = '\|\s+`' + [regex]::Escape($expectedId) + '`\s+\|.*?\|\s+`completed`\s+—'
        if ($orderText -notmatch $pattern) {
            $failures.Add("Execution order table must mark $expectedId as completed.")
        }
    }
}

$dashboardPath = Join-RepoPath "doc/archive/comparative/HELPER_EXECUTION_DASHBOARD_LFL300_2026-03-22.md"
if (Test-Path -LiteralPath $dashboardPath) {
    $dashboardText = Get-Content -Path $dashboardPath -Raw
    foreach ($expectedId in $expectedStepIds) {
        $pattern = '\|\s+`Wave\s+[1-5]`\s+\|\s+`' + [regex]::Escape($expectedId) + '`\s+[^|]*\|\s+`completed`\s+\|'
        if ($dashboardText -notmatch $pattern) {
            $failures.Add("Execution dashboard must mark $expectedId as completed.")
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "[Execution Closure] Check failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "[Execution Closure] STEP-001..STEP-016 are symmetrically marked and proved." -ForegroundColor Green
