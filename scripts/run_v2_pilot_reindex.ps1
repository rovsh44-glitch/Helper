[CmdletBinding()]
param(
    [string]$WorkspaceRoot = ".",
    [string]$ApiBaseUrl = "http://127.0.0.1:5000",
    [string]$ApiKey = "",
    [string]$ReportPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$env:HELPER_INDEX_PIPELINE_VERSION = "v2"

$orderedIndexScript = Join-Path $PSScriptRoot "run_ordered_library_indexing.ps1"

& $orderedIndexScript `
    -WorkspaceRoot $WorkspaceRoot `
    -ApiBaseUrl $ApiBaseUrl `
    -ApiKey $ApiKey `
    -PipelineVersion "v2" `
    -AppendDiscoveredDomains:$false `
    -ReportPath $ReportPath `
    -DomainOrder @("psychology", "medicine", "encyclopedias", "history")
