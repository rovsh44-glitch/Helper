param(
    [Parameter(Mandatory = $true)][string]$SourceValidatedProjectPath,
    [Parameter(Mandatory = $true)][string]$TargetProjectPath,
    [switch]$Approve
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)

    Write-Host "[Promotion] $Name..."
    $global:LASTEXITCODE = 0
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "[Promotion] Step failed: $Name (exit code: $LASTEXITCODE)."
    }
}

$source = [System.IO.Path]::GetFullPath($SourceValidatedProjectPath)
$target = [System.IO.Path]::GetFullPath($TargetProjectPath)

if (-not (Test-Path $source -PathType Container)) {
    throw "[Promotion] Source does not exist: $source"
}

if ($source -notmatch "generated_validated") {
    throw "[Promotion] Source must be inside generated_validated."
}

if ($target -notmatch "\\src\\") {
    throw "[Promotion] Target must be in src/**."
}

Invoke-Step "Contract + unit tests" {
    dotnet test Helper.sln --no-build
}

Invoke-Step "Security scan" {
    powershell -ExecutionPolicy Bypass -File scripts/secret_scan.ps1 -ScanMode repo
}

$diffDir = Join-Path (Get-Location) "doc"
New-Item -ItemType Directory -Force -Path $diffDir | Out-Null
$diffPath = Join-Path $diffDir ("generation_promote_diff_{0}.patch" -f (Get-Date -Format "yyyyMMdd_HHmmss"))

Write-Host "[Promotion] Generating diff: $diffPath"
cmd /c "git diff --no-index \"$target\" \"$source\" > \"$diffPath\""

if (-not $Approve) {
    Write-Host "[Promotion] Dry-run complete. Diff generated: $diffPath"
    Write-Host "[Promotion] Re-run with -Approve to copy artifacts."
    exit 0
}

if ($env:HELPER_ENABLE_AUTOPROMOTE_TO_RUNTIME -ne "true") {
    throw "[Promotion] HELPER_ENABLE_AUTOPROMOTE_TO_RUNTIME must be true for approved promotion."
}

if (Test-Path $target) {
    Remove-Item -Recurse -Force $target
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $target -Recurse -Force

Write-Host "[Promotion] Promotion completed: $source -> $target"
Write-Host "[Promotion] Diff report: $diffPath"

