[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "..\test\Helper.Runtime.Eval.Tests\Helper.Runtime.Eval.Tests.csproj"

$arguments = @(
    "test"
    $projectPath
    "-c"
    $Configuration
    "-m:1"
    "--logger"
    "console;verbosity=minimal"
)

if ($NoBuild) {
    $arguments += "--no-build"
}

if ($NoRestore) {
    $arguments += "--no-restore"
}

& dotnet @arguments
exit $LASTEXITCODE
