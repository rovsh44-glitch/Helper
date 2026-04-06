param(
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore,
    [string]$Filter = "",
    [string[]]$ExtraArgs = @()
)

$ErrorActionPreference = "Stop"

$arguments = @(
    "test",
    "test\Helper.Runtime.Certification.Tests\Helper.Runtime.Certification.Tests.csproj",
    "-c", $Configuration,
    "-m:1"
)

if ($NoBuild)
{
    $arguments += "--no-build"
}

if ($NoRestore)
{
    $arguments += "--no-restore"
}

if (-not [string]::IsNullOrWhiteSpace($Filter))
{
    $arguments += "--filter"
    $arguments += $Filter
}

if ($ExtraArgs.Count -gt 0)
{
    $arguments += $ExtraArgs
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0)
{
    throw "Certification lane failed with exit code $LASTEXITCODE."
}
