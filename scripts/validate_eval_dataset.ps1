param(
    [string]$DatasetPath = "eval/human_level_parity_ru_en.jsonl",
    [int]$MinScenarios = 200,
    [double]$MinEndToEndRatio = 0.60,
    [double]$MinLanguageShare = 0.30,
    [double]$MinKindShare = 0.15,
    [string]$ReportPath = "doc/eval_dataset_validation_report.md"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $DatasetPath)) {
    throw "[EvalDatasetValidation] Dataset not found: $DatasetPath"
}

$lines = Get-Content -Path $DatasetPath -Encoding UTF8 | Where-Object { $_ -and -not $_.TrimStart().StartsWith("#") }
$scenarios = @()
$lineNo = 0
foreach ($line in $lines) {
    $lineNo++
    try {
        $scenarios += ($line | ConvertFrom-Json)
    }
    catch {
        throw "[EvalDatasetValidation] Invalid JSONL at line $lineNo in $DatasetPath"
    }
}

if ($scenarios.Count -eq 0) {
    throw "[EvalDatasetValidation] Dataset is empty."
}

$total = $scenarios.Count
$endToEnd = @($scenarios | Where-Object { [bool]$_.endToEnd }).Count
$endToEndRatio = if ($total -eq 0) { 0.0 } else { $endToEnd / [double]$total }
$langGroups = $scenarios | Group-Object { ([string]$_.language).Trim().ToLowerInvariant() }
$kindGroups = $scenarios | Group-Object { ([string]$_.kind).Trim().ToLowerInvariant() }

$langMap = @{}
foreach ($g in $langGroups) { $langMap[$g.Name] = $g.Count }
$kindMap = @{}
foreach ($g in $kindGroups) { $kindMap[$g.Name] = $g.Count }

$violations = New-Object System.Collections.Generic.List[string]
if ($total -lt $MinScenarios) {
    $violations.Add("TotalScenarios $total < $MinScenarios")
}
if ($endToEndRatio -lt $MinEndToEndRatio) {
    $violations.Add("EndToEndRatio $([math]::Round($endToEndRatio * 100, 2))% < $([math]::Round($MinEndToEndRatio * 100, 2))%")
}

foreach ($lang in @("ru", "en")) {
    $count = if ($langMap.ContainsKey($lang)) { [int]$langMap[$lang] } else { 0 }
    $share = if ($total -eq 0) { 0.0 } else { $count / [double]$total }
    if ($share -lt $MinLanguageShare) {
        $violations.Add("LanguageShare[$lang] $([math]::Round($share * 100, 2))% < $([math]::Round($MinLanguageShare * 100, 2))%")
    }
}

foreach ($kind in @("clarification", "research", "personalization")) {
    $count = if ($kindMap.ContainsKey($kind)) { [int]$kindMap[$kind] } else { 0 }
    $share = if ($total -eq 0) { 0.0 } else { $count / [double]$total }
    if ($share -lt $MinKindShare) {
        $violations.Add("KindShare[$kind] $([math]::Round($share * 100, 2))% < $([math]::Round($MinKindShare * 100, 2))%")
    }
}

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
$out = @()
$out += "# Eval Dataset Validation Report"
$out += "Generated: $timestamp"
$out += "Dataset: $DatasetPath"
$out += ""
$out += "- TotalScenarios: $total"
$out += "- EndToEndScenarios: $endToEnd"
$out += "- EndToEndRatio: $([math]::Round($endToEndRatio * 100, 2))%"
$out += ""
$out += "## Language distribution"
foreach ($lang in ($langMap.Keys | Sort-Object)) {
    $count = [int]$langMap[$lang]
    $share = if ($total -eq 0) { 0.0 } else { $count / [double]$total }
    $out += "- ${lang}: $count ($([math]::Round($share * 100, 2))%)"
}
$out += ""
$out += "## Kind distribution"
foreach ($kind in ($kindMap.Keys | Sort-Object)) {
    $count = [int]$kindMap[$kind]
    $share = if ($total -eq 0) { 0.0 } else { $count / [double]$total }
    $out += "- ${kind}: $count ($([math]::Round($share * 100, 2))%)"
}
$out += ""
$out += "## Status"
if ($violations.Count -eq 0) {
    $out += "- PASS"
}
else {
    $out += "- FAIL"
    $out += "## Violations"
    foreach ($v in $violations) {
        $out += "- $v"
    }
}

$dir = [System.IO.Path]::GetDirectoryName($ReportPath)
if (-not [string]::IsNullOrWhiteSpace($dir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}
Set-Content -Path $ReportPath -Value ($out -join "`r`n") -Encoding UTF8
Write-Host "[EvalDatasetValidation] Report saved to $ReportPath"

if ($violations.Count -gt 0) {
    throw "[EvalDatasetValidation] Validation failed."
}

Write-Host "[EvalDatasetValidation] Passed." -ForegroundColor Green
