param(
    [Parameter(Mandatory = $true)][int]$RootProcessId,
    [string]$Title = "HELPER Process Monitor",
    [string]$StatusPath = "",
    [string]$LogPath = "",
    [int]$RefreshSec = 2
)

$ErrorActionPreference = "Continue"
Set-StrictMode -Version Latest

function Read-JsonFileOrNull {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or (-not (Test-Path $Path))) {
        return $null
    }

    try {
        return Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Get-OptionalObjectPropertyValue {
    param(
        [AllowNull()]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) {
            return $Object[$Name]
        }

        return $null
    }

    $property = @($Object.PSObject.Properties | Where-Object { $_.Name -eq $Name }) | Select-Object -First 1
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-ProcessTreeSnapshot {
    param([Parameter(Mandatory = $true)][int]$RootProcessId)

    $allProcesses = @()
    $processInventoryAvailable = $true
    try {
        $allProcesses = @(Get-CimInstance Win32_Process -ErrorAction Stop)
    }
    catch {
        try {
            $allProcesses = @(Get-WmiObject Win32_Process -ErrorAction Stop)
        }
        catch {
            $processInventoryAvailable = $false
        }
    }

    $liveProcessMap = @{}
    foreach ($process in @(Get-Process -ErrorAction SilentlyContinue)) {
        $liveProcessMap[$process.Id] = $process
    }

    if (-not $processInventoryAvailable) {
        if (-not $liveProcessMap.ContainsKey($RootProcessId)) {
            return @()
        }

        $root = $liveProcessMap[$RootProcessId]
        return @([ordered]@{
            ProcessId = $root.Id
            ParentProcessId = $null
            Name = $root.ProcessName + ".exe"
            CommandLine = ""
            StartTime = $root.StartTime
            CPU = $root.CPU
            Handles = $root.Handles
        })
    }

    $byParent = @{}
    foreach ($process in $allProcesses) {
        $parentKey = [string]$process.ParentProcessId
        if (-not $byParent.ContainsKey($parentKey)) {
            $byParent[$parentKey] = New-Object System.Collections.Generic.List[object]
        }
        $byParent[$parentKey].Add($process)
    }

    $queue = New-Object System.Collections.Generic.Queue[int]
    $seen = New-Object System.Collections.Generic.HashSet[int]
    $snapshot = New-Object System.Collections.Generic.List[object]

    $queue.Enqueue($RootProcessId)
    while ($queue.Count -gt 0) {
        $processId = $queue.Dequeue()
        if (-not $seen.Add($processId)) {
            continue
        }

        $current = @($allProcesses | Where-Object { $_.ProcessId -eq $processId }) | Select-Object -First 1
        if ($null -eq $current) {
            continue
        }

        $live = $null
        if ($liveProcessMap.ContainsKey($processId)) {
            $live = $liveProcessMap[$processId]
        }

        $snapshot.Add([ordered]@{
            ProcessId = $current.ProcessId
            ParentProcessId = $current.ParentProcessId
            Name = $current.Name
            CommandLine = [string]$current.CommandLine
            StartTime = if ($null -ne $live) { $live.StartTime } else { $null }
            CPU = if ($null -ne $live) { $live.CPU } else { $null }
            Handles = if ($null -ne $live) { $live.Handles } else { $null }
        }) | Out-Null

        $childKey = [string]$processId
        if ($byParent.ContainsKey($childKey)) {
            foreach ($child in $byParent[$childKey]) {
                $queue.Enqueue([int]$child.ProcessId)
            }
        }
    }

    return @($snapshot.ToArray())
}

function Resolve-RepoRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $RepoRoot $Path
}

function Get-ShortCommandLine {
    param([AllowNull()][string]$CommandLine)

    $text = [string]$CommandLine
    if ([string]::IsNullOrWhiteSpace($text)) {
        return ""
    }

    if ($text.Length -le 120) {
        return $text
    }

    return $text.Substring(0, 117) + "..."
}

