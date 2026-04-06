param(
    [Parameter(Mandatory = $true)][string]$FilePath,
    [string]$ProjectPath = "",
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore,
    [switch]$ListOnly,
    [switch]$EmitJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-TestProjectPath {
    param(
        [Parameter(Mandatory = $true)][string]$ResolvedFilePath,
        [string]$ExplicitProjectPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitProjectPath)) {
        return [System.IO.Path]::GetFullPath($ExplicitProjectPath)
    }

    $directory = Split-Path -Parent $ResolvedFilePath
    while (-not [string]::IsNullOrWhiteSpace($directory)) {
        $candidateProjects = @(Get-ChildItem -LiteralPath $directory -Filter *.csproj -File -ErrorAction SilentlyContinue)
        if ($candidateProjects.Count -eq 1) {
            return $candidateProjects[0].FullName
        }

        if ($candidateProjects.Count -gt 1) {
            $matching = @($candidateProjects | Where-Object { $_.BaseName -eq (Split-Path -Leaf $directory) })
            if ($matching.Count -ge 1) {
                return $matching[0].FullName
            }
        }

        $parent = Split-Path -Parent $directory
        if ($parent -eq $directory) {
            break
        }

        $directory = $parent
    }

    throw ("Unable to infer test project for file: {0}" -f $ResolvedFilePath)
}

function Get-TestClassNamesFromFile {
    param([Parameter(Mandatory = $true)][string]$ResolvedFilePath)

    $content = Get-Content -LiteralPath $ResolvedFilePath -Raw -Encoding utf8
    $sanitizedContent = [regex]::Replace($content, '(?s)\$*""".*?"""', '""')
    $matches = [regex]::Matches($sanitizedContent, '(?m)^\s*(?:public|internal)?\s*(?:(?:sealed|abstract|partial)\s+)*class\s+([A-Za-z_][A-Za-z0-9_]*)')
    $classNames = @(
        $matches |
            ForEach-Object { $_.Groups[1].Value } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique
    )

    if ($classNames.Count -eq 0) {
        return @()
    }

    $preferred = @($classNames | Where-Object { $_ -match '(?i)tests?$' })
    if ($preferred.Count -gt 0) {
        return $preferred
    }

    return $classNames
}

$resolvedFilePath = [System.IO.Path]::GetFullPath($FilePath)
if (-not (Test-Path -LiteralPath $resolvedFilePath)) {
    throw ("Test file not found: {0}" -f $resolvedFilePath)
}

$resolvedProjectPath = Resolve-TestProjectPath -ResolvedFilePath $resolvedFilePath -ExplicitProjectPath $ProjectPath
$classNames = @(Get-TestClassNamesFromFile -ResolvedFilePath $resolvedFilePath)

if ($classNames.Count -eq 0) {
    if ($EmitJson) {
        [pscustomobject]@{
            resolvedFilePath = $resolvedFilePath
            resolvedProjectPath = $resolvedProjectPath
            hasTests = $false
            classNames = @()
            filter = ""
        } | ConvertTo-Json -Depth 5
    }
    else {
        Write-Host ("Test file: {0}" -f $resolvedFilePath) -ForegroundColor Cyan
        Write-Host ("Project: {0}" -f $resolvedProjectPath) -ForegroundColor Cyan
        Write-Host "No test classes found in file. Skipping." -ForegroundColor DarkYellow
    }
    exit 0
}

$filter = ($classNames | ForEach-Object { "FullyQualifiedName~{0}" -f $_ }) -join "|"

if ($EmitJson) {
    [pscustomobject]@{
        resolvedFilePath = $resolvedFilePath
        resolvedProjectPath = $resolvedProjectPath
        hasTests = $true
        classNames = @($classNames)
        filter = $filter
    } | ConvertTo-Json -Depth 5
    exit 0
}

Write-Host ("Test file: {0}" -f $resolvedFilePath) -ForegroundColor Cyan
Write-Host ("Project: {0}" -f $resolvedProjectPath) -ForegroundColor Cyan
Write-Host ("Classes: {0}" -f ($classNames -join ", ")) -ForegroundColor Cyan
Write-Host ("Filter: {0}" -f $filter) -ForegroundColor DarkCyan

if ($ListOnly) {
    return
}

$dotnetArgs = @("test", $resolvedProjectPath, "-c", $Configuration, "-m:1", "--filter", $filter)
if ($NoBuild) {
    $dotnetArgs += "--no-build"
}
if ($NoRestore) {
    $dotnetArgs += "--no-restore"
}

Write-Host ("Command: dotnet {0}" -f ($dotnetArgs -join " ")) -ForegroundColor Yellow
& dotnet @dotnetArgs
exit $LASTEXITCODE
