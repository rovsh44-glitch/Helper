[CmdletBinding()]
param(
    [string]$WorkspaceRoot = ".",
    [string]$InstallerPath = "",
    [string]$DownloadCacheDir = "",
    [string]$InstallRoot = "",
    [switch]$ForceDownload,
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

$pathConfig = Get-HelperPathConfig -WorkspaceRoot $WorkspaceRoot
$helperRoot = $pathConfig.HelperRoot
$ghostscriptTag = "gs10051"
$ghostscriptVersion = "10.05.1"
$installerFileName = "gs10051w64.exe"
$expectedSha256 = "A0E49D912D21D8193FF0CB89EF741A47B21286FBB0A0E35DD0192B0097D35766"
$sourceUrl = "https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/download/gs10051/gs10051w64.exe"

if ([string]::IsNullOrWhiteSpace($DownloadCacheDir)) {
    $DownloadCacheDir = Join-Path $pathConfig.OperatorRuntimeRoot "downloads\ghostscript"
}

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $DownloadCacheDir $installerFileName
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path $helperRoot "tools\ghostscript\$ghostscriptTag"
}

$InstallerPath = [System.IO.Path]::GetFullPath($InstallerPath)
$DownloadCacheDir = [System.IO.Path]::GetFullPath($DownloadCacheDir)
$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$ExecutablePath = Join-Path $InstallRoot "bin\gswin64c.exe"

function Assert-InstallerHash {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Ghostscript installer not found: $Path"
    }

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -ne $expectedSha256) {
        throw "Ghostscript installer checksum mismatch. Expected $expectedSha256 but found $actual."
    }

    return $actual
}

function Invoke-InstallerDownload {
    param(
        [Parameter(Mandatory = $true)][string]$SourceUrl,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $destinationDirectory = Split-Path -Parent $DestinationPath
    if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    }

    Write-Host "[GhostscriptBootstrap] Downloading $SourceUrl"
    Invoke-WebRequest -Uri $SourceUrl -OutFile $DestinationPath
}

if (($ForceDownload.IsPresent -or -not (Test-Path -LiteralPath $InstallerPath)) -and -not $VerifyOnly.IsPresent) {
    Invoke-InstallerDownload -SourceUrl $sourceUrl -DestinationPath $InstallerPath
}

$installerHash = Assert-InstallerHash -Path $InstallerPath

if ($VerifyOnly.IsPresent) {
    Write-Host "[GhostscriptBootstrap] Version=$ghostscriptVersion"
    Write-Host "[GhostscriptBootstrap] InstallerPath=$InstallerPath"
    Write-Host "[GhostscriptBootstrap] InstallerSha256=$installerHash"
    Write-Host "[GhostscriptBootstrap] InstallRoot=$InstallRoot"
    Write-Host "[GhostscriptBootstrap] ExecutablePath=$ExecutablePath"
    Write-Host ("[GhostscriptBootstrap] Installed={0}" -f (Test-Path -LiteralPath $ExecutablePath))
    return
}

if (Test-Path -LiteralPath $ExecutablePath) {
    Write-Host "[GhostscriptBootstrap] Ghostscript already available at $ExecutablePath"
    return
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $InstallRoot) | Out-Null

$installArgs = @(
    "/S",
    "/D=$InstallRoot"
)

Write-Host "[GhostscriptBootstrap] Installing Ghostscript $ghostscriptVersion into $InstallRoot"
$process = Start-Process -FilePath $InstallerPath -ArgumentList $installArgs -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0) {
    throw "Ghostscript installer exited with code $($process.ExitCode)."
}

if (-not (Test-Path -LiteralPath $ExecutablePath)) {
    throw "Ghostscript install completed without producing $ExecutablePath"
}

Write-Host "[GhostscriptBootstrap] InstalledPath=$ExecutablePath"
Write-Host "[GhostscriptBootstrap] Optional override: HELPER_PDF_VISION_GHOSTSCRIPT_PATH=$ExecutablePath"
