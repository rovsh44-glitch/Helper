param(
    [string]$HelperRoot = "",
    [string]$DataRoot = "",
    [int]$ApiPort = 5000,
    [int]$UiPort = 5173,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

$pathConfig = Get-HelperPathConfig -WorkspaceRoot (Join-Path $PSScriptRoot "..")

$smokePromptToken = "__HELPER_SMOKE_READY__"
$longSmokePromptToken = "__HELPER_SMOKE_LONG_STREAM__"

if ([string]::IsNullOrWhiteSpace($HelperRoot)) {
    $HelperRoot = $pathConfig.HelperRoot
}

if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    $DataRoot = $pathConfig.DataRoot
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $pathConfig.DocRoot ("perf_baseline_{0}.json" -f (Get-Date -Format "yyyy-MM-dd"))
}

$HelperRoot = [System.IO.Path]::GetFullPath($HelperRoot)
$DataRoot = [System.IO.Path]::GetFullPath($DataRoot)
$apiProjectRoot = $pathConfig.ApiProjectRoot
$apiBaseUrl = "http://localhost:$ApiPort"
$uiUrl = "http://127.0.0.1:$UiPort"
$logRoot = Join-Path $DataRoot "LOG"
$envFile = Join-Path $HelperRoot ".env.local"

New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Stop-HelperProcesses {
    Get-Process Helper.Api -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-CimInstance Win32_Process |
        Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'Helper\\.Api' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Get-CimInstance Win32_Process |
        Where-Object { $_.Name -eq 'node.exe' -and $_.CommandLine -match 'vite' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
}

function Wait-UntilReady {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 60
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        try {
            $readiness = Invoke-RestMethod -Method Get -Uri $Url -TimeoutSec 3
            if ([bool]$readiness.readyForChat) {
                $sw.Stop()
                return [pscustomobject]@{
                    ElapsedMs = [int]$sw.ElapsedMilliseconds
                    Snapshot = $readiness
                }
            }
        } catch {
        }

        Start-Sleep -Milliseconds 300
    }

    throw "Timed out waiting for readiness: $Url"
}

function Wait-UntilUiReady {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 45
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 3
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                $sw.Stop()
                return [int]$sw.ElapsedMilliseconds
            }
        } catch {
        }

        Start-Sleep -Milliseconds 300
    }

    throw "Timed out waiting for UI: $Url"
}

function Read-SseDoneChunk {
    param(
        [System.Net.Http.HttpClient]$Client,
        [System.Net.Http.HttpRequestMessage]$Request
    )

    $response = $Client.SendAsync($Request, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) {
        throw "SSE request failed with HTTP $($response.StatusCode)."
    }

    $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $reader = New-Object System.IO.StreamReader($stream)
    $tokenCount = 0
    $doneChunk = $null
    $ttftMs = $null
    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    while (-not $reader.EndOfStream) {
        $line = $reader.ReadLine()
        if ([string]::IsNullOrWhiteSpace($line) -or -not $line.StartsWith("data:")) {
            continue
        }

        $payload = $line.Substring(5).Trim()
        if ([string]::IsNullOrWhiteSpace($payload)) {
            continue
        }

        $chunk = $payload | ConvertFrom-Json
        if ($chunk.type -eq "token" -and $chunk.content) {
            $tokenCount += 1
            if ($null -eq $ttftMs) {
                $ttftMs = [int]$sw.ElapsedMilliseconds
            }
        }

        if ($chunk.type -eq "done") {
            $doneChunk = $chunk
            break
        }
    }

    $sw.Stop()
    Assert-Condition ($null -ne $doneChunk) "SSE stream completed without done chunk."
    return [pscustomobject]@{
        TokenCount = $tokenCount
        FirstTokenMs = if ($null -eq $ttftMs) { [int]$sw.ElapsedMilliseconds } else { $ttftMs }
        FullTurnMs = [int]$sw.ElapsedMilliseconds
        Done = $doneChunk
    }
}

