param(
    [Parameter(Position = 1)]
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$CliArgs
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
. (Join-Path $scriptRoot "common\DotnetBuildIsolation.ps1")

$projectPath = "src\Helper.Runtime.Cli\Helper.Runtime.Cli.csproj"
$build = Get-IsolatedDotnetBuildPlan -RepoRoot $repoRoot -HostName "helper_runtime_cli_host" -Configuration $Configuration
$cliDllPath = Join-Path $build.OutputRoot "Helper.Runtime.Cli.dll"

if ((-not $NoBuild.IsPresent) -or (-not (Test-Path -LiteralPath $cliDllPath))) {
    $buildResult = Invoke-IsolatedDotnetBuild `
        -RepoRoot $repoRoot `
        -ProjectPath $projectPath `
        -HostName "helper_runtime_cli_host" `
        -Configuration $Configuration
    if ($buildResult.ExitCode -ne 0) {
        throw "[HelperRuntimeCli] Build failed."
    }
}

if (-not (Test-Path -LiteralPath $cliDllPath)) {
    throw "[HelperRuntimeCli] Built CLI assembly missing: $cliDllPath"
}

Push-Location $repoRoot
try {
    & dotnet exec $cliDllPath @CliArgs
    exit $(if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 })
}
finally {
    Pop-Location
}
