param(
    [Parameter(Mandatory = $true)][string]$RunId,
    [string]$OutputRoot = "sandbox/active_learning"
)

$ErrorActionPreference = "Stop"

$fullOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$rawRunPath = Join-Path $fullOutputRoot ("generated_raw/{0}" -f $RunId)

Write-Host "[Rollback] Disable generation flag for current shell: HELPER_ENABLE_ACTIVE_LEARNING_GENERATION=false"
$env:HELPER_ENABLE_ACTIVE_LEARNING_GENERATION = "false"

if (Test-Path $rawRunPath) {
    Write-Host "[Rollback] Removing raw run artifacts: $rawRunPath"
    Remove-Item -Recurse -Force $rawRunPath
}
else {
    Write-Host "[Rollback] No raw artifacts found for run: $RunId"
}

Write-Host "[Rollback] Completed."
