param(
    [string]$IncidentCorpusPath = "eval/incident_corpus.jsonl",
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $IncidentCorpusPath)) {
    throw "[ClosedLoop] Incident corpus not found: $IncidentCorpusPath"
}

$incidentCount = (Get-Content $IncidentCorpusPath | Where-Object { $_.Trim() -ne "" }).Count
if ($incidentCount -lt 80) {
    throw "[ClosedLoop] Incident corpus requires >=80 scenarios. Found: $incidentCount"
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = "doc/CLOSED_LOOP_PREDICTABILITY_REPORT_" + (Get-Date -Format "yyyy-MM-dd_HH-mm-ss") + ".md"
}

Write-Host "[ClosedLoop] Running predictability protocol. incident=$incidentCount"
$cliArgs = @("certify-closed-loop-predictability", $IncidentCorpusPath, $ReportPath)
& (Join-Path $PSScriptRoot "invoke_helper_runtime_cli.ps1") -CliArgs $cliArgs
if ($LASTEXITCODE -ne 0) {
    throw "[ClosedLoop] Predictability protocol failed."
}

Write-Host "[ClosedLoop] Passed. Report: $ReportPath" -ForegroundColor Green
