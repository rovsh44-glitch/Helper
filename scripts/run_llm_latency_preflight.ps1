param(
    [string]$OllamaBase = "http://localhost:11434",
    [int]$Attempts = 2,
    [int]$TimeoutSec = 45,
    [string]$Prompt = "Reply with one word: ready",
    [string]$ReportPath = "",
    [string[]]$CandidateModels = @()
)

$ErrorActionPreference = "Stop"

if ($Attempts -lt 1) { $Attempts = 1 }
if ($Attempts -gt 5) { $Attempts = 5 }
if ($TimeoutSec -lt 5) { $TimeoutSec = 5 }
if ($TimeoutSec -gt 180) { $TimeoutSec = 180 }

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $ReportPath = "doc/llm_latency_preflight_$stamp.md"
}

function Get-Percentile {
    param(
        [double[]]$Values,
        [double]$P
    )

    if (-not $Values -or $Values.Count -eq 0) {
        return $null
    }

    $sorted = $Values | Sort-Object
    $index = [int]([Math]::Ceiling($P * $sorted.Count) - 1)
    if ($index -lt 0) { $index = 0 }
    if ($index -ge $sorted.Count) { $index = $sorted.Count - 1 }
    return [double]$sorted[$index]
}

function Invoke-ModelProbe {
    param(
        [string]$BaseUrl,
        [string]$Model,
        [string]$TextPrompt,
        [int]$Timeout
    )

    $payload = @{
        model = $Model
        prompt = $TextPrompt
        stream = $false
        keep_alive = 120
        options = @{
            temperature = 0
            num_ctx = 2048
        }
    } | ConvertTo-Json -Depth 6 -Compress

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-RestMethod -Uri "$BaseUrl/api/generate" -Method Post -Body $payload -ContentType "application/json; charset=utf-8" -TimeoutSec $Timeout
        $sw.Stop()
        $text = ""
        if ($response -and $response.PSObject.Properties.Name -contains "response") {
            $text = [string]$response.response
        }

        return [PSCustomObject]@{
            Success = $true
            LatencyMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)
            ResponseLength = $text.Length
            Error = ""
        }
    }
    catch {
        $sw.Stop()
        return [PSCustomObject]@{
            Success = $false
            LatencyMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)
            ResponseLength = 0
            Error = $_.Exception.Message
        }
    }
}

function Resolve-Recommendation {
    param(
        [object[]]$Rows,
        [string]$Pattern
    )

    $successful = @($Rows | Where-Object { $_.SuccessRate -gt 0 })
    if ($successful.Count -eq 0) {
        return ""
    }

    $bucket = @($successful |
        Where-Object { $_.Model -match $Pattern } |
        Sort-Object P95Ms, P50Ms, AvgMs)
    if ($bucket.Count -gt 0) {
        return [string]$bucket[0].Model
    }

    $fallback = @($successful | Sort-Object P95Ms, P50Ms, AvgMs)
    return [string]$fallback[0].Model
}

