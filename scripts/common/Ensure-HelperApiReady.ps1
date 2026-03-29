param(
    [string]$ApiBase = "http://127.0.0.1:5000",
    [int]$TimeoutSec = 600,
    [int]$PollIntervalMs = 2000,
    [switch]$StartLocalApiIfUnavailable,
    [switch]$EnableStrictAuditMode,
    [int]$StrictAuditMaxOutstandingTurns = 4,
    [switch]$NoBuild,
    [string]$RuntimeDir = "",
    [string]$ReportPath = "",
    [string]$RuntimeMetadataPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Resolve-HelperPaths.ps1")
. (Join-Path $PSScriptRoot "HelperRuntimeMetadata.ps1")

function Test-IsLoopbackApiBase {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl
    )

    try {
        $uri = [System.Uri]$BaseUrl
    }
    catch {
        return $false
    }

    return $uri.Host -in @("127.0.0.1", "localhost", "::1")
}

function Get-EnvFileVariableValue {
    param(
        [Parameter(Mandatory = $true)][string]$EnvFile,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not (Test-Path $EnvFile)) {
        return ""
    }

    $prefix = "$Name="
    $line = Get-Content $EnvFile | Where-Object { $_ -match ("^{0}=" -f [regex]::Escape($Name)) } | Select-Object -First 1
    if ($null -eq $line) {
        return ""
    }

    return $line.Substring($prefix.Length).Trim()
}

function Get-ReadinessProbe {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl
    )

    $normalizedBase = $BaseUrl.TrimEnd("/")
    $readinessUrl = "$normalizedBase/api/readiness"
    $healthUrl = "$normalizedBase/api/health"

    try {
        $response = Invoke-RestMethod -Uri $readinessUrl -Method Get -TimeoutSec 10
        $readyForChat = $false
        if ($null -ne $response.PSObject.Properties["readyForChat"]) {
            $readyForChat = [bool]$response.readyForChat
        }

        return [PSCustomObject]@{
            Ready = $readyForChat
            Endpoint = "readiness"
            Category = if ($readyForChat) { "ready" } else { "endpoint_reachable_not_ready" }
            Message = if ($readyForChat) { "Readiness endpoint reports readyForChat=true." } else { "Readiness endpoint is reachable but readyForChat=false." }
            Status = [string]$response.status
            Phase = [string]$response.phase
            ReadyForChat = $readyForChat
            Alerts = @($response.alerts)
        }
    }
    catch {
        $readinessMessage = $_.Exception.Message
        try {
            $healthResponse = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 10
            return [PSCustomObject]@{
                Ready = $true
                Endpoint = "health"
                Category = "health_fallback_ready"
                Message = "Health endpoint reachable; readiness endpoint unavailable or unsupported."
                Status = [string]$healthResponse.status
                Phase = ""
                ReadyForChat = $true
                Alerts = @($readinessMessage)
            }
        }
        catch {
            return [PSCustomObject]@{
                Ready = $false
                Endpoint = "none"
                Category = "process_not_reachable"
                Message = $_.Exception.Message
                Status = ""
                Phase = ""
                ReadyForChat = $false
                Alerts = @($readinessMessage, $_.Exception.Message)
            }
        }
    }
}

