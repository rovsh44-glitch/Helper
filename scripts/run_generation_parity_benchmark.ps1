param(
    [string]$GoldenCorpusPath = "eval/golden_template_prompts_ru_en.jsonl",
    [string]$IncidentCorpusPath = "eval/incident_corpus.jsonl",
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $GoldenCorpusPath)) {
    throw "[ParityBenchmark] Golden corpus not found: $GoldenCorpusPath"
}

if (-not (Test-Path $IncidentCorpusPath)) {
    throw "[ParityBenchmark] Incident corpus not found: $IncidentCorpusPath"
}

$goldenCount = (Get-Content $GoldenCorpusPath | Where-Object { $_.Trim() -ne "" }).Count
$incidentCount = (Get-Content $IncidentCorpusPath | Where-Object { $_.Trim() -ne "" }).Count

if ($goldenCount -lt 100) {
    throw "[ParityBenchmark] Golden corpus requires >=100 scenarios. Found: $goldenCount"
}

if ($incidentCount -lt 80) {
    throw "[ParityBenchmark] Incident corpus requires >=80 scenarios. Found: $incidentCount"
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = "temp/verification/heavy/HELPER_GENERATION_PARITY_BENCHMARK_" + (Get-Date -Format "yyyy-MM-dd_HH-mm-ss") + ".md"
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not [System.IO.Path]::IsPathRooted($ReportPath)) {
    $ReportPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ReportPath))
}

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Write-Host "[ParityBenchmark] Running benchmark. golden=$goldenCount incident=$incidentCount"
Write-Host "[ParityBenchmark] Note: benchmark is synthetic regression coverage and does not replace production parity window gates."
& (Join-Path $PSScriptRoot "invoke_helper_runtime_cli.ps1") benchmark-generation-parity $GoldenCorpusPath $IncidentCorpusPath $ReportPath
if ($LASTEXITCODE -ne 0) {
    throw "[ParityBenchmark] Benchmark gate failed."
}

Write-Host "[ParityBenchmark] Passed. Report: $ReportPath" -ForegroundColor Green
