$ErrorActionPreference = "Stop"

$script:BackendEnvInventoryHostReady = $false
. (Join-Path $PSScriptRoot "common\DotnetBuildIsolation.ps1")

function Get-HelperRepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Ensure-BackendEnvInventoryHost {
    if ($script:BackendEnvInventoryHostReady) {
        return
    }

    $repoRoot = Get-HelperRepoRoot
    $build = Get-IsolatedDotnetBuildPlan -RepoRoot $repoRoot -HostName "config_inventory_api_host" -Configuration "Debug"
    $outputRoot = $build.OutputRoot
    $outputDll = Join-Path $outputRoot "Helper.Api.dll"
    $inventorySourcePath = Join-Path $repoRoot "src\\Helper.Api\\Backend\\Configuration\\BackendEnvironmentInventory.cs"
    $programSourcePath = Join-Path $repoRoot "src\\Helper.Api\\Program.cs"

    $needsBuild = -not (Test-Path -LiteralPath $outputDll)
    if (-not $needsBuild) {
        $outputTimestamp = (Get-Item -LiteralPath $outputDll).LastWriteTimeUtc
        $needsBuild = $outputTimestamp -lt (Get-Item -LiteralPath $inventorySourcePath).LastWriteTimeUtc -or
            $outputTimestamp -lt (Get-Item -LiteralPath $programSourcePath).LastWriteTimeUtc
    }

    if ($needsBuild) {
        $buildResult = Invoke-IsolatedDotnetBuild `
            -RepoRoot $repoRoot `
            -ProjectPath "src\\Helper.Api\\Helper.Api.csproj" `
            -HostName "config_inventory_api_host" `
            -Configuration "Debug"
        if ($buildResult.ExitCode -ne 0) {
            throw "Failed to build isolated Helper.Api config inventory host."
        }
    }

    $script:BackendEnvInventoryHostReady = $true
}

function Invoke-BackendEnvInventoryCli {
    param(
        [ValidateSet("markdown", "json", "template")]
        [string]$Format
    )

    $repoRoot = Get-HelperRepoRoot
    Ensure-BackendEnvInventoryHost
    $build = Get-IsolatedDotnetBuildPlan -RepoRoot $repoRoot -HostName "config_inventory_api_host" -Configuration "Debug"
    $apiAssemblyPath = Join-Path $build.OutputRoot "Helper.Api.dll"

    $command = @(
        "exec",
        $apiAssemblyPath,
        "--dump-config-inventory",
        $Format
    )

    Push-Location $repoRoot
    try {
        $output = & dotnet @command
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet run failed while rendering config inventory format '$Format'."
        }

        return ($output -join [Environment]::NewLine).TrimEnd() + [Environment]::NewLine
    }
    finally {
        Pop-Location
    }
}

function Get-BackendEnvDefinitions {
    $json = Invoke-BackendEnvInventoryCli -Format "json"
    return @((ConvertFrom-Json $json).definitions)
}

function Get-BackendEnvDefinitionMap {
    $map = @{}
    foreach ($definition in Get-BackendEnvDefinitions) {
        $map[$definition.Name] = $definition
    }

    return $map
}

function Get-BackendEnvReferenceMarkdown {
    return Invoke-BackendEnvInventoryCli -Format "markdown"
}

function Get-BackendLocalEnvTemplate {
    return Invoke-BackendEnvInventoryCli -Format "template"
}

function Get-BackendEnvInventoryJson {
    return Invoke-BackendEnvInventoryCli -Format "json"
}

function Get-BackendGovernedScriptFiles {
    $definitionMap = Get-BackendEnvDefinitionMap
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($definition in $definitionMap.Values) {
        foreach ($consumer in @($definition.consumers)) {
            if ($consumer -like "scripts/*" -and -not $paths.Contains($consumer)) {
                $paths.Add($consumer)
            }
        }
    }

    return @($paths | Sort-Object -Unique)
}

function Get-EnvVariableNamesFromEnvFile {
    param(
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    $names = New-Object System.Collections.Generic.List[string]
    foreach ($line in Get-Content -Path $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#')) {
            continue
        }

        $equalsIndex = $trimmed.IndexOf('=')
        if ($equalsIndex -le 0) {
            continue
        }

        $name = $trimmed.Substring(0, $equalsIndex).Trim()
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            $names.Add($name)
        }
    }

    return @($names | Sort-Object -Unique)
}

function Get-EnvVariableNamesFromScript {
    param(
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    $content = Get-Content -Path $Path -Raw
    $matches = [regex]::Matches($content, '\b(?:HELPER|VITE)_[A-Z0-9_]+\b')
    return @($matches | ForEach-Object { $_.Value } | Sort-Object -Unique)
}

function Get-BackendEnvGovernanceSnapshot {
    param(
        [string[]]$Names
    )

    $definitionMap = Get-BackendEnvDefinitionMap
    $known = New-Object System.Collections.Generic.List[string]
    $deprecated = New-Object System.Collections.Generic.List[string]
    $unknown = New-Object System.Collections.Generic.List[string]

    foreach ($name in @($Names | Sort-Object -Unique)) {
        if ($definitionMap.ContainsKey($name)) {
            $known.Add($name)
            if ([bool]$definitionMap[$name].deprecated) {
                $deprecated.Add($name)
            }
        }
        else {
            $unknown.Add($name)
        }
    }

    return [PSCustomObject]@{
        KnownVariables = @($known)
        DeprecatedVariables = @($deprecated)
        UnknownVariables = @($unknown)
    }
}

