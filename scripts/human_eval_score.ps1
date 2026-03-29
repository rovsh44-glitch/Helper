param(
    [string]$InputCsv = "eval/human_eval_scores.csv"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $InputCsv)) {
    throw "[HumanEval] Input file not found: $InputCsv"
}

$rows = Import-Csv -Path $InputCsv
if (-not $rows -or $rows.Count -eq 0) {
    throw "[HumanEval] Input file is empty."
}

$numericColumns = @("clarity", "empathy_appropriateness", "usefulness", "factuality")
$variants = $rows | Group-Object -Property variant

if ($variants.Count -lt 2) {
    Write-Warning "[HumanEval] Found less than 2 variants. Delta comparison may be incomplete."
}

function Convert-ToScore([object]$raw) {
    $text = [string]$raw
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "[HumanEval] Empty score value."
    }

    $normalized = $text.Trim().Replace(",", ".")
    $parsed = 0.0
    if (-not [double]::TryParse($normalized, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        throw "[HumanEval] Invalid score value '$text'."
    }

    return $parsed
}

function Get-Average([object[]]$data, [string]$column) {
    $values = $data | ForEach-Object { Convert-ToScore $_.($column) }
    if ($values.Count -eq 0) { return 0.0 }
    return ($values | Measure-Object -Average).Average
}

$result = @()
foreach ($variantGroup in $variants) {
    $entry = [ordered]@{
        Variant = $variantGroup.Name
        Count = $variantGroup.Count
    }
    foreach ($column in $numericColumns) {
        $entry[$column] = [math]::Round((Get-Average $variantGroup.Group $column), 3)
    }
    $entry["overall"] = [math]::Round((($numericColumns | ForEach-Object { [double]$entry[$_] } | Measure-Object -Average).Average), 3)
    $result += [PSCustomObject]$entry
}

Write-Host "[HumanEval] Aggregate scores:"
$result | Sort-Object Variant | Format-Table -AutoSize

$helper = $result | Where-Object { $_.Variant -match "helper|a" } | Select-Object -First 1
if ($helper) {
    if ($helper.usefulness -lt 4.3) {
        Write-Warning "[HumanEval] Helper usefulness is below target 4.3 (actual: $($helper.usefulness))."
    }
    else {
        Write-Host "[HumanEval] Helper usefulness target met (>= 4.3)." -ForegroundColor Green
    }
}
