param(
    [string]$Target = "Helper.sln",
    [ValidateSet("direct", "all")][string]$AuditMode = "all",
    [ValidateSet("low", "moderate", "high", "critical")][string]$AuditLevel = "low"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Write-Host "[NuGet Security Gate] Target=$Target AuditMode=$AuditMode AuditLevel=$AuditLevel"

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
$restoreOutput = & dotnet @restoreArguments 2>&1
$restoreOutput | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "[NuGet Security Gate] dotnet restore failed with exit code $LASTEXITCODE."
}

if (($restoreOutput | Out-String) -match '\bNU1900\b') {
    throw "[NuGet Security Gate] Package vulnerability audit data could not be retrieved (NU1900)."
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

$listOutput = & dotnet @listArguments 2>&1
$listOutput | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "[NuGet Security Gate] dotnet list package --vulnerable failed with exit code $LASTEXITCODE."
}

$listText = ($listOutput | Out-String).Trim()
$jsonStart = $listText.IndexOf('{')
if ($jsonStart -lt 0) {
    throw "[NuGet Security Gate] Vulnerability report did not return JSON output."
}

$report = $listText.Substring($jsonStart) | ConvertFrom-Json
$vulnerablePackages = New-Object System.Collections.Generic.List[string]

foreach ($project in @($report.projects)) {
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

if ($vulnerablePackages.Count -gt 0) {
    Write-Host "[NuGet Security Gate] Vulnerable packages detected:" -ForegroundColor Red
    $vulnerablePackages | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    throw "[NuGet Security Gate] Vulnerable packages were detected."
}

Write-Host "[NuGet Security Gate] Passed."

