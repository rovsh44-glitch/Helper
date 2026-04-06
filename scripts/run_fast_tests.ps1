param(
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore,
    [string]$Filter = "",
    [string[]]$ExtraArgs = @()
)

$ErrorActionPreference = "Stop"

$projects = @(
    "test\Helper.Runtime.Tests\Helper.Runtime.Tests.csproj",
    "test\Helper.Runtime.Api.Tests\Helper.Runtime.Api.Tests.csproj",
    "test\Helper.Runtime.Browser.Tests\Helper.Runtime.Browser.Tests.csproj"
)

foreach ($project in $projects)
{
    $arguments = @(
        "test",
        $project,
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
        throw "Fast lane failed for $project with exit code $LASTEXITCODE."
    }
}