function New-SessionHeaders {
    param(
        [string]$ApiBase
    )

    $body = @{ requestedScopes = @("chat:read", "chat:write") } | ConvertTo-Json -Depth 3
    $session = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/auth/session" -ContentType "application/json" -Body $body -TimeoutSec 30
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($session.accessToken)) "Session bootstrap returned empty token."

    return @{
        Authorization = "Bearer $($session.accessToken)"
    }
}

function Get-ApiProcessSnapshot {
    param(
        [int]$ProcessId = 0
    )

    $runtimeProcess = $null
    if ($ProcessId -gt 0) {
        $runtimeProcess = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    }

    if ($null -eq $runtimeProcess) {
        $process = Get-CimInstance Win32_Process |
            Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'Helper\\.Api' } |
            Select-Object -First 1

        if ($null -eq $process) {
            return $null
        }

        $ProcessId = [int]$process.ProcessId
        $runtimeProcess = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    }

    if ($null -eq $runtimeProcess) {
        return $null
    }

    $gpuMemoryMb = $null
    $nvidiaSmi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if ($null -ne $nvidiaSmi) {
        try {
            $gpuRows = & $nvidiaSmi.Source --query-compute-apps=pid,used_gpu_memory --format=csv,noheader,nounits 2>$null
            $match = $gpuRows | Where-Object { $_ -match "^\s*$ProcessId\s*," } | Select-Object -First 1
            if ($match) {
                $gpuMemoryMb = [int](($match -split ',')[1].Trim())
            }
        } catch {
        }
    }

    return [pscustomobject]@{
        ProcessId = $ProcessId
        CpuSeconds = [math]::Round($runtimeProcess.CPU, 2)
        WorkingSetMb = [math]::Round($runtimeProcess.WorkingSet64 / 1MB, 2)
        PrivateMemoryMb = [math]::Round($runtimeProcess.PrivateMemorySize64 / 1MB, 2)
        GpuMemoryMb = $gpuMemoryMb
    }
}

function Start-ApiProcess {
    param(
        [string]$LogPrefix
    )

    $stdoutPath = Join-Path $logRoot "${LogPrefix}_api_out.log"
    $stderrPath = Join-Path $logRoot "${LogPrefix}_api_err.log"
    return Start-Process -FilePath "dotnet" -ArgumentList "run" -WorkingDirectory $apiProjectRoot -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
}

function Start-UiProcess {
    param(
        [string]$LogPrefix
    )

    $stdoutPath = Join-Path $logRoot "${LogPrefix}_ui_out.log"
    $stderrPath = Join-Path $logRoot "${LogPrefix}_ui_err.log"
    return Start-Process -FilePath "cmd.exe" -ArgumentList "/c", "npm run dev -- --host 127.0.0.1 --port $UiPort --strictPort" -WorkingDirectory $HelperRoot -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
}

$env:HELPER_ROOT = $HelperRoot
Import-HelperEnvFile -Path $envFile
$env:HELPER_DATA_ROOT = $DataRoot
$env:HELPER_PROJECTS_ROOT = Join-Path $DataRoot "PROJECTS"
$env:HELPER_LIBRARY_ROOT = Join-Path $DataRoot "library"
$env:HELPER_LOGS_ROOT = Join-Path $DataRoot "LOG"
$env:HELPER_TEMPLATES_ROOT = Join-Path $env:HELPER_LIBRARY_ROOT "forge_templates"
$env:HELPER_MODEL_WARMUP_MODE = "minimal"
$env:HELPER_MODEL_PREFLIGHT_ENABLED = "false"

Stop-HelperProcesses

$apiProcess = $null
$uiProcess = $null
$warmApiProcess = $null
$httpClient = [System.Net.Http.HttpClient]::new()

