[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [string]$Filter = "",
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "..\test\Helper.Runtime.CompilePath.Tests\Helper.Runtime.CompilePath.Tests.csproj"
$runId = [guid]::NewGuid().ToString("N")
$runRoot = Join-Path $PSScriptRoot "..\temp\compile-path-runs\$runId"
$resultsRoot = Join-Path $runRoot "TestResults"
$stdoutLogPath = Join-Path $runRoot "compile-path.stdout.log"
$stderrLogPath = Join-Path $runRoot "compile-path.stderr.log"

New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

$arguments = @(
    "test"
    $projectPath
    "-c"
    $Configuration
    "-m:1"
    "--results-directory"
    $resultsRoot
    "--logger"
    "console;verbosity=minimal"
)

if ($NoBuild) {
    $arguments += "--no-build"
}

if ($NoRestore) {
    $arguments += "--no-restore"
}

if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $arguments += "--filter"
    $arguments += $Filter
}

$process = $null

try {
    $process = Start-Process -FilePath "dotnet" `
        -ArgumentList $arguments `
        -WorkingDirectory (Split-Path $projectPath -Parent) `
        -NoNewWindow `
        -PassThru `
        -Wait `
        -RedirectStandardOutput $stdoutLogPath `
        -RedirectStandardError $stderrLogPath

    $process.Refresh()
    $exitCode = if ($null -ne $process.ExitCode) { [int]$process.ExitCode } else { 0 }
    if (Test-Path $stdoutLogPath) {
        Get-Content $stdoutLogPath
    }
    if (Test-Path $stderrLogPath) {
        Get-Content $stderrLogPath
    }

    if ($exitCode -ne 0) {
        throw "Compile-path tests failed with exit code $exitCode."
    }
}
finally {
    if ($process -and -not $process.HasExited) {
        taskkill /PID $process.Id /T /F | Out-Null
    }
}
