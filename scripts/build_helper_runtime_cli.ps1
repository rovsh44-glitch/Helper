param(
    [string]$Configuration = "Debug",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalBuildArgs
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$workspaceRoot = Split-Path -Parent $scriptRoot
$stopScriptPath = Join-Path $scriptRoot "stop_running_helper_runtime_cli.ps1"
. (Join-Path $scriptRoot "common\DotnetBuildIsolation.ps1")

& $stopScriptPath

$buildResult = Invoke-IsolatedDotnetBuild `
    -RepoRoot $workspaceRoot `
    -ProjectPath "src\Helper.Runtime.Cli\Helper.Runtime.Cli.csproj" `
    -HostName "helper_runtime_cli_host" `
    -Configuration $Configuration `
    -AdditionalBuildArgs $AdditionalBuildArgs

exit $buildResult.ExitCode
