param(
    [string]$OutDir = "temp/verification/parity_nightly",
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$AllowIncompleteWindow,
    [int]$WindowDays = 14
)

$ErrorActionPreference = "Stop"

$date = Get-Date -Format "yyyy-MM-dd"
$stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$nightlyDir = Join-Path $OutDir $date
New-Item -ItemType Directory -Force -Path $nightlyDir | Out-Null

$reportPath = Join-Path $nightlyDir ("HELPER_PARITY_GATE_" + $stamp + ".md")
$benchmarkPath = Join-Path $nightlyDir ("HELPER_PARITY_BENCHMARK_" + $stamp + ".md")
$windowGatePath = Join-Path $nightlyDir ("HELPER_PARITY_WINDOW_GATE_" + $stamp + ".md")
$certificationGatePath = Join-Path $nightlyDir ("HELPER_TEMPLATE_CERTIFICATION_GATE_" + $stamp + ".md")

if (-not $SkipBuild.IsPresent) {
    Write-Host "[ParityNightly] Build..."
dotnet build Helper.sln -c Debug -m:1
    if ($LASTEXITCODE -ne 0) {
        throw "[ParityNightly] Build failed."
    }
}

if (-not $SkipTests.IsPresent) {
    Write-Host "[ParityNightly] Unit tests..."
    dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj -c Debug --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "[ParityNightly] Tests failed."
    }
}

Write-Host "[ParityNightly] KPI gate..."
powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_gate.ps1 -ReportPath $reportPath
if ($LASTEXITCODE -ne 0) {
    throw "[ParityNightly] KPI gate failed."
}

Write-Host "[ParityNightly] Benchmark gate..."
powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_benchmark.ps1 -ReportPath $benchmarkPath
if ($LASTEXITCODE -ne 0) {
    throw "[ParityNightly] Benchmark gate failed."
}

if ($AllowIncompleteWindow.IsPresent) {
    $env:HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE = "true"
}
else {
    $env:HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE = "false"
}

Write-Host "[ParityNightly] Rolling window gate..."
powershell -ExecutionPolicy Bypass -File scripts/run_generation_parity_window_gate.ps1 -ReportPath $windowGatePath -WindowDays $WindowDays
if ($LASTEXITCODE -ne 0) {
    throw "[ParityNightly] Rolling window gate failed."
}

Write-Host "[ParityNightly] Template certification gate..."
powershell -ExecutionPolicy Bypass -File scripts/run_template_certification_gate.ps1 -ReportPath $certificationGatePath
if ($LASTEXITCODE -ne 0) {
    throw "[ParityNightly] Template certification gate failed."
}

Write-Host "[ParityNightly] Completed successfully. Artifacts: $nightlyDir" -ForegroundColor Green