try {
    $apiProcess = Start-ApiProcess -LogPrefix "perf_cold"
    $coldReady = Wait-UntilReady -Url "$apiBaseUrl/api/readiness"

    $uiProcess = Start-UiProcess -LogPrefix "perf_cold"
    $uiReadyMs = Wait-UntilUiReady -Url $uiUrl

    powershell -ExecutionPolicy Bypass -File (Join-Path $HelperRoot "scripts\run_runtime_smoke.ps1") -ApiBaseUrl $apiBaseUrl -UiUrl $uiUrl | Out-Null

    $headers = New-SessionHeaders -ApiBase $apiBaseUrl
    $metricsHeaders = if (-not [string]::IsNullOrWhiteSpace($env:HELPER_API_KEY)) {
        @{ "X-API-KEY" = $env:HELPER_API_KEY }
    } else {
        $headers
    }
    $chatBody = @{
        message = $smokePromptToken
        maxHistory = 4
        systemInstruction = "deterministic_smoke"
    } | ConvertTo-Json -Depth 4
    $chatResponse = Invoke-RestMethod -Method Post -Uri "$apiBaseUrl/api/chat" -Headers $headers -ContentType "application/json" -Body $chatBody -TimeoutSec 30
    $restoreStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $snapshot = Invoke-RestMethod -Method Get -Uri "$apiBaseUrl/api/chat/$($chatResponse.conversationId)" -Headers $headers -TimeoutSec 30
    $restoreStopwatch.Stop()
    Assert-Condition ($snapshot.messages.Count -ge 2) "Perf capture restore scenario returned incomplete snapshot."

    $longDescriptor = Invoke-RestMethod -Method Get -Uri "$apiBaseUrl/api/smoke/long" -TimeoutSec 30
    $longRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$apiBaseUrl/api/chat/stream")
    $longRequest.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $headers.Authorization.Substring("Bearer ".Length))
    $longRequest.Headers.Accept.Add([System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new("text/event-stream"))
    $longPayload = @{
        message = $longSmokePromptToken
        maxHistory = 4
        systemInstruction = "deterministic_smoke_long"
    } | ConvertTo-Json -Depth 4
    $longRequest.Content = [System.Net.Http.StringContent]::new($longPayload, [System.Text.Encoding]::UTF8, "application/json")
    $longStream = Read-SseDoneChunk -Client $httpClient -Request $longRequest
    Assert-Condition ($longStream.TokenCount -eq [int]$longDescriptor.chunkCount) "Perf capture long stream token count mismatch."

    $metrics = Invoke-RestMethod -Method Get -Uri "$apiBaseUrl/api/metrics" -Headers $metricsHeaders -TimeoutSec 30
    $processSnapshot = Get-ApiProcessSnapshot -ProcessId $apiProcess.Id

    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
        $apiProcess = $null
    }

    $warmApiProcess = Start-ApiProcess -LogPrefix "perf_warm"
    $warmReady = Wait-UntilReady -Url "$apiBaseUrl/api/readiness"

    $result = [ordered]@{
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        helperRoot = $HelperRoot
        dataRoot = $DataRoot
        startup = [ordered]@{
            coldReadyMs = [int]$coldReady.ElapsedMs
            warmReadyMs = [int]$warmReady.ElapsedMs
            uiReadyMs = [int]$uiReadyMs
            readinessPhase = $coldReady.Snapshot.phase
            warmupMode = $coldReady.Snapshot.warmupMode
        }
        restore = [ordered]@{
            conversationId = $chatResponse.conversationId
            snapshotMs = [int]$restoreStopwatch.ElapsedMilliseconds
        }
        streamPath = [ordered]@{
            deterministicLongChunkCount = [int]$longStream.TokenCount
            deterministicLongFirstTokenMs = [int]$longStream.FirstTokenMs
            deterministicLongFullTurnMs = [int]$longStream.FullTurnMs
            deterministicLongResponseLength = [int]$longStream.Done.fullResponse.Length
        }
        runtime = [ordered]@{
            avgFirstTokenLatencyMs = [double]$metrics.conversations.avgFirstTokenLatencyMs
            avgFullResponseLatencyMs = [double]$metrics.conversations.avgFullResponseLatencyMs
            budgetExceededRate = [double]$metrics.conversations.budgetExceededRate
        }
        stages = $metrics.conversationStages.stages
        process = $processSnapshot
    }

    $json = $result | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
    $json
}
finally {
    foreach ($proc in @($apiProcess, $uiProcess, $warmApiProcess)) {
        if ($null -ne $proc -and -not $proc.HasExited) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
    }

    Stop-HelperProcesses
}

