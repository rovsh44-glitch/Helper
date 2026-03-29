function Resolve-HelperRuntimeMetadataPath {
    param(
        [string]$RuntimeDir,
        [string]$ExplicitPath = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return [System.IO.Path]::GetFullPath($ExplicitPath)
    }

    if ([string]::IsNullOrWhiteSpace($RuntimeDir)) {
        return ""
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RuntimeDir "runtime_metadata.json"))
}

function New-HelperRuntimeMetadataPayload {
    param(
        [Parameter(Mandatory = $true)][string]$ApiBase,
        [string]$RuntimeRoot = "",
        [string]$LogsRoot = "",
        [string]$AuditTracePath = "",
        [AllowNull()][int]$ApiPid = $null,
        [string]$LauncherMode = "",
        [string]$Status = "unknown",
        [string]$Source = "launcher"
    )

    return [ordered]@{
        schemaVersion = 1
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        apiBase = $ApiBase
        runtimeRoot = $RuntimeRoot
        logsRoot = $LogsRoot
        auditTracePath = $AuditTracePath
        apiPid = $ApiPid
        launcherMode = $LauncherMode
        status = $Status
        source = $Source
        startedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }
}

function Write-HelperRuntimeMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Payload
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $directory = [System.IO.Path]::GetDirectoryName($Path)
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $Payload | ConvertTo-Json -Depth 8 | Set-Content -Path $Path -Encoding UTF8
}

function Import-HelperRuntimeMetadata {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        return $null
    }

    try {
        return Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        return $null
    }
}