function Show-StatusBlock {
    param([AllowNull()]$Status)

    if ($null -eq $Status) {
        Write-Host "Status file: unavailable" -ForegroundColor DarkYellow
        return
    }

    $phase = [string](Get-OptionalObjectPropertyValue -Object $Status -Name "phase")
    $outcome = [string](Get-OptionalObjectPropertyValue -Object $Status -Name "outcome")
    $heartbeat = [string](Get-OptionalObjectPropertyValue -Object $Status -Name "lastHeartbeatUtc")
    $details = [string](Get-OptionalObjectPropertyValue -Object $Status -Name "details")
    $command = [string](Get-OptionalObjectPropertyValue -Object $Status -Name "command")
    $processId = Get-OptionalObjectPropertyValue -Object $Status -Name "processId"
    $exitCode = Get-OptionalObjectPropertyValue -Object $Status -Name "exitCode"
    $currentStep = Get-OptionalObjectPropertyValue -Object $Status -Name "currentStep"
    $currentBatch = Get-OptionalObjectPropertyValue -Object $Status -Name "currentBatch"

    Write-Host ("Phase: {0} | Outcome: {1}" -f $phase, $outcome) -ForegroundColor Cyan
    Write-Host ("Heartbeat: {0}" -f $heartbeat)
    Write-Host ("Details: {0}" -f $details)

    if (-not [string]::IsNullOrWhiteSpace($command)) {
        Write-Host ("Command: {0}" -f $command)
    }

    if ($null -ne $processId) {
        Write-Host ("Reported processId: {0}" -f $processId)
    }

    if ($null -ne $exitCode) {
        Write-Host ("Exit code: {0}" -f $exitCode)
    }

    if ($null -ne $currentStep) {
        $stepId = [string](Get-OptionalObjectPropertyValue -Object $currentStep -Name "id")
        $stepState = [string](Get-OptionalObjectPropertyValue -Object $currentStep -Name "state")
        $stepElapsedMs = Get-OptionalObjectPropertyValue -Object $currentStep -Name "elapsedMs"
        $stepTitle = [string](Get-OptionalObjectPropertyValue -Object $currentStep -Name "title")
        $stepDetails = [string](Get-OptionalObjectPropertyValue -Object $currentStep -Name "details")
        Write-Host ("Current step: {0} [{1}] elapsed={2} ms" -f $stepId, $stepState, $stepElapsedMs) -ForegroundColor Yellow
        Write-Host ("Step title: {0}" -f $stepTitle)
        Write-Host ("Step details: {0}" -f $stepDetails)
    }
    else {
        Write-Host "Current step: none"
    }

    if ($null -ne $currentBatch) {
        $batchClassName = [string](Get-OptionalObjectPropertyValue -Object $currentBatch -Name "className")
        $batchIndex = Get-OptionalObjectPropertyValue -Object $currentBatch -Name "index"
        $batchTotal = Get-OptionalObjectPropertyValue -Object $currentBatch -Name "total"
        Write-Host ("Current batch: {0} ({1}/{2})" -f $batchClassName, $batchIndex, $batchTotal) -ForegroundColor Yellow
    }
}

function Show-LogTail {
    param(
        [Parameter(Mandatory = $true)][string]$ConfiguredLogPath,
        [AllowNull()]$Status
    )

    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $resolvedLogPath = $ConfiguredLogPath
    $currentStep = Get-OptionalObjectPropertyValue -Object $Status -Name "currentStep"
    $artifacts = Get-OptionalObjectPropertyValue -Object $Status -Name "artifacts"
    $currentStepId = [string](Get-OptionalObjectPropertyValue -Object $currentStep -Name "id")

    if ($null -ne $artifacts) {
        $candidateLogPath = $null
        if ($currentStepId -eq "tests") {
            $candidateLogPath = [string](Get-OptionalObjectPropertyValue -Object $artifacts -Name "dotnetTestLogPath")
        }

        if ([string]::IsNullOrWhiteSpace($candidateLogPath)) {
            $candidateLogPath = [string](Get-OptionalObjectPropertyValue -Object $artifacts -Name "logPath")
        }

        if (-not [string]::IsNullOrWhiteSpace($candidateLogPath)) {
            $resolvedLogPath = Resolve-RepoRelativePath -RepoRoot $repoRoot -Path $candidateLogPath
        }
    }

    if ([string]::IsNullOrWhiteSpace($resolvedLogPath) -or (-not (Test-Path $resolvedLogPath))) {
        Write-Host "Log: unavailable" -ForegroundColor DarkYellow
        return
    }

    Write-Host ""
    Write-Host ("Last log lines: {0}" -f $resolvedLogPath) -ForegroundColor Cyan
    foreach ($line in @(Get-Content -Path $resolvedLogPath -Tail 8 -ErrorAction SilentlyContinue)) {
        Write-Host $line
    }
}

$refreshSeconds = [Math]::Max(1, $RefreshSec)

while ($true) {
    $rootAlive = $null -ne (Get-Process -Id $RootProcessId -ErrorAction SilentlyContinue)
    $status = Read-JsonFileOrNull -Path $StatusPath
    $processSnapshot = @(Get-ProcessTreeSnapshot -RootProcessId $RootProcessId)

    Clear-Host
    Write-Host $Title -ForegroundColor Green
    Write-Host ("Updated: {0}" -f (Get-Date).ToString("yyyy-MM-dd HH:mm:ss"))
    Write-Host ("Root PID: {0} | Alive: {1}" -f $RootProcessId, $(if ($rootAlive) { "YES" } else { "NO" }))
    Write-Host ""

    Show-StatusBlock -Status $status
    Write-Host ""
    Write-Host "Tracked process tree:" -ForegroundColor Cyan
    if ($processSnapshot.Count -eq 0) {
        Write-Host "No tracked processes found."
    }
    else {
        $processSnapshot |
            Select-Object @{
                Name = "PID"
                Expression = { $_.ProcessId }
            }, @{
                Name = "PPID"
                Expression = { $_.ParentProcessId }
            }, @{
                Name = "Name"
                Expression = { $_.Name }
            }, @{
                Name = "CPU"
                Expression = { if ($null -eq $_.CPU) { "" } else { [math]::Round([double]$_.CPU, 2) } }
            }, @{
                Name = "Handles"
                Expression = { $_.Handles }
            }, @{
                Name = "StartTime"
                Expression = { if ($null -eq $_.StartTime) { "" } else { ([datetime]$_.StartTime).ToString("HH:mm:ss") } }
            }, @{
                Name = "CommandLine"
                Expression = { Get-ShortCommandLine -CommandLine $_.CommandLine }
            } |
            Format-Table -Wrap -AutoSize
    }

    Show-LogTail -ConfiguredLogPath $LogPath -Status $status

    if (-not $rootAlive) {
        Write-Host ""
        Write-Host "Root process exited. Window left open for inspection." -ForegroundColor Yellow
        break
    }

    Start-Sleep -Seconds $refreshSeconds
}
