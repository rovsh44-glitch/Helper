param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore,
    [switch]$StopOnFirstFailure,
    [int]$MaxDurationSec = 0,
    [int]$RefreshSec = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedProjectPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
$projectName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedProjectPath)
$runRoot = Join-Path $repoRoot ("temp\{0}_file_runs_{1}" -f $projectName, (Get-Date -Format "yyyy-MM-dd_HH-mm-ss"))

$runnerArguments = @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "run_test_project_by_file.ps1"),
    "-ProjectPath", $ProjectPath,
    "-Configuration", $Configuration,
    "-RunRoot", $runRoot,
    "-MaxDurationSec", $MaxDurationSec
)
if ($NoBuild) { $runnerArguments += "-NoBuild" }
if ($NoRestore) { $runnerArguments += "-NoRestore" }
if ($StopOnFirstFailure) { $runnerArguments += "-StopOnFirstFailure" }

$runnerProcess = Start-Process -FilePath "powershell.exe" -ArgumentList $runnerArguments -WorkingDirectory $repoRoot -PassThru

$monitorArguments = @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "monitor_test_project_by_file.ps1"),
    "-RunRoot", $runRoot,
    "-RefreshSec", $RefreshSec
)
Start-Process -FilePath "powershell.exe" -ArgumentList $monitorArguments -WorkingDirectory $repoRoot | Out-Null

Write-Host ("RunRoot: {0}" -f $runRoot) -ForegroundColor Cyan
Write-Host ("Runner PID: {0}" -f $runnerProcess.Id) -ForegroundColor Cyan
