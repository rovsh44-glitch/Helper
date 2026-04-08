param(
    [string]$RepoRoot = ".",
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore,
    [switch]$StopOnFirstFailure,
    [string]$RunRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
if ([string]::IsNullOrWhiteSpace($RunRoot)) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $RunRoot = Join-Path $resolvedRepoRoot ("temp\\solution_test_matrix_{0}" -f $stamp)
}

$resolvedRunRoot = [System.IO.Path]::GetFullPath($RunRoot)
New-Item -ItemType Directory -Force -Path $resolvedRunRoot | Out-Null

$runnerScript = Join-Path $PSScriptRoot "run_solution_test_stable.ps1"
$monitorScript = Join-Path $PSScriptRoot "monitor_solution_test_stable.ps1"
$powershellCommand = Get-Command powershell.exe -ErrorAction Stop

$runnerArguments = @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", $runnerScript,
    "-RepoRoot", $resolvedRepoRoot,
    "-Configuration", $Configuration,
    "-RunRoot", $resolvedRunRoot
)
if ($NoBuild) {
    $runnerArguments += "-NoBuild"
}
if ($NoRestore) {
    $runnerArguments += "-NoRestore"
}
if ($StopOnFirstFailure) {
    $runnerArguments += "-StopOnFirstFailure"
}

$monitorArguments = @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", $monitorScript,
    "-RunRoot", $resolvedRunRoot
)

Start-Process -FilePath $powershellCommand.Source -ArgumentList $runnerArguments -WorkingDirectory $resolvedRepoRoot | Out-Null
Start-Process -FilePath $powershellCommand.Source -ArgumentList $monitorArguments -WorkingDirectory $resolvedRepoRoot | Out-Null

Write-Host ("Stable solution test matrix started in separate windows. Run root: {0}" -f $resolvedRunRoot) -ForegroundColor Green
