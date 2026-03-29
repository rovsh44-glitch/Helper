param(
    [string]$Destination = "",
    [switch]$InitGit = $true,
    [string]$InitialBranch = "main"
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\PublicLaunchBoundaryCommon.ps1")

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $repoRoot "showcase_repo"
}

$resolvedDestination = [System.IO.Path]::GetFullPath($Destination)
if ($resolvedDestination.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Destination must not be the repository root."
}

if (Test-Path -LiteralPath $resolvedDestination) {
    $existingEntries = @(Get-ChildItem -LiteralPath $resolvedDestination -Force -ErrorAction SilentlyContinue)
    if ($existingEntries.Count -gt 0) {
        throw "Destination '$resolvedDestination' already exists and is not empty."
    }
} else {
    New-Item -ItemType Directory -Path $resolvedDestination -Force | Out-Null
}

$candidateFiles = @(
    & git -C $repoRoot -c core.quotePath=false ls-files --cached --others --exclude-standard
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique

$publicSafeFiles = foreach ($path in $candidateFiles) {
    $classification = Get-PublicShowcasePathClassification -RelativePath $path
    if ($classification.category -eq "public_safe") {
        ($path -replace '\\', '/')
    }
}

if (@($publicSafeFiles).Count -eq 0) {
    throw "No public-safe files were found in the current candidate set."
}

foreach ($relativePath in $publicSafeFiles) {
    $sourcePath = Join-Path $repoRoot ($relativePath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Public-safe path '$relativePath' does not exist in the working tree."
    }

    $destinationPath = Join-Path $resolvedDestination ($relativePath -replace '/', '\')
    $destinationDirectory = Split-Path -Parent $destinationPath
    if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
}

if ($InitGit) {
    & git -C $resolvedDestination init -b $InitialBranch | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "git init failed for '$resolvedDestination'."
    }
}

Write-Host "[ShowcaseExport] Exported $(@($publicSafeFiles).Count) public-safe files to $resolvedDestination"
if ($InitGit) {
    Write-Host "[ShowcaseExport] Initialized standalone git repository on branch '$InitialBranch'"
}

@($publicSafeFiles) | ForEach-Object { Write-Host " - $_" }
