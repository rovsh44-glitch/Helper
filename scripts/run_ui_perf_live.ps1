param(
    [string]$HelperRoot = "",
    [string]$DataRoot = "",
    [int]$ApiPort = 5000,
    [int]$UiPort = 5173
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
$apiBaseUrl = "http://localhost:$ApiPort"
$uiUrl = "http://127.0.0.1:$UiPort"
$envFile = Join-Path $HelperRoot ".env.local"
$logRoot = Join-Path $DataRoot "LOG"

function Stop-HelperProcesses {
    Get-Process Helper.Api -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-CimInstance Win32_Process |
        Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'Helper\\.Api' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Get-CimInstance Win32_Process |
        Where-Object { $_.Name -eq 'node.exe' -and $_.CommandLine -match 'vite' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
}

function Wait-ApiReady {
    param([string]$Url)

    for ($i = 0; $i -lt 120; $i++) {
        try {
            $snapshot = Invoke-RestMethod -Method Get -Uri "$Url/api/readiness" -TimeoutSec 3
            if ([bool]$snapshot.readyForChat) {
                return
            }
        } catch {
        }

        Start-Sleep -Milliseconds 500
    }

    throw "API readiness timed out."
}

function Wait-UiReady {
    param([string]$Url)

    for ($i = 0; $i -lt 120; $i++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 3
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return
            }
        } catch {
        }

        Start-Sleep -Milliseconds 500
    }

    throw "UI readiness timed out."
}

Import-HelperEnvFile -Path $envFile
$env:HELPER_ROOT = $HelperRoot
$env:HELPER_DATA_ROOT = $DataRoot
$env:HELPER_PROJECTS_ROOT = Join-Path $DataRoot "PROJECTS"
$env:HELPER_LIBRARY_ROOT = Join-Path $DataRoot "library"
$env:HELPER_LOGS_ROOT = Join-Path $DataRoot "LOG"
$env:HELPER_TEMPLATES_ROOT = Join-Path $env:HELPER_LIBRARY_ROOT "forge_templates"
$env:HELPER_MODEL_WARMUP_MODE = "minimal"
$env:HELPER_MODEL_PREFLIGHT_ENABLED = "false"

New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
Stop-HelperProcesses

$apiProcess = $null
$uiProcess = $null

try {
    $apiProcess = Start-Process -FilePath "dotnet" -ArgumentList "run" -WorkingDirectory (Join-Path $HelperRoot "src\Helper.Api") -RedirectStandardOutput (Join-Path $logRoot "ui_perf_live_api_out.log") -RedirectStandardError (Join-Path $logRoot "ui_perf_live_api_err.log") -PassThru
    Wait-ApiReady -Url $apiBaseUrl

    $uiProcess = Start-Process -FilePath "cmd.exe" -ArgumentList "/c", "npm run dev -- --host 127.0.0.1 --port $UiPort --strictPort" -WorkingDirectory $HelperRoot -RedirectStandardOutput (Join-Path $logRoot "ui_perf_live_ui_out.log") -RedirectStandardError (Join-Path $logRoot "ui_perf_live_ui_err.log") -PassThru
    Wait-UiReady -Url $uiUrl

    powershell -ExecutionPolicy Bypass -File (Join-Path $HelperRoot "scripts\ui_perf_regression.ps1") -ApiBaseUrl $apiBaseUrl -UiUrl $uiUrl
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    foreach ($proc in @($apiProcess, $uiProcess)) {
        if ($null -ne $proc -and -not $proc.HasExited) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
    }

    Stop-HelperProcesses
}