function Start-LocalHelperApi {
    param(
        [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$HelperRoot,
        [Parameter(Mandatory = $true)][string]$LaunchRuntimeDir,
        [switch]$SkipBuild,
        [switch]$StrictAuditMode,
        [int]$StrictAuditMaxOutstanding = 4
    )

    New-Item -ItemType Directory -Force -Path $LaunchRuntimeDir | Out-Null

    $stdoutPath = Join-Path $LaunchRuntimeDir "api_ready_stdout.log"
    $stderrPath = Join-Path $LaunchRuntimeDir "api_ready_stderr.log"
    $pidPath = Join-Path $LaunchRuntimeDir "api_ready.pid"
    $runtimeLogsRoot = Join-Path $LaunchRuntimeDir "LOG"
    $runtimeAuthKeysPath = Join-Path $LaunchRuntimeDir "auth_keys.json"
    $runtimeMetadataPath = Resolve-HelperRuntimeMetadataPath -RuntimeDir $LaunchRuntimeDir
    $auditTracePath = Join-Path $runtimeLogsRoot "post_turn_audit.trace.jsonl"

    New-Item -ItemType Directory -Force -Path $runtimeLogsRoot | Out-Null

    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -eq $pwsh) {
        $pwsh = Get-Command powershell -ErrorAction Stop
    }

    $envFile = Join-Path $HelperRoot ".env.local"
    Import-HelperEnvFile -Path $envFile
    $resolvedApiKey = $env:HELPER_API_KEY
    if ([string]::IsNullOrWhiteSpace($resolvedApiKey)) {
        $resolvedApiKey = Get-EnvFileVariableValue -EnvFile $envFile -Name "HELPER_API_KEY"
    }

    $resolvedSessionSigningKey = $env:HELPER_SESSION_SIGNING_KEY
    if ([string]::IsNullOrWhiteSpace($resolvedSessionSigningKey)) {
        $resolvedSessionSigningKey = Get-EnvFileVariableValue -EnvFile $envFile -Name "HELPER_SESSION_SIGNING_KEY"
    }

    $apiPort = 0
    try {
        $apiPort = ([System.Uri]$ApiBaseUrl).Port
    }
    catch {
        $apiPort = 0
    }

    $escapedRoot = $HelperRoot.Replace("'", "''")
    $escapedApiBase = $ApiBaseUrl.Replace("'", "''")
    $escapedApiKey = if ([string]::IsNullOrWhiteSpace($resolvedApiKey)) { "" } else { $resolvedApiKey.Replace("'", "''") }
    $escapedSessionSigningKey = if ([string]::IsNullOrWhiteSpace($resolvedSessionSigningKey)) { "" } else { $resolvedSessionSigningKey.Replace("'", "''") }
    $escapedRuntimeLogsRoot = $runtimeLogsRoot.Replace("'", "''")
    $escapedRuntimeAuthKeysPath = $runtimeAuthKeysPath.Replace("'", "''")
    $dotnetArgs = "run --project src/Helper.Api"
    if ($SkipBuild.IsPresent) {
        $dotnetArgs += " --no-build"
    }

    $commandParts = New-Object System.Collections.Generic.List[string]
    $commandParts.Add("`$env:ASPNETCORE_URLS = '$escapedApiBase'")
    if ($apiPort -gt 0) {
        $commandParts.Add("`$env:HELPER_API_PORT = '$apiPort'")
    }
    $commandParts.Add("`$env:HELPER_LOGS_ROOT = '$escapedRuntimeLogsRoot'")
    $commandParts.Add("`$env:HELPER_AUTH_KEYS_PATH = '$escapedRuntimeAuthKeysPath'")
    if ($StrictAuditMode.IsPresent) {
        $commandParts.Add("`$env:HELPER_POST_TURN_AUDIT_STRICT = 'true'")
        $commandParts.Add("`$env:HELPER_POST_TURN_AUDIT_MAX_OUTSTANDING = '$([Math]::Max(1, $StrictAuditMaxOutstanding))'")
    }
    if (-not [string]::IsNullOrWhiteSpace($escapedApiKey)) {
        $commandParts.Add("`$env:HELPER_API_KEY = '$escapedApiKey'")
    }
    if (-not [string]::IsNullOrWhiteSpace($escapedSessionSigningKey)) {
        $commandParts.Add("`$env:HELPER_SESSION_SIGNING_KEY = '$escapedSessionSigningKey'")
    }
    $commandParts.Add("Set-Location '$escapedRoot'")
    $commandParts.Add("dotnet $dotnetArgs")
    $command = $commandParts -join "; "

    $process = Start-Process `
        -FilePath $pwsh.Source `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $command) `
        -WorkingDirectory $HelperRoot `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru

    Set-Content -Path $pidPath -Value $process.Id -Encoding UTF8

    Write-HelperRuntimeMetadata -Path $runtimeMetadataPath -Payload (New-HelperRuntimeMetadataPayload `
        -ApiBase $ApiBaseUrl `
        -RuntimeRoot $LaunchRuntimeDir `
        -LogsRoot $runtimeLogsRoot `
        -AuditTracePath $auditTracePath `
        -ApiPid $process.Id `
        -LauncherMode "local_process_starting" `
        -Status "starting")

    return [PSCustomObject]@{
        ProcessId = $process.Id
        StdoutPath = $stdoutPath
        StderrPath = $stderrPath
        PidPath = $pidPath
        RuntimeRoot = $LaunchRuntimeDir
        LogsRoot = $runtimeLogsRoot
        AuditTracePath = $auditTracePath
        RuntimeMetadataPath = $runtimeMetadataPath
    }
}

function Write-ApiReadyReport {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][psobject]$Result
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Helper API Readiness")
    $lines.Add("")
    $lines.Add(('- ApiBase: `{0}`' -f $Result.ApiBase))
    $lines.Add(('- Result: `{0}`' -f $Result.Result))
    $lines.Add(('- Category: `{0}`' -f $Result.Category))
    $lines.Add(('- EndpointUsed: `{0}`' -f $Result.Endpoint))
    $lines.Add(('- ReadyForChat: `{0}`' -f $Result.ReadyForChat))
    $lines.Add(('- Status: `{0}`' -f $Result.Status))
    $lines.Add(('- Phase: `{0}`' -f $Result.Phase))
    $lines.Add(('- ElapsedSec: `{0}`' -f $Result.ElapsedSec))
    $lines.Add(('- Attempts: `{0}`' -f $Result.Attempts))
    if (-not [string]::IsNullOrWhiteSpace($Result.LaunchReason)) {
        $lines.Add(('- LaunchReason: `{0}`' -f $Result.LaunchReason))
    }
    if ($null -ne $Result.ProcessId) {
        $lines.Add(('- ProcessId: `{0}`' -f $Result.ProcessId))
    }
    if (-not [string]::IsNullOrWhiteSpace($Result.RuntimeRoot)) {
        $lines.Add(('- RuntimeRoot: `{0}`' -f $Result.RuntimeRoot))
    }
    if (-not [string]::IsNullOrWhiteSpace($Result.LogsRoot)) {
        $lines.Add(('- LogsRoot: `{0}`' -f $Result.LogsRoot))
    }
    if (-not [string]::IsNullOrWhiteSpace($Result.AuditTracePath)) {
        $lines.Add(('- AuditTracePath: `{0}`' -f $Result.AuditTracePath))
    }
    if (-not [string]::IsNullOrWhiteSpace($Result.RuntimeMetadataPath)) {
        $lines.Add(('- RuntimeMetadata: `{0}`' -f $Result.RuntimeMetadataPath))
    }
    if (-not [string]::IsNullOrWhiteSpace($Result.StdoutPath)) {
        $lines.Add(('- Stdout: `{0}`' -f $Result.StdoutPath))
    }
    if (-not [string]::IsNullOrWhiteSpace($Result.StderrPath)) {
        $lines.Add(('- Stderr: `{0}`' -f $Result.StderrPath))
    }
    $lines.Add("")
    $lines.Add("## Alerts")
    if (@($Result.Alerts).Count -eq 0) {
        $lines.Add("- none")
    }
    else {
        foreach ($alert in @($Result.Alerts)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$alert)) {
                $lines.Add(("- {0}" -f $alert))
            }
        }
    }

    $dir = [System.IO.Path]::GetDirectoryName($Path)
    if (-not [string]::IsNullOrWhiteSpace($dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }

    Set-Content -Path $Path -Value ($lines -join "`r`n") -Encoding UTF8
}

$pathConfig = Get-HelperPathConfig -WorkspaceRoot (Join-Path $PSScriptRoot "..\..")
$helperRoot = $pathConfig.HelperRoot
$runtimeRoot = if ([string]::IsNullOrWhiteSpace($RuntimeDir)) { $pathConfig.OperatorRuntimeRoot } else { [System.IO.Path]::GetFullPath($RuntimeDir) }
$resolvedRuntimeMetadataPath = Resolve-HelperRuntimeMetadataPath -RuntimeDir $runtimeRoot -ExplicitPath $RuntimeMetadataPath
$normalizedApiBase = $ApiBase.TrimEnd("/")
$startedAt = [DateTimeOffset]::UtcNow
$deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(15, $TimeoutSec))
$attempts = 0
$launch = $null
$launchReason = ""
$lastProbe = $null
$alerts = New-Object System.Collections.Generic.List[string]

while ([DateTimeOffset]::UtcNow -lt $deadline) {
    $attempts++
    $probe = Get-ReadinessProbe -BaseUrl $normalizedApiBase
    $lastProbe = $probe
    foreach ($alert in @($probe.Alerts)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$alert)) {
            $alerts.Add([string]$alert)
        }
    }

    if ($probe.Ready) {
        $runtimeLogsRoot = if ($null -eq $launch) { "" } else { [string]$launch.LogsRoot }
        $auditTracePath = if ($null -eq $launch) { "" } else { [string]$launch.AuditTracePath }
        $runtimeMetadataPathForResult = if ($null -eq $launch) { $resolvedRuntimeMetadataPath } else { [string]$launch.RuntimeMetadataPath }
        $runtimeRootForResult = if ($null -eq $launch) { $runtimeRoot } else { [string]$launch.RuntimeRoot }
        $metadataPayload = New-HelperRuntimeMetadataPayload `
            -ApiBase $normalizedApiBase `
            -RuntimeRoot $runtimeRootForResult `
            -LogsRoot $runtimeLogsRoot `
            -AuditTracePath $auditTracePath `
            -ApiPid $(if ($null -eq $launch) { $null } else { $launch.ProcessId }) `
            -LauncherMode $(if ($null -eq $launch) { "external_existing" } else { "local_process_started" }) `
            -Status "ready" `
            -Source "Ensure-HelperApiReady"
        Write-HelperRuntimeMetadata -Path $runtimeMetadataPathForResult -Payload $metadataPayload

        $result = [PSCustomObject]@{
            ApiBase = $normalizedApiBase
            Result = "PASS"
            Category = $probe.Category
            Endpoint = $probe.Endpoint
            ReadyForChat = $probe.ReadyForChat
            Status = $probe.Status
            Phase = $probe.Phase
            ElapsedSec = [math]::Round(([DateTimeOffset]::UtcNow - $startedAt).TotalSeconds, 2)
            Attempts = $attempts
            LaunchReason = $launchReason
            ProcessId = if ($null -eq $launch) { $null } else { $launch.ProcessId }
            RuntimeRoot = $runtimeRootForResult
            LogsRoot = $runtimeLogsRoot
            AuditTracePath = $auditTracePath
            RuntimeMetadataPath = $runtimeMetadataPathForResult
            StdoutPath = if ($null -eq $launch) { "" } else { $launch.StdoutPath }
            StderrPath = if ($null -eq $launch) { "" } else { $launch.StderrPath }
            Alerts = $alerts | Select-Object -Unique
        }

        if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
            Write-ApiReadyReport -Path $ReportPath -Result $result
        }

        Write-Host "[HelperApiReady] Ready via $($probe.Endpoint)." -ForegroundColor Green
        return
    }

    if (($null -eq $launch) -and $StartLocalApiIfUnavailable.IsPresent -and ($probe.Category -eq "process_not_reachable") -and (Test-IsLoopbackApiBase -BaseUrl $normalizedApiBase)) {
        $launch = Start-LocalHelperApi `
            -ApiBaseUrl $normalizedApiBase `
            -HelperRoot $helperRoot `
            -LaunchRuntimeDir $runtimeRoot `
            -SkipBuild:$NoBuild.IsPresent `
            -StrictAuditMode:$EnableStrictAuditMode.IsPresent `
            -StrictAuditMaxOutstanding $StrictAuditMaxOutstandingTurns
        $launchReason = "local_api_started_after_unreachable_probe"
        $alerts.Add("API was unreachable; started local Helper.Api process.")
    }

    Start-Sleep -Milliseconds ([Math]::Max(250, $PollIntervalMs))
}

$failureCategory = if ($null -eq $lastProbe) { "timed_out_without_probe" } elseif ($lastProbe.Category -eq "process_not_reachable") { "process_not_reachable" } else { "timed_out_waiting_for_readiness" }
$failureMessage = if ($null -eq $lastProbe) { "No readiness probe completed before timeout." } else { $lastProbe.Message }
$failureResult = [PSCustomObject]@{
    ApiBase = $normalizedApiBase
    Result = "FAIL"
    Category = $failureCategory
    Endpoint = if ($null -eq $lastProbe) { "none" } else { $lastProbe.Endpoint }
    ReadyForChat = if ($null -eq $lastProbe) { $false } else { $lastProbe.ReadyForChat }
    Status = if ($null -eq $lastProbe) { "" } else { $lastProbe.Status }
    Phase = if ($null -eq $lastProbe) { "" } else { $lastProbe.Phase }
    ElapsedSec = [math]::Round(([DateTimeOffset]::UtcNow - $startedAt).TotalSeconds, 2)
    Attempts = $attempts
    LaunchReason = $launchReason
    ProcessId = if ($null -eq $launch) { $null } else { $launch.ProcessId }
    RuntimeRoot = if ($null -eq $launch) { $runtimeRoot } else { $launch.RuntimeRoot }
    LogsRoot = if ($null -eq $launch) { "" } else { $launch.LogsRoot }
    AuditTracePath = if ($null -eq $launch) { "" } else { $launch.AuditTracePath }
    RuntimeMetadataPath = if ($null -eq $launch) { $resolvedRuntimeMetadataPath } else { $launch.RuntimeMetadataPath }
    StdoutPath = if ($null -eq $launch) { "" } else { $launch.StdoutPath }
    StderrPath = if ($null -eq $launch) { "" } else { $launch.StderrPath }
    Alerts = @($alerts + $failureMessage) | Select-Object -Unique
}

Write-HelperRuntimeMetadata -Path $failureResult.RuntimeMetadataPath -Payload (New-HelperRuntimeMetadataPayload `
    -ApiBase $normalizedApiBase `
    -RuntimeRoot $failureResult.RuntimeRoot `
    -LogsRoot $failureResult.LogsRoot `
    -AuditTracePath $failureResult.AuditTracePath `
    -ApiPid $failureResult.ProcessId `
    -LauncherMode $(if ($null -eq $launch) { "external_existing" } else { "local_process_started" }) `
    -Status "failed" `
    -Source "Ensure-HelperApiReady")

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    Write-ApiReadyReport -Path $ReportPath -Result $failureResult
}

throw "[HelperApiReady] ${failureCategory}: $failureMessage"