Write-Host "[LLM-Preflight] Fetching model catalog from $OllamaBase ..."
$tags = Invoke-RestMethod -Uri "$OllamaBase/api/tags" -Method Get -TimeoutSec ([Math]::Min($TimeoutSec, 20))
$available = @($tags.models | ForEach-Object { $_.name } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

if (-not $available -or $available.Count -eq 0) {
    throw "[LLM-Preflight] No models returned by $OllamaBase/api/tags"
}

$candidates = New-Object System.Collections.Generic.List[string]
if ($CandidateModels -and $CandidateModels.Count -gt 0) {
    foreach ($candidate in $CandidateModels) {
        if ($available -contains $candidate) {
            $candidates.Add($candidate)
        }
    }
}
else {
    $priority = @(
        "qwen2.5-coder:14b",
        "command-r7b:7b",
        "deepseek-r1:8b",
        "deepseek-r1:14b",
        "qwen2.5vl:7b"
    )

    foreach ($name in $priority) {
        if (($available -contains $name) -and -not $candidates.Contains($name)) {
            $candidates.Add($name)
        }
    }

    foreach ($name in $available) {
        if ($candidates.Count -ge 6) { break }
        if (-not $candidates.Contains($name)) {
            $candidates.Add($name)
        }
    }
}

if ($candidates.Count -eq 0) {
    throw "[LLM-Preflight] No probe candidates after filtering."
}

$rows = @()
foreach ($model in $candidates) {
    Write-Host "[LLM-Preflight] Probing model: $model"
    $attemptResults = @()
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $probe = Invoke-ModelProbe -BaseUrl $OllamaBase -Model $model -TextPrompt $Prompt -Timeout $TimeoutSec
        $attemptResults += $probe
        if (-not $probe.Success) {
            Write-Warning "[LLM-Preflight] $model attempt $attempt failed: $($probe.Error)"
        }
    }

    $successful = @($attemptResults | Where-Object { $_.Success })
    $latencies = @($successful | ForEach-Object { [double]$_.LatencyMs })
    $successRate = if ($Attempts -eq 0) { 0 } else { [math]::Round(($successful.Count / $Attempts) * 100, 2) }

    $failedAttempts = @($attemptResults | Where-Object { -not $_.Success })
    $lastError = if ($failedAttempts.Count -gt 0) { [string]$failedAttempts[-1].Error } else { "" }

    $rows += [PSCustomObject]@{
        Model = $model
        Attempts = $Attempts
        SuccessCount = $successful.Count
        SuccessRate = $successRate
        AvgMs = if ($latencies.Count -gt 0) { [math]::Round((($latencies | Measure-Object -Average).Average), 2) } else { $null }
        P50Ms = if ($latencies.Count -gt 0) { [math]::Round((Get-Percentile -Values $latencies -P 0.5), 2) } else { $null }
        P95Ms = if ($latencies.Count -gt 0) { [math]::Round((Get-Percentile -Values $latencies -P 0.95), 2) } else { $null }
        LastError = $lastError
    }
}

$fastModel = Resolve-Recommendation -Rows $rows -Pattern "(?i)(1\.5b|7b|8b|mini|small|r7b)"
$coderModel = Resolve-Recommendation -Rows $rows -Pattern "(?i)(coder|code)"
$reasoningModel = Resolve-Recommendation -Rows $rows -Pattern "(?i)(r1|reason)"
$visionModel = Resolve-Recommendation -Rows $rows -Pattern "(?i)(vl|vision)"

$generatedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz"
$md = New-Object System.Collections.Generic.List[string]
$md.Add("# LLM Latency Preflight Report")
$md.Add("Generated at: $generatedAt")
$md.Add("Ollama base: $OllamaBase")
$md.Add("Attempts per model: $Attempts")
$md.Add("Timeout per attempt: ${TimeoutSec}s")
$md.Add("")
$md.Add("| Model | Attempts | Success | Success Rate (%) | Avg ms | P50 ms | P95 ms | Last Error |")
$md.Add("|---|---:|---:|---:|---:|---:|---:|---|")
foreach ($row in $rows | Sort-Object P95Ms, P50Ms, AvgMs) {
    $avg = if ($null -eq $row.AvgMs) { "n/a" } else { [string]$row.AvgMs }
    $p50 = if ($null -eq $row.P50Ms) { "n/a" } else { [string]$row.P50Ms }
    $p95 = if ($null -eq $row.P95Ms) { "n/a" } else { [string]$row.P95Ms }
    $err = if ([string]::IsNullOrWhiteSpace($row.LastError)) { "" } else { ($row.LastError -replace "\|", "/") }
    $md.Add("| $($row.Model) | $($row.Attempts) | $($row.SuccessCount) | $($row.SuccessRate) | $avg | $p50 | $p95 | $err |")
}

$md.Add("")
$md.Add("## Recommended env profile")
$md.Add("- HELPER_MODEL_FAST=$fastModel")
$md.Add("- HELPER_MODEL_CODER=$coderModel")
$md.Add("- HELPER_MODEL_REASONING=$reasoningModel")
$md.Add("- HELPER_MODEL_VISION=$visionModel")
$md.Add("")
$md.Add("## Notes")
$md.Add("- Recommendation rule: prefer successful model with lowest P95/P50/Avg in category bucket.")
$md.Add("- Empty value means no successful model matched that category pattern.")

$reportDir = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDir)) {
    New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
}

Set-Content -Path $ReportPath -Value ($md -join [Environment]::NewLine) -Encoding UTF8
$jsonPath = [System.IO.Path]::ChangeExtension($ReportPath, ".json")
$rows | ConvertTo-Json -Depth 6 | Set-Content -Path $jsonPath -Encoding UTF8

Write-Host "[LLM-Preflight] Report saved: $ReportPath"
Write-Host "[LLM-Preflight] JSON saved: $jsonPath"
