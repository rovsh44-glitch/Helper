param(
    [Parameter(Position = 1)]
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$CliArgs
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

& (Join-Path $PSScriptRoot "invoke_helper_runtime_cli.ps1") `
    -Configuration $Configuration `
    -NoBuild:$NoBuild `
    -CliArgs $CliArgs
