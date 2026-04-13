param(
    [string]$Target = "Helper.sln",
    [ValidateSet("direct", "all")][string]$AuditMode = "all",
    [ValidateSet("low", "moderate", "high", "critical")][string]$AuditLevel = "low",
    [ValidateSet("strict-online", "best-effort-local")][string]$ExecutionMode = "best-effort-local",
    [switch]$ClearProxyEnvironment,
    [string]$ReportPath = "",
    [string]$SimulatedRestoreOutputPath = "",
    [int]$SimulatedRestoreExitCode = -2147483648,
    [string]$SimulatedListOutputPath = "",
    [int]$SimulatedListExitCode = -2147483648
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedTarget = if ([System.IO.Path]::IsPathRooted($Target)) {
    $Target
}
else {
    Join-Path $repoRoot $Target
}
$resolvedReportPath = if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    ""
}
elseif ([System.IO.Path]::IsPathRooted($ReportPath)) {
    $ReportPath
}
else {
    Join-Path $repoRoot $ReportPath
}
$resolvedNugetConfigPath = Join-Path $repoRoot "NuGet.Config"

function Clear-ProxyEnvironmentVariables {
    $proxyNames = @("ALL_PROXY", "HTTP_PROXY", "HTTPS_PROXY", "GIT_HTTP_PROXY", "GIT_HTTPS_PROXY")
    foreach ($proxyName in $proxyNames) {
        [Environment]::SetEnvironmentVariable($proxyName, $null)
    }
}

function Test-LoopbackProxyReachable {
    param([Uri]$Uri)

    if ($null -eq $Uri) {
        return $false
    }

    if ($Uri.IsDefaultPort) {
        return $false
    }

    if ($Uri.Port -eq 9) {
        return $false
    }

    $tcpClient = $null
    try {
        $tcpClient = [System.Net.Sockets.TcpClient]::new()
        $connectTask = $tcpClient.ConnectAsync($Uri.DnsSafeHost, $Uri.Port)
        if (-not $connectTask.Wait(150)) {
            return $false
        }

        return $tcpClient.Connected
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $tcpClient) {
            $tcpClient.Dispose()
        }
    }
}

function Get-SanitizedUriValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "unset"
    }

    try {
        [Uri]$uri = $Value
        if ($uri.IsAbsoluteUri) {
            return "{0}://{1}{2}" -f $uri.Scheme, $uri.Host, ($(if ($uri.IsDefaultPort) { "" } else { ":$($uri.Port)" }))
        }
    }
    catch {
    }

    return "set"
}

function Get-ProxyDiagnostics {
    $proxyNames = @("ALL_PROXY", "HTTP_PROXY", "HTTPS_PROXY", "GIT_HTTP_PROXY", "GIT_HTTPS_PROXY")
    return @($proxyNames | ForEach-Object {
            $value = [Environment]::GetEnvironmentVariable($_)
            [Uri]$uri = $null
            $hasUri = $false
            try {
                if (-not [string]::IsNullOrWhiteSpace($value)) {
                    $uri = [Uri]$value
                    $hasUri = $uri.IsAbsoluteUri
                }
            }
            catch {
                $uri = $null
                $hasUri = $false
            }
            $isLoopback = $false
            $reachable = $false
            if ($hasUri -and $null -ne $uri) {
                $isLoopback = $uri.IsLoopback
                if ($isLoopback) {
                    $reachable = Test-LoopbackProxyReachable -Uri $uri
                }
            }

            [PSCustomObject]@{
                name = $_
                configured = -not [string]::IsNullOrWhiteSpace($value)
                sanitizedValue = Get-SanitizedUriValue -Value $value
                host = if ($hasUri -and $null -ne $uri) { $uri.DnsSafeHost } else { "" }
                port = if ($hasUri -and $null -ne $uri -and -not $uri.IsDefaultPort) { $uri.Port } else { 0 }
                isLoopback = $isLoopback
                reachable = $reachable
            }
        })
}

