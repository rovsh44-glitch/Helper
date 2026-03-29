Set-StrictMode -Version Latest

function Import-HelperEnvFile {
    param(
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return
    }

    Get-Content -LiteralPath $Path | ForEach-Object {
        $line = $_.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) {
            return
        }

        $parts = $line -split "=", 2
        if ($parts.Count -ne 2) {
            return
        }

        $name = $parts[0].Trim()
        $value = $parts[1].Trim()
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            $existing = [Environment]::GetEnvironmentVariable($name)
            if ([string]::IsNullOrWhiteSpace($existing)) {
                [Environment]::SetEnvironmentVariable($name, $value)
            }
        }
    }
}

function Resolve-HelperDataChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$HelperRoot,
        [Parameter(Mandatory = $true)][string]$EnvName,
        [Parameter(Mandatory = $true)][string]$FallbackRelative
    )

    $configuredValue = [Environment]::GetEnvironmentVariable($EnvName)
    if (-not [string]::IsNullOrWhiteSpace($configuredValue)) {
        return [System.IO.Path]::GetFullPath($configuredValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Resolve-HelperDataRoot -HelperRoot $HelperRoot) $FallbackRelative))
}

function Resolve-HelperRoot {
    param(
        [string]$StartPath = "."
    )

    if ($env:HELPER_ROOT) {
        return [System.IO.Path]::GetFullPath($env:HELPER_ROOT)
    }

    $current = [System.IO.DirectoryInfo]::new([System.IO.Path]::GetFullPath($StartPath))
    while ($null -ne $current) {
        $candidate = $current.FullName
        $hasSolution = Test-Path (Join-Path $candidate "Helper.sln")
        $hasApiProject = Test-Path (Join-Path $candidate "src/Helper.Api/Helper.Api.csproj")
        $hasRuntimeProject = Test-Path (Join-Path $candidate "src/Helper.Runtime/Helper.Runtime.csproj")
        if ($hasSolution -or $hasApiProject -or $hasRuntimeProject) {
            return $candidate
        }

        $current = $current.Parent
    }

    return [System.IO.Path]::GetFullPath($StartPath)
}

function Resolve-HelperDataRoot {
    param(
        [Parameter(Mandatory = $true)][string]$HelperRoot
    )

    if ($env:HELPER_DATA_ROOT) {
        return [System.IO.Path]::GetFullPath($env:HELPER_DATA_ROOT)
    }

    $parent = [System.IO.Directory]::GetParent([System.IO.Path]::GetFullPath($HelperRoot))
    if ($null -eq $parent) {
        return (Join-Path $HelperRoot "HELPER_DATA")
    }

    return (Join-Path $parent.FullName "HELPER_DATA")
}

function Resolve-HelperOperatorRuntimeRoot {
    param(
        [Parameter(Mandatory = $true)][string]$HelperRoot
    )

    return [System.IO.Path]::GetFullPath((Join-Path (Resolve-HelperDataRoot -HelperRoot $HelperRoot) "runtime"))
}

function Resolve-HelperProjectsRoot {
    param(
        [Parameter(Mandatory = $true)][string]$HelperRoot
    )

    return Resolve-HelperDataChildPath -HelperRoot $HelperRoot -EnvName "HELPER_PROJECTS_ROOT" -FallbackRelative "PROJECTS"
}

function Resolve-HelperLogsRoot {
    param(
        [Parameter(Mandatory = $true)][string]$HelperRoot
    )

    return Resolve-HelperDataChildPath -HelperRoot $HelperRoot -EnvName "HELPER_LOGS_ROOT" -FallbackRelative "LOG"
}

function Resolve-HelperLibraryRoot {
    param(
        [Parameter(Mandatory = $true)][string]$HelperRoot
    )

    return Resolve-HelperDataChildPath -HelperRoot $HelperRoot -EnvName "HELPER_LIBRARY_ROOT" -FallbackRelative "library"
}

function Resolve-HelperTemplatesRoot {
    param(
        [Parameter(Mandatory = $true)][string]$HelperRoot
    )

    return Resolve-HelperDataChildPath -HelperRoot $HelperRoot -EnvName "HELPER_TEMPLATES_ROOT" -FallbackRelative "library\\forge_templates"
}

function Resolve-HelperDocRoot {
    param(
        [Parameter(Mandatory = $true)][string]$HelperRoot
    )

    return [System.IO.Path]::GetFullPath((Join-Path $HelperRoot "doc"))
}

function Resolve-HelperApiProjectRoot {
    param(
        [Parameter(Mandatory = $true)][string]$HelperRoot
    )

    return [System.IO.Path]::GetFullPath((Join-Path $HelperRoot "src\\Helper.Api"))
}

function Resolve-HelperApiPortFile {
    param(
        [Parameter(Mandatory = $true)][string]$HelperRoot
    )

    $logsRoot = Resolve-HelperLogsRoot -HelperRoot $HelperRoot
    return [System.IO.Path]::GetFullPath((Join-Path $logsRoot "API_PORT.txt"))
}

function Get-HelperPathConfig {
    param(
        [string]$WorkspaceRoot = "."
    )

    $workspace = [System.IO.Path]::GetFullPath($WorkspaceRoot)
    $helperRoot = Resolve-HelperRoot -StartPath $workspace
    Import-HelperEnvFile -Path (Join-Path $helperRoot ".env.local")
    $dataRoot = Resolve-HelperDataRoot -HelperRoot $helperRoot
    $operatorRuntimeRoot = Resolve-HelperOperatorRuntimeRoot -HelperRoot $helperRoot
    $projectsRoot = Resolve-HelperProjectsRoot -HelperRoot $helperRoot
    $logsRoot = Resolve-HelperLogsRoot -HelperRoot $helperRoot
    $libraryRoot = Resolve-HelperLibraryRoot -HelperRoot $helperRoot
    $templatesRoot = Resolve-HelperTemplatesRoot -HelperRoot $helperRoot
    $docRoot = Resolve-HelperDocRoot -HelperRoot $helperRoot
    $apiProjectRoot = Resolve-HelperApiProjectRoot -HelperRoot $helperRoot
    $apiPortFile = Resolve-HelperApiPortFile -HelperRoot $helperRoot

    return [PSCustomObject]@{
        WorkspaceRoot = $workspace
        HelperRoot = $helperRoot
        DataRoot = $dataRoot
        OperatorRuntimeRoot = $operatorRuntimeRoot
        ProjectsRoot = $projectsRoot
        LogsRoot = $logsRoot
        LibraryRoot = $libraryRoot
        TemplatesRoot = $templatesRoot
        DocRoot = $docRoot
        ApiProjectRoot = $apiProjectRoot
        ApiPortFile = $apiPortFile
    }
}

