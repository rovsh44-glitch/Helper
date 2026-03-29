param(
    [string]$HelperRoot = "",
    [string]$DataRoot = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

$pathConfig = Get-HelperPathConfig -WorkspaceRoot (Join-Path $PSScriptRoot "..")

if ([string]::IsNullOrWhiteSpace($HelperRoot)) {
    $HelperRoot = $pathConfig.HelperRoot
}

if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    $DataRoot = $pathConfig.DataRoot
}

$HelperRoot = [System.IO.Path]::GetFullPath($HelperRoot)
$DataRoot = [System.IO.Path]::GetFullPath($DataRoot)
$LogsRoot = Join-Path $DataRoot "LOG"

New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
New-Item -ItemType Directory -Force -Path $LogsRoot | Out-Null

function Get-ExtendedPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ($Path.StartsWith("\\?\")) {
        return $Path
    }

    return "\\?\$Path"
}

function Remove-DirectoryExact {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (-not [System.IO.Directory]::Exists((Get-ExtendedPath -Path $Path))) {
        return
    }

    [System.IO.Directory]::Delete((Get-ExtendedPath -Path $Path), $true)
}

function Get-ExactRootDirectoryPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $match = Get-ChildItem -LiteralPath $Parent -Force -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq $Name } |
        Select-Object -First 1

    if ($null -eq $match) {
        return $null
    }

    return $match.FullName
}

function Merge-Directory {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        $target = Join-Path $Destination $_.Name
        if ($_.PSIsContainer) {
            Merge-Directory -Source $_.FullName -Destination $target
            if ((Test-Path -LiteralPath $_.FullName) -and ((Get-ChildItem -LiteralPath $_.FullName -Force | Measure-Object).Count -eq 0)) {
                Remove-DirectoryExact -Path $_.FullName
            }
            return
        }

        if (Test-Path -LiteralPath $target) {
            $sameFile = $false
            try {
                $sameFile = (Get-FileHash -LiteralPath $_.FullName).Hash -eq (Get-FileHash -LiteralPath $target).Hash
            } catch {
                $sameFile = $false
            }

            if ($sameFile) {
                Remove-Item -LiteralPath $_.FullName -Force
                return
            }

            $base = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
            $ext = [System.IO.Path]::GetExtension($_.Name)
            $target = Join-Path $Destination ("{0}__migrated_{1}{2}" -f $base, (Get-Date -Format "yyyyMMddHHmmssfff"), $ext)
        }

        Move-Item -LiteralPath $_.FullName -Destination $target
    }

    if ((Get-ChildItem -LiteralPath $Source -Force | Measure-Object).Count -eq 0) {
        Remove-DirectoryExact -Path $Source
    }
}

function Move-RootDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$DestinationName = $Name
    )

    $source = Get-ExactRootDirectoryPath -Parent $HelperRoot -Name $Name
    if ([string]::IsNullOrWhiteSpace($source)) {
        return
    }

    $destination = Join-Path $DataRoot $DestinationName
    Write-Host "[DataRoot] Moving directory '$source' -> '$destination'"
    Merge-Directory -Source $source -Destination $destination
}

function Move-RootFile {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory
    )

    $source = Join-Path $HelperRoot $Name
    if (-not (Test-Path -LiteralPath $source)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
    $destination = Join-Path $DestinationDirectory $Name

    if (Test-Path -LiteralPath $destination) {
        $destination = Join-Path $DestinationDirectory ("{0}__migrated_{1}{2}" -f [System.IO.Path]::GetFileNameWithoutExtension($Name), (Get-Date -Format "yyyyMMddHHmmssfff"), [System.IO.Path]::GetExtension($Name))
    }

    Write-Host "[DataRoot] Moving file '$source' -> '$destination'"
    Move-Item -LiteralPath $source -Destination $destination
}

function Move-LiteralFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
    $sourceName = Split-Path $Source -Leaf
    $destination = Join-Path $DestinationDirectory $sourceName

    if (Test-Path -LiteralPath $destination) {
        $destination = Join-Path $DestinationDirectory ("{0}__migrated_{1}{2}" -f [System.IO.Path]::GetFileNameWithoutExtension($sourceName), (Get-Date -Format "yyyyMMddHHmmssfff"), [System.IO.Path]::GetExtension($sourceName))
    }

    Write-Host "[DataRoot] Moving file '$Source' -> '$destination'"
    Move-Item -LiteralPath $Source -Destination $destination
}

Move-RootDirectory -Name "library"
Move-RootDirectory -Name "ocr_venv"
Move-RootDirectory -Name "logs"
Move-RootDirectory -Name "LOG"
Move-RootDirectory -Name "generated_projects"
Move-RootDirectory -Name "output_projects"
Move-RootDirectory -Name "tmp_tpl_build"
Move-RootDirectory -Name "bin"
Move-RootDirectory -Name "obj"
Move-RootDirectory -Name "runtime" -DestinationName "runtime"
Move-RootDirectory -Name "tmp_pdfepub_smoke" -DestinationName "runtime\tmp_pdfepub_smoke"

$projectsDestination = Join-Path $DataRoot "PROJECTS"
Move-RootDirectory -Name "PROJECTS" -DestinationName "PROJECTS"
Move-RootDirectory -Name "PROJECTS " -DestinationName "PROJECTS"

Move-RootFile -Name "indexing_queue.json" -DestinationDirectory $DataRoot
Move-RootFile -Name "auth_keys.json" -DestinationDirectory $DataRoot
Move-RootFile -Name "generation_runs.jsonl" -DestinationDirectory $DataRoot
Move-RootFile -Name "evolution_state.json" -DestinationDirectory $DataRoot

$apiProjectsRoot = Join-Path $HelperRoot "src\Helper.Api\PROJECTS"
if (Test-Path -LiteralPath $apiProjectsRoot) {
    Write-Host "[DataRoot] Moving directory '$apiProjectsRoot' -> '$projectsDestination'"
    Merge-Directory -Source $apiProjectsRoot -Destination $projectsDestination
}

$apiDebugLog = Join-Path $HelperRoot "src\Helper.Api\api_debug.log"
if (Test-Path -LiteralPath $apiDebugLog) {
    Move-LiteralFile -Source $apiDebugLog -DestinationDirectory $LogsRoot
}

Get-ChildItem -LiteralPath $HelperRoot -File -Force |
    Where-Object { $_.Extension -in @(".log", ".pid") } |
    ForEach-Object {
        Move-RootFile -Name $_.Name -DestinationDirectory $LogsRoot
    }

$queuePath = Join-Path $DataRoot "indexing_queue.json"
if (Test-Path -LiteralPath $queuePath) {
    $queueRaw = Get-Content -LiteralPath $queuePath -Raw
    $queueRaw = $queueRaw.Replace((Join-Path $HelperRoot "library"), (Join-Path $DataRoot "library"))
    $queueRaw = $queueRaw.Replace((Join-Path $HelperRoot "PROJECTS"), (Join-Path $DataRoot "PROJECTS"))
    $queueRaw = $queueRaw.Replace((Join-Path $HelperRoot "PROJECTS "), (Join-Path $DataRoot "PROJECTS"))
    Set-Content -LiteralPath $queuePath -Value $queueRaw -Encoding UTF8
}

$trailingProjects = Get-ExactRootDirectoryPath -Parent $HelperRoot -Name "PROJECTS "
if (-not [string]::IsNullOrWhiteSpace($trailingProjects)) {
    Remove-DirectoryExact -Path $trailingProjects
}

Write-Host "[DataRoot] Migration complete."
Write-Host "[DataRoot] HELPER_DATA_ROOT=$DataRoot"