function Get-ProxyPreflightDecision {
    param([object[]]$ProxyDiagnostics)

    $configured = @($ProxyDiagnostics | Where-Object { $_.configured })
    if ($configured.Count -eq 0) {
        return [PSCustomObject]@{
            HasDecision = $false
            Status = ""
            Message = ""
        }
    }

    $loopbackConfigured = @($configured | Where-Object { $_.isLoopback })
    if ($loopbackConfigured.Count -ne $configured.Count) {
        return [PSCustomObject]@{
            HasDecision = $false
            Status = ""
            Message = ""
        }
    }

    $unreachableLoopback = @($loopbackConfigured | Where-Object { -not $_.reachable })
    if ($unreachableLoopback.Count -ne $configured.Count) {
        return [PSCustomObject]@{
            HasDecision = $false
            Status = ""
            Message = ""
        }
    }

    $proxySummary = ($configured | ForEach-Object {
            if ($_.port -gt 0) {
                "{0}={1}:{2}" -f $_.name, $_.host, $_.port
            }
            else {
                "{0}={1}" -f $_.name, $_.host
            }
        }) -join ", "

    if ($ExecutionMode -eq "strict-online") {
        return [PSCustomObject]@{
            HasDecision = $true
            Status = "audit_failed_proxy_misconfigured"
            Message = "Strict online NuGet audit cannot run because all configured proxies resolve to unreachable loopback endpoints: $proxySummary"
        }
    }

    return [PSCustomObject]@{
        HasDecision = $true
        Status = "audit_skipped_local_offline_proxy_unavailable"
        Message = "Best-effort local NuGet audit skipped because all configured proxies resolve to unreachable loopback endpoints: $proxySummary"
    }
}

function Get-AuditWarningCodes {
    param([string]$Text)

    $codes = New-Object System.Collections.Generic.List[string]
    if ($Text -match '\bNU1900\b') {
        $codes.Add("NU1900") | Out-Null
    }

    if ($Text -match '\bNU1905\b') {
        $codes.Add("NU1905") | Out-Null
    }

    return @($codes)
}

function Resolve-AuditDataUnavailableStatus {
    param([string[]]$WarningCodes)

    if ($WarningCodes -contains "NU1900") {
        if ($ExecutionMode -eq "strict-online") {
            return "audit_failed_infrastructure_unavailable"
        }

        return "audit_degraded_infrastructure_unavailable"
    }

    if ($WarningCodes -contains "NU1905") {
        if ($ExecutionMode -eq "strict-online") {
            return "audit_failed_audit_source_unavailable"
        }

        return "audit_degraded_audit_source_unavailable"
    }

    return ""
}

function Invoke-DotnetStep {
    param(
        [string[]]$Arguments,
        [string]$SimulatedOutputPath,
        [int]$SimulatedExitCode
    )

    if (-not [string]::IsNullOrWhiteSpace($SimulatedOutputPath) -or $SimulatedExitCode -ne -2147483648) {
        $text = if ([string]::IsNullOrWhiteSpace($SimulatedOutputPath)) {
            ""
        }
        else {
            Get-Content -Path $SimulatedOutputPath -Raw
        }

        return [PSCustomObject]@{
            ExitCode = if ($SimulatedExitCode -eq -2147483648) { 0 } else { $SimulatedExitCode }
            Text = $text
        }
    }

    $output = & dotnet @Arguments 2>&1
    $output | Out-Host
    return [PSCustomObject]@{
        ExitCode = $LASTEXITCODE
        Text = ($output | Out-String)
    }
}

function Get-NugetSourcesSummary {
    $arguments = @("nuget", "list", "source")
    if (Test-Path -LiteralPath $resolvedNugetConfigPath) {
        $arguments += @("--configfile", $resolvedNugetConfigPath)
    }

    $output = & dotnet @arguments 2>&1
    return ($output | Out-String).Trim()
}

