param(
    [string]$EnvReferencePath = "doc/config/ENV_REFERENCE.md",
    [string]$EnvInventoryJsonPath = "doc/config/ENV_INVENTORY.json",
    [string]$EnvExamplePath = ".env.local.example",
    [string]$LocalEnvPath = ""
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "env_inventory_common.ps1")

$repoRoot = Get-HelperRepoRoot
$failures = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$definitionMap = Get-BackendEnvDefinitionMap

function Normalize-GeneratedContent {
    param(
        [string]$RelativePath,
        [string]$Content
    )

    if ($RelativePath -eq "doc/config/ENV_REFERENCE.md") {
        return ($Content -replace 'Generated: `[^`]+`', 'Generated: `<normalized>`').Trim()
    }

    if ($RelativePath -eq "doc/config/ENV_INVENTORY.json") {
        return ($Content -replace '"generatedAtUtc"\s*:\s*"[^"]+"', '"generatedAtUtc":"<normalized>"').Trim()
    }

    return $Content.Trim()
}

function Test-GeneratedFile {
    param(
        [string]$RelativePath,
        [string]$ExpectedContent
    )

    $fullPath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        $failures.Add("Missing generated config artifact: $RelativePath")
        return
    }

    $actual = Get-Content -Path $fullPath -Raw
    if ((Normalize-GeneratedContent -RelativePath $RelativePath -Content $actual) -ne (Normalize-GeneratedContent -RelativePath $RelativePath -Content $ExpectedContent)) {
        $failures.Add("$RelativePath is stale. Run scripts/generate_env_reference.ps1.")
    }
}

Test-GeneratedFile -RelativePath $EnvReferencePath -ExpectedContent (Get-BackendEnvReferenceMarkdown)
Test-GeneratedFile -RelativePath $EnvInventoryJsonPath -ExpectedContent (Get-BackendEnvInventoryJson)
Test-GeneratedFile -RelativePath $EnvExamplePath -ExpectedContent (Get-BackendLocalEnvTemplate)

$envExampleNames = Get-EnvVariableNamesFromEnvFile -Path (Join-Path $repoRoot $EnvExamplePath)
$envExampleSnapshot = Get-BackendEnvGovernanceSnapshot -Names $envExampleNames
foreach ($deprecated in @($envExampleSnapshot.DeprecatedVariables)) {
    $replacement = if ($definitionMap.ContainsKey($deprecated) -and -not [string]::IsNullOrWhiteSpace($definitionMap[$deprecated].Replacement)) {
        " Use $($definitionMap[$deprecated].Replacement) instead."
    } else {
        ""
    }

    $failures.Add(".env.local.example contains deprecated variable $deprecated.$replacement")
}
foreach ($unknown in @($envExampleSnapshot.UnknownVariables)) {
    $failures.Add(".env.local.example contains unknown variable $unknown.")
}

$localEnvPath = if ([string]::IsNullOrWhiteSpace($LocalEnvPath)) {
    Join-Path $repoRoot ".env.local"
}
else {
    $LocalEnvPath
}

$localEnvNames = @(Get-EnvVariableNamesFromEnvFile -Path $localEnvPath)
if ($localEnvNames.Count -gt 0) {
    $localSnapshot = Get-BackendEnvGovernanceSnapshot -Names $localEnvNames
    foreach ($deprecated in @($localSnapshot.DeprecatedVariables)) {
        $replacement = if ($definitionMap.ContainsKey($deprecated) -and -not [string]::IsNullOrWhiteSpace($definitionMap[$deprecated].Replacement)) {
            " Use $($definitionMap[$deprecated].Replacement) instead."
        } else {
            ""
        }

        $warnings.Add(".env.local contains deprecated variable $deprecated.$replacement")
    }
    foreach ($unknown in @($localSnapshot.UnknownVariables)) {
        $warnings.Add(".env.local contains unknown variable $unknown.")
    }
}

$governedScriptPaths = Get-BackendGovernedScriptFiles
foreach ($relativePath in $governedScriptPaths) {
    $scriptPath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $scriptPath)) {
        $failures.Add("Governed script not found: $relativePath")
        continue
    }

    foreach ($name in Get-EnvVariableNamesFromScript -Path $scriptPath) {
        if (-not $definitionMap.ContainsKey($name)) {
            $failures.Add("$relativePath references unknown governed variable $name.")
            continue
        }

        if ($definitionMap[$name].Deprecated) {
            $warnings.Add("$relativePath references deprecated variable $name.")
        }
    }
}

if ($warnings.Count -gt 0) {
    Write-Host "[Config] Governance warnings:" -ForegroundColor Yellow
    $warnings | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" -ForegroundColor Yellow }
}

if ($failures.Count -gt 0) {
    Write-Host "[Config] Governance check failed:" -ForegroundColor Red
    $failures | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "[Config] Environment inventory and governance checks passed." -ForegroundColor Green
