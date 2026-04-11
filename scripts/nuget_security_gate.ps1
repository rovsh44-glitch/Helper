param(
    [string]$Target = "Helper.sln",
    [ValidateSet("direct", "all")][string]$AuditMode = "all",
    [ValidateSet("low", "moderate", "high", "critical")][string]$AuditLevel = "low",
    [ValidateSet("strict-online", "best-effort-local")][string]$ExecutionMode = "best-effort-local",
    [string]$ReportPath = "",
    [string]$SimulatedRestoreOutputPath = "",
    [int]$SimulatedRestoreExitCode = -2147483648,
    [string]$SimulatedListOutputPath = "",
    [int]$SimulatedListExitCode = -2147483648
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-SanitizedUriValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "unset"
    }

    $uri = $null
    if ([Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri)) {
        return "{0}://{1}{2}" -f $uri.Scheme, $uri.Host, ($(if ($uri.IsDefaultPort) { "" } else { ":$($uri.Port)" }))
    }

    return "set"
}

function Get-ProxyDiagnostics {
    $proxyNames = @("ALL_PROXY", "HTTP_PROXY", "HTTPS_PROXY", "GIT_HTTP_PROXY", "GIT_HTTPS_PROXY")
    return @($proxyNames | ForEach-Object {
            $value = [Environment]::GetEnvironmentVariable($_)
            [PSCustomObject]@{
                name = $_
                configured = -not [string]::IsNullOrWhiteSpace($value)
                sanitizedValue = Get-SanitizedUriValue -Value $value
            }
        })
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
    $output = & dotnet nuget list source 2>&1
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
        [string]$RestoreText,
        [string]$ListText
    )

    $report = [PSCustomObject]@{
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        status = $Status
        executionMode = $ExecutionMode
        target = $Target
        auditMode = $AuditMode
        auditLevel = $AuditLevel
        auditDataUnavailable = $AuditDataUnavailable
        restoreExitCode = $RestoreExitCode
        listExitCode = $ListExitCode
        nugetSources = $NugetSources
        proxyEnvironment = $ProxyDiagnostics
        vulnerablePackages = @($VulnerablePackages)
        restoreSummary = $RestoreText.Trim()
        listSummary = $ListText.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
        $directory = Split-Path -Parent $ReportPath
        if (-not [string]::IsNullOrWhiteSpace($directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }

        $report | ConvertTo-Json -Depth 8 | Set-Content -Path $ReportPath -Encoding UTF8
    }

    exit $ExitCode
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

$restoreArguments = @(
    "restore",
    $Target,
    "-m:1",
    "--disable-build-servers",
    "-v:minimal",
    ("-p:NuGetAudit=true"),
    ("-p:NuGetAuditMode={0}" -f $AuditMode),
    ("-p:NuGetAuditLevel={0}" -f $AuditLevel),
    ("-p:RestoreForce=true")
)

Write-Host "[NuGet Security Gate] Restoring with audit enabled..."
$restoreResult = Invoke-DotnetStep -Arguments $restoreArguments -SimulatedOutputPath $SimulatedRestoreOutputPath -SimulatedExitCode $SimulatedRestoreExitCode
$restoreText = $restoreResult.Text
$restoreHasNu1900 = $restoreText -match '\bNU1900\b'

if ($restoreHasNu1900) {
    $status = if ($ExecutionMode -eq "strict-online") {
        "audit_failed_infrastructure_unavailable"
    }
    else {
        "audit_degraded_infrastructure_unavailable"
    }
    $messageColor = if ($ExecutionMode -eq "strict-online") { "Red" } else { "Yellow" }
    Write-Host "[NuGet Security Gate] Package vulnerability audit data could not be retrieved (NU1900)." -ForegroundColor $messageColor
    Write-Host "[NuGet Security Gate] FinalStatus=$status" -ForegroundColor $messageColor
    Write-ReportAndExit -Status $status -ExitCode $(if ($ExecutionMode -eq "strict-online") { 1 } else { 0 }) -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode 0 -VulnerablePackages @() -AuditDataUnavailable $true -RestoreText $restoreText -ListText ""
}

if ($restoreResult.ExitCode -ne 0) {
    Write-Host "[NuGet Security Gate] dotnet restore failed with exit code $($restoreResult.ExitCode)." -ForegroundColor Red
    Write-Host "[NuGet Security Gate] FinalStatus=audit_failed_command_error" -ForegroundColor Red
    Write-ReportAndExit -Status "audit_failed_command_error" -ExitCode 1 -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode 0 -VulnerablePackages @() -AuditDataUnavailable $false -RestoreText $restoreText -ListText ""
}

Write-Host "[NuGet Security Gate] Listing vulnerable packages..."
$listArguments = @(
    "list",
    $Target,
    "package",
    "--vulnerable",
    "--include-transitive",
    "--format", "json"
)

$listResult = Invoke-DotnetStep -Arguments $listArguments -SimulatedOutputPath $SimulatedListOutputPath -SimulatedExitCode $SimulatedListExitCode
$listText = $listResult.Text
$listHasNu1900 = $listText -match '\bNU1900\b'

if ($listHasNu1900) {
    $status = if ($ExecutionMode -eq "strict-online") {
        "audit_failed_infrastructure_unavailable"
    }
    else {
        "audit_degraded_infrastructure_unavailable"
    }
    $messageColor = if ($ExecutionMode -eq "strict-online") { "Red" } else { "Yellow" }
    Write-Host "[NuGet Security Gate] Vulnerability report retrieval degraded during package listing (NU1900)." -ForegroundColor $messageColor
    Write-Host "[NuGet Security Gate] FinalStatus=$status" -ForegroundColor $messageColor
    Write-ReportAndExit -Status $status -ExitCode $(if ($ExecutionMode -eq "strict-online") { 1 } else { 0 }) -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode $listResult.ExitCode -VulnerablePackages @() -AuditDataUnavailable $true -RestoreText $restoreText -ListText $listText
}

if ($listResult.ExitCode -ne 0) {
    Write-Host "[NuGet Security Gate] dotnet list package --vulnerable failed with exit code $($listResult.ExitCode)." -ForegroundColor Red
    Write-Host "[NuGet Security Gate] FinalStatus=audit_failed_command_error" -ForegroundColor Red
    Write-ReportAndExit -Status "audit_failed_command_error" -ExitCode 1 -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode $listResult.ExitCode -VulnerablePackages @() -AuditDataUnavailable $false -RestoreText $restoreText -ListText $listText
}

$report = Try-ParseVulnerabilityReport -Text $listText
if ($null -eq $report) {
    Write-Host "[NuGet Security Gate] Vulnerability report did not return JSON output." -ForegroundColor Red
    Write-Host "[NuGet Security Gate] FinalStatus=audit_failed_command_error" -ForegroundColor Red
    Write-ReportAndExit -Status "audit_failed_command_error" -ExitCode 1 -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode $listResult.ExitCode -VulnerablePackages @() -AuditDataUnavailable $false -RestoreText $restoreText -ListText $listText
}

$vulnerablePackages = @(Get-VulnerablePackages -Report $report)
if ($vulnerablePackages.Count -gt 0) {
    Write-Host "[NuGet Security Gate] Vulnerable packages detected:" -ForegroundColor Red
    $vulnerablePackages | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host "[NuGet Security Gate] FinalStatus=audit_failed_vulnerabilities_found" -ForegroundColor Red
    Write-ReportAndExit -Status "audit_failed_vulnerabilities_found" -ExitCode 1 -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode $listResult.ExitCode -VulnerablePackages $vulnerablePackages -AuditDataUnavailable $false -RestoreText $restoreText -ListText $listText
}

Write-Host "[NuGet Security Gate] FinalStatus=audit_passed" -ForegroundColor Green
Write-Host "[NuGet Security Gate] Passed." -ForegroundColor Green
Write-ReportAndExit -Status "audit_passed" -ExitCode 0 -NugetSources $nugetSources -ProxyDiagnostics $proxyDiagnostics -RestoreExitCode $restoreResult.ExitCode -ListExitCode $listResult.ExitCode -VulnerablePackages $vulnerablePackages -AuditDataUnavailable $false -RestoreText $restoreText -ListText $listText