function Try-ParseVulnerabilityReport {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    $jsonStart = $Text.IndexOf('{')
    if ($jsonStart -lt 0) {
        return $null
    }

    return $Text.Substring($jsonStart) | ConvertFrom-Json
}

function Get-VulnerablePackages {
    param($Report)

    $vulnerablePackages = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Report) {
        return @()
    }

    foreach ($project in @($Report.projects)) {
        $frameworksProperty = $project.PSObject.Properties["frameworks"]
        if ($null -eq $frameworksProperty) {
            continue
        }

        foreach ($framework in @($frameworksProperty.Value)) {
            foreach ($packageGroupName in @("topLevelPackages", "transitivePackages")) {
                $packageGroup = $framework.PSObject.Properties[$packageGroupName]
                if ($null -eq $packageGroup) {
                    continue
                }

                foreach ($package in @($packageGroup.Value)) {
                    $vulnerabilities = @($package.vulnerabilities)
                    if ($vulnerabilities.Count -eq 0) {
                        continue
                    }

                    $severityList = ($vulnerabilities | ForEach-Object {
                        if ($_.PSObject.Properties["severity"]) {
                            $_.severity
                        }
                    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ", "

                    if ([string]::IsNullOrWhiteSpace($severityList)) {
                        $severityList = "unspecified"
                    }

                    $vulnerablePackages.Add(("{0}::{1}::{2}::{3}" -f $project.path, $framework.framework, $package.id, $severityList))
                }
            }
        }
    }

    return @($vulnerablePackages)
}

function Write-ReportAndExit {
    param(
        [string]$Status,
        [int]$ExitCode,
        [string]$NugetSources,
        [object[]]$ProxyDiagnostics,
        [int]$RestoreExitCode,
        [int]$ListExitCode,
        [object[]]$VulnerablePackages,
        [bool]$AuditDataUnavailable,
        [string]$PreflightStatus,
        [string]$PreflightMessage,
        [string[]]$AuditWarningCodes,
        [string]$RestoreText,
        [string]$ListText
    )

    $report = [PSCustomObject]@{
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        status = $Status
        executionMode = $ExecutionMode
        target = $resolvedTarget
        repoRoot = $repoRoot
        nugetConfigPath = $(if (Test-Path -LiteralPath $resolvedNugetConfigPath) { $resolvedNugetConfigPath } else { "" })
        auditMode = $AuditMode
        auditLevel = $AuditLevel
        auditDataUnavailable = $AuditDataUnavailable
        preflightStatus = $PreflightStatus
        preflightMessage = $PreflightMessage
        auditWarningCodes = @($AuditWarningCodes)
        restoreExitCode = $RestoreExitCode
        listExitCode = $ListExitCode
        nugetSources = $NugetSources
        proxyEnvironment = $ProxyDiagnostics
        vulnerablePackages = @($VulnerablePackages)
        restoreSummary = $RestoreText.Trim()
        listSummary = $ListText.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($resolvedReportPath)) {
        $directory = Split-Path -Parent $resolvedReportPath
        if (-not [string]::IsNullOrWhiteSpace($directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }

        $report | ConvertTo-Json -Depth 8 | Set-Content -Path $resolvedReportPath -Encoding UTF8
    }

    exit $ExitCode
}

if ($ClearProxyEnvironment) {
    Clear-ProxyEnvironmentVariables
}

Write-Host "[NuGet Security Gate] Target=$Target AuditMode=$AuditMode AuditLevel=$AuditLevel ExecutionMode=$ExecutionMode"
$proxyDiagnostics = Get-ProxyDiagnostics
$proxyDiagnostics | ForEach-Object {
    Write-Host ("[NuGet Security Gate] Proxy {0}: configured={1}; value={2}" -f $_.name, $_.configured, $_.sanitizedValue)
}

$nugetSources = Get-NugetSourcesSummary
Write-Host "[NuGet Security Gate] Sources:"
if ([string]::IsNullOrWhiteSpace($nugetSources)) {
    Write-Host "  <none>"
}
else {
    $nugetSources.Split("`r", [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object {
        $_.Split("`n", [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { Write-Host "  $_" }
    }
}

$proxyPreflight = Get-ProxyPreflightDecision -ProxyDiagnostics $proxyDiagnostics
if ($proxyPreflight.HasDecision) {
    $messageColor = if ($ExecutionMode -eq "strict-online") { "Red" } else { "Yellow" }
    Write-Host ("[NuGet Security Gate] Preflight: {0}" -f $proxyPreflight.Message) -ForegroundColor $messageColor
    Write-Host "[NuGet Security Gate] FinalStatus=$($proxyPreflight.Status)" -ForegroundColor $messageColor
    Write-ReportAndExit -Status $proxyPreflight.Status -ExitCode $(if ($ExecutionMode -eq "strict-online") { 1 } else { 0 }) -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode 0 -ListExitCode 0 -VulnerablePackages @() -AuditDataUnavailable $true -PreflightStatus $proxyPreflight.Status -PreflightMessage $proxyPreflight.Message -AuditWarningCodes @() -RestoreText "" -ListText ""
}

$restoreArguments = @(
    "restore",
    $resolvedTarget,
    "-m:1",
    "--disable-build-servers",
    "-v:minimal",
    ("-p:NuGetAudit=true"),
    ("-p:NuGetAuditMode={0}" -f $AuditMode),
    ("-p:NuGetAuditLevel={0}" -f $AuditLevel),
    ("-p:RestoreForce=true")
)
if (Test-Path -LiteralPath $resolvedNugetConfigPath) {
    $restoreArguments += @("--configfile", $resolvedNugetConfigPath)
}

Write-Host "[NuGet Security Gate] Restoring with audit enabled..."
$restoreResult = Invoke-DotnetStep -Arguments $restoreArguments -SimulatedOutputPath $SimulatedRestoreOutputPath -SimulatedExitCode $SimulatedRestoreExitCode
$restoreText = $restoreResult.Text
$restoreWarningCodes = Get-AuditWarningCodes -Text $restoreText
$restoreAuditStatus = Resolve-AuditDataUnavailableStatus -WarningCodes $restoreWarningCodes

if (-not [string]::IsNullOrWhiteSpace($restoreAuditStatus)) {
    $messageColor = if ($ExecutionMode -eq "strict-online") { "Red" } else { "Yellow" }
    Write-Host ("[NuGet Security Gate] Package vulnerability audit data could not be retrieved ({0})." -f ($restoreWarningCodes -join ", ")) -ForegroundColor $messageColor
    Write-Host "[NuGet Security Gate] FinalStatus=$restoreAuditStatus" -ForegroundColor $messageColor
    Write-ReportAndExit -Status $restoreAuditStatus -ExitCode $(if ($ExecutionMode -eq "strict-online") { 1 } else { 0 }) -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode 0 -VulnerablePackages @() -AuditDataUnavailable $true -PreflightStatus "" -PreflightMessage "" -AuditWarningCodes $restoreWarningCodes -RestoreText $restoreText -ListText ""
}

if ($restoreResult.ExitCode -ne 0) {
    Write-Host "[NuGet Security Gate] dotnet restore failed with exit code $($restoreResult.ExitCode)." -ForegroundColor Red
    Write-Host "[NuGet Security Gate] FinalStatus=audit_failed_command_error" -ForegroundColor Red
    Write-ReportAndExit -Status "audit_failed_command_error" -ExitCode 1 -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode 0 -VulnerablePackages @() -AuditDataUnavailable $false -PreflightStatus "" -PreflightMessage "" -AuditWarningCodes @() -RestoreText $restoreText -ListText ""
}

Write-Host "[NuGet Security Gate] Listing vulnerable packages..."
$listArguments = @(
    "list",
    $resolvedTarget,
    "package",
    "--vulnerable",
    "--include-transitive",
    "--format", "json"
)
if (Test-Path -LiteralPath $resolvedNugetConfigPath) {
    $listArguments += @("--configfile", $resolvedNugetConfigPath)
}

$listResult = Invoke-DotnetStep -Arguments $listArguments -SimulatedOutputPath $SimulatedListOutputPath -SimulatedExitCode $SimulatedListExitCode
$listText = $listResult.Text
$listWarningCodes = Get-AuditWarningCodes -Text $listText
$listAuditStatus = Resolve-AuditDataUnavailableStatus -WarningCodes $listWarningCodes

if (-not [string]::IsNullOrWhiteSpace($listAuditStatus)) {
    $messageColor = if ($ExecutionMode -eq "strict-online") { "Red" } else { "Yellow" }
    Write-Host ("[NuGet Security Gate] Vulnerability report retrieval degraded during package listing ({0})." -f ($listWarningCodes -join ", ")) -ForegroundColor $messageColor
    Write-Host "[NuGet Security Gate] FinalStatus=$listAuditStatus" -ForegroundColor $messageColor
    Write-ReportAndExit -Status $listAuditStatus -ExitCode $(if ($ExecutionMode -eq "strict-online") { 1 } else { 0 }) -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode $listResult.ExitCode -VulnerablePackages @() -AuditDataUnavailable $true -PreflightStatus "" -PreflightMessage "" -AuditWarningCodes $listWarningCodes -RestoreText $restoreText -ListText $listText
}

if ($listResult.ExitCode -ne 0) {
    Write-Host "[NuGet Security Gate] dotnet list package --vulnerable failed with exit code $($listResult.ExitCode)." -ForegroundColor Red
    Write-Host "[NuGet Security Gate] FinalStatus=audit_failed_command_error" -ForegroundColor Red
    Write-ReportAndExit -Status "audit_failed_command_error" -ExitCode 1 -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode $listResult.ExitCode -VulnerablePackages @() -AuditDataUnavailable $false -PreflightStatus "" -PreflightMessage "" -AuditWarningCodes @() -RestoreText $restoreText -ListText $listText
}

$report = Try-ParseVulnerabilityReport -Text $listText
if ($null -eq $report) {
    Write-Host "[NuGet Security Gate] Vulnerability report did not return JSON output." -ForegroundColor Red
    Write-Host "[NuGet Security Gate] FinalStatus=audit_failed_command_error" -ForegroundColor Red
    Write-ReportAndExit -Status "audit_failed_command_error" -ExitCode 1 -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode $listResult.ExitCode -VulnerablePackages @() -AuditDataUnavailable $false -PreflightStatus "" -PreflightMessage "" -AuditWarningCodes @() -RestoreText $restoreText -ListText $listText
}

$vulnerablePackages = @(Get-VulnerablePackages -Report $report)
if ($vulnerablePackages.Count -gt 0) {
    Write-Host "[NuGet Security Gate] Vulnerable packages detected:" -ForegroundColor Red
    $vulnerablePackages | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host "[NuGet Security Gate] FinalStatus=audit_failed_vulnerabilities_found" -ForegroundColor Red
    Write-ReportAndExit -Status "audit_failed_vulnerabilities_found" -ExitCode 1 -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode $listResult.ExitCode -VulnerablePackages $vulnerablePackages -AuditDataUnavailable $false -PreflightStatus "" -PreflightMessage "" -AuditWarningCodes @() -RestoreText $restoreText -ListText $listText
}

Write-Host "[NuGet Security Gate] FinalStatus=audit_passed" -ForegroundColor Green
Write-Host "[NuGet Security Gate] Passed." -ForegroundColor Green
Write-ReportAndExit -Status "audit_passed" -ExitCode 0 -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode $listResult.ExitCode -VulnerablePackages $vulnerablePackages -AuditDataUnavailable $false -PreflightStatus "" -PreflightMessage "" -AuditWarningCodes @() -RestoreText $restoreText -ListText $listText

