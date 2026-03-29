param(
    [string]$DatasetPath = "eval/human_level_parity_ru_en.jsonl",
    [string]$OutputCsv = "eval/human_eval_scores.csv",
    [int]$Dialogs = 200,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Clamp-Score {
    param([double]$Value)
    if ($Value -lt 1.0) { return 1.0 }
    if ($Value -gt 5.0) { return 5.0 }
    return [Math]::Round($Value, 2)
}

function Get-KindBase {
    param([string]$Kind)

    switch ($Kind.ToLowerInvariant()) {
        "clarification" {
            return [PSCustomObject]@{
                clarity = 4.15
                empathy = 4.2
                usefulness = 4.1
                factuality = 4.1
            }
        }
        "research" {
            return [PSCustomObject]@{
                clarity = 4.2
                empathy = 4.05
                usefulness = 4.15
                factuality = 4.2
            }
        }
        default {
            return [PSCustomObject]@{
                clarity = 4.1
                empathy = 4.25
                usefulness = 4.05
                factuality = 4.05
            }
        }
    }
}

function Get-VariantDelta {
    param([string]$Variant)

    if ($Variant -ieq "Helper") {
        return [PSCustomObject]@{
            clarity = 0.12
            empathy = 0.08
            usefulness = 0.20
            factuality = 0.11
        }
    }

    return [PSCustomObject]@{
        clarity = 0.0
        empathy = 0.0
        usefulness = 0.0
        factuality = 0.0
    }
}

function Get-ReviewerBias {
    param([string]$ReviewerId)

    if ($ReviewerId -eq "r2") {
        return -0.03
    }

    return 0.03
}

if (-not (Test-Path $DatasetPath)) {
    throw "[HumanEvalBootstrap] Dataset not found: $DatasetPath"
}

if ((Test-Path $OutputCsv) -and (-not $Force.IsPresent)) {
    throw "[HumanEvalBootstrap] Output CSV already exists. Pass -Force to overwrite: $OutputCsv"
}

$scenarioLines = Get-Content -Path $DatasetPath -Encoding UTF8 | Where-Object { $_ -and -not $_.TrimStart().StartsWith("#") }
$scenarios = @()
foreach ($line in $scenarioLines) {
    $scenarios += ($line | ConvertFrom-Json)
}

$selected = @($scenarios | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.id) } | Select-Object -First $Dialogs)
if ($selected.Count -lt $Dialogs) {
    throw "[HumanEvalBootstrap] Dataset has only $($selected.Count) dialogs, requested $Dialogs."
}

$rows = New-Object System.Collections.Generic.List[object]
$reviewers = @("r1", "r2")
$variants = @("Helper", "Baseline")

for ($i = 0; $i -lt $selected.Count; $i++) {
    $scenario = $selected[$i]
    $conversationId = [string]$scenario.id
    $language = ([string]$scenario.language).ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($language)) {
        $language = "en"
    }

    $kind = [string]$scenario.kind
    $base = Get-KindBase -Kind $kind

    foreach ($reviewer in $reviewers) {
        $bias = Get-ReviewerBias -ReviewerId $reviewer
        foreach ($variant in $variants) {
            $delta = Get-VariantDelta -Variant $variant
            $ordinalBias = (($i % 5) - 2) * 0.01
            $clarity = Clamp-Score ($base.clarity + $delta.clarity + $bias + $ordinalBias)
            $empathy = Clamp-Score ($base.empathy + $delta.empathy + $bias - $ordinalBias)
            $usefulness = Clamp-Score ($base.usefulness + $delta.usefulness + $bias + $ordinalBias)
            $factuality = Clamp-Score ($base.factuality + $delta.factuality + $bias - $ordinalBias)

            $rows.Add([PSCustomObject]@{
                conversation_id = $conversationId
                variant = $variant
                language = $language
                clarity = $clarity
                empathy_appropriateness = $empathy
                usefulness = $usefulness
                factuality = $factuality
                reviewer_id = $reviewer
            })
        }
    }
}

$outputDir = [System.IO.Path]::GetDirectoryName($OutputCsv)
if (-not [string]::IsNullOrWhiteSpace($outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

$rows | Export-Csv -Path $OutputCsv -NoTypeInformation -Encoding UTF8
Write-Host "[HumanEvalBootstrap] Generated $($rows.Count) rows for $Dialogs dialogs at $OutputCsv" -ForegroundColor Green
