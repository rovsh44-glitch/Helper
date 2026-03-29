param(
    [string]$ApiBaseUrl = "http://127.0.0.1:5000",
    [string]$QueuePath = "D:\HELPER_DATA\indexing_queue.json",
    [string]$BseMarker = "Большая Советская Энциклопедия, 3-е изд, 30 томов, (1969-1981)",
    [int]$PollSeconds = 5
)

$ErrorActionPreference = "Stop"

function Get-ApiKey {
    $line = Get-Content ".env.local" | Where-Object { $_ -like "HELPER_API_KEY=*" } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($line)) {
        throw "HELPER_API_KEY is missing in .env.local."
    }

    return $line.Split("=", 2)[1]
}

function Invoke-HelperApi {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null
    )

    $headers = @{ "X-API-KEY" = $script:ApiKey }
    $uri = "$ApiBaseUrl$Path"
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }

    $json = $Body | ConvertTo-Json -Depth 8
    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body $json
}

function Get-Queue {
    if (-not (Test-Path $QueuePath)) {
        throw "Queue file not found: $QueuePath"
    }

    return Get-Content $QueuePath -Raw | ConvertFrom-Json -AsHashtable
}

function Get-BseTargets {
    $queue = Get-Queue
    return $queue.GetEnumerator() |
        Where-Object {
            $_.Key -like "*$BseMarker*" -and
            $_.Value -ne "Done" -and
            [System.IO.Path]::GetFileName($_.Key) -match '^Том\s'
        } |
        Sort-Object Key
}

function Wait-ForIdle {
    while ($true) {
        $status = Invoke-HelperApi -Method Get -Path "/api/evolution/status"
        if (-not $status.isIndexing) {
            return $status
        }

        Write-Host ("[{0}] Waiting for idle. CurrentTask={1} Progress={2}" -f (Get-Date -Format s), $status.activeTask, $status.fileProgress)
        Start-Sleep -Seconds $PollSeconds
    }
}

function Wait-ForFileCompletion {
    param(
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    while ($true) {
        Start-Sleep -Seconds $PollSeconds
        $status = Invoke-HelperApi -Method Get -Path "/api/evolution/status"
        $queue = Get-Queue
        $fileState = $queue[$TargetPath]

        Write-Host ("[{0}] State={1} ActiveTask={2} Progress={3}" -f (Get-Date -Format s), $fileState, $status.activeTask, $status.fileProgress)

        if ($fileState -eq "Done") {
            return
        }

        if ($fileState -like "Error*") {
            throw "Indexing failed for '$TargetPath': $fileState"
        }

        if (-not $status.isIndexing -and $fileState -ne "Processing") {
            throw "Indexing stopped unexpectedly for '$TargetPath'. QueueState=$fileState"
        }
    }
}

$script:ApiKey = Get-ApiKey
$targets = @(Get-BseTargets)
if ($targets.Count -eq 0) {
    Write-Host "No pending BSE files found."
    exit 0
}

Write-Host ("[{0}] BSE archive queue detected. RemainingFiles={1}" -f (Get-Date -Format s), $targets.Count)

foreach ($entry in $targets) {
    $targetPath = $entry.Key
    Wait-ForIdle | Out-Null

    Write-Host ("[{0}] Starting single-file archive indexing: {1}" -f (Get-Date -Format s), $targetPath)
    Invoke-HelperApi -Method Post -Path "/api/indexing/start" -Body @{
        TargetPath = $targetPath
        TargetDomain = $null
        SingleFileOnly = $true
    } | Out-Null

    Wait-ForFileCompletion -TargetPath $targetPath
    Write-Host ("[{0}] Completed: {1}" -f (Get-Date -Format s), $targetPath)
}

Write-Host ("[{0}] BSE archive indexing supervisor finished." -f (Get-Date -Format s))
