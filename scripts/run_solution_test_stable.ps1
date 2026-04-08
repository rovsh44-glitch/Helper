param(
    [string]$RepoRoot = ".",
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore,
    [switch]$StopOnFirstFailure,
    [string]$RunRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$originalNativeErrorActionPreference = $null
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
    $originalNativeErrorActionPreference = $global:PSNativeCommandUseErrorActionPreference
    $global:PSNativeCommandUseErrorActionPreference = $false
}

$resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
if ([string]::IsNullOrWhiteSpace($RunRoot)) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $RunRoot = Join-Path $resolvedRepoRoot ("temp\\solution_test_matrix_{0}" -f $stamp)
}

$resolvedRunRoot = [System.IO.Path]::GetFullPath($RunRoot)
$logsRoot = Join-Path $resolvedRunRoot "logs"
$statusRoot = Join-Path $resolvedRunRoot "status"
$argsRoot = Join-Path $resolvedRunRoot "args"
$aggregateLogPath = Join-Path $resolvedRunRoot "live_run.log"
$summaryPath = Join-Path $resolvedRunRoot "run_summary.json"
$wrapperScriptPath = Join-Path $PSScriptRoot "run_dotnet_test_with_monitor.ps1"

New-Item -ItemType Directory -Force -Path $resolvedRunRoot | Out-Null
New-Item -ItemType Directory -Force -Path $logsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $statusRoot | Out-Null
New-Item -ItemType Directory -Force -Path $argsRoot | Out-Null

$projects = @(
    @{ Name = "Helper.Runtime.Tests"; Path = "test\\Helper.Runtime.Tests\\Helper.Runtime.Tests.csproj" },
    @{ Name = "Helper.RuntimeSlice.Api.Tests"; Path = "test\\Helper.RuntimeSlice.Api.Tests\\Helper.RuntimeSlice.Api.Tests.csproj" },
    @{ Name = "Helper.Runtime.Api.Tests"; Path = "test\\Helper.Runtime.Api.Tests\\Helper.Runtime.Api.Tests.csproj" },
    @{ Name = "Helper.Runtime.Integration.Tests"; Path = "test\\Helper.Runtime.Integration.Tests\\Helper.Runtime.Integration.Tests.csproj" },
    @{ Name = "Helper.Runtime.Browser.Tests"; Path = "test\\Helper.Runtime.Browser.Tests\\Helper.Runtime.Browser.Tests.csproj" },
    @{ Name = "Helper.Runtime.Certification.Tests"; Path = "test\\Helper.Runtime.Certification.Tests\\Helper.Runtime.Certification.Tests.csproj" },
    @{ Name = "Helper.Runtime.Certification.Compile.Tests"; Path = "test\\Helper.Runtime.Certification.Compile.Tests\\Helper.Runtime.Certification.Compile.Tests.csproj" }
)

$statuses = New-Object System.Collections.Generic.List[object]
$startedAt = Get-Date
$runnerEnvVarNames = @(
    "HELPER_DOTNET_TIMEOUT_SEC",
    "HELPER_DOTNET_RESTORE_TIMEOUT_SEC",
    "HELPER_DOTNET_BUILD_TIMEOUT_SEC",
    "HELPER_DOTNET_TEST_TIMEOUT_SEC",
    "HELPER_DOTNET_TRACE_HEARTBEAT_SEC",
    "HELPER_DOTNET_KILL_CONFIRM_TIMEOUT_SEC",
    "HELPER_CERTIFICATION_PROCESS_TRACE_PATH"
)

function Write-LogLine {
    param([string]$Message)

    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-ddTHH:mm:ssK"), $Message
    $line | Tee-Object -FilePath $aggregateLogPath -Append
}

function Append-LogFile {
    param(
        [string]$SourcePath,
        [string[]]$Targets
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        return
    }

    foreach ($line in Get-Content -LiteralPath $SourcePath -ErrorAction SilentlyContinue) {
        foreach ($target in $Targets) {
            Add-Content -LiteralPath $target -Value $line -Encoding utf8
        }
    }
}

function Read-JsonFileOrNull {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or (-not (Test-Path -LiteralPath $Path))) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Clear-RunnerAmbientEnvironment {
    param([string[]]$Names)

    $snapshot = @{}
    foreach ($name in $Names) {
        $snapshot[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
        [Environment]::SetEnvironmentVariable($name, $null, "Process")
    }

    return $snapshot
}

function Restore-RunnerAmbientEnvironment {
    param([hashtable]$Snapshot)

    foreach ($entry in $Snapshot.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
    }
}

function Write-Summary {
    param(
        [datetime]$StartedAt,
        [System.Collections.Generic.List[object]]$Statuses,
        [int]$PlannedTotalProjects,
        [string]$Path
    )

    $payload = [ordered]@{
        startedAt = $StartedAt.ToString("o")
        updatedAt = (Get-Date).ToString("o")
        runnerProcessId = $PID
        runnerProcessName = "powershell"
        runRoot = $resolvedRunRoot
        aggregateLogPath = $aggregateLogPath
        summaryPath = $summaryPath
        totalProjects = $PlannedTotalProjects
        passedProjects = @($Statuses | Where-Object { $_.status -eq "passed" }).Count
        failedProjects = @($Statuses | Where-Object { $_.status -eq "failed" }).Count
        runningProjects = @($Statuses | Where-Object { $_.status -eq "running" }).Count
        completedProjects = @($Statuses | Where-Object { $_.status -in @("passed", "failed") }).Count
        projects = @($Statuses)
    }

    $payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Path -Encoding utf8
}

Push-Location $resolvedRepoRoot
try {
    Write-LogLine ("Starting stable solution test matrix run at {0}" -f $resolvedRunRoot)
    Write-LogLine ("Configuration={0}; NoBuild={1}; NoRestore={2}; StopOnFirstFailure={3}" -f $Configuration, $NoBuild.IsPresent.ToString().ToLowerInvariant(), $NoRestore.IsPresent.ToString().ToLowerInvariant(), $StopOnFirstFailure.IsPresent.ToString().ToLowerInvariant())

    if (-not $NoBuild) {
        $buildArgs = @("build", "Helper.sln", "-c", $Configuration, "-m:1")
        if ($NoRestore) {
            $buildArgs += "--no-restore"
        }

        Write-LogLine ("BUILD start: dotnet {0}" -f ($buildArgs -join " "))
        & dotnet @buildArgs 2>&1 | Tee-Object -FilePath $aggregateLogPath -Append
        if ($LASTEXITCODE -ne 0) {
            Write-LogLine ("BUILD failed with exit code {0}" -f $LASTEXITCODE)
            throw "Stable solution test matrix build failed."
        }

        Write-LogLine "BUILD passed"
    }

    foreach ($project in $projects) {
        $projectLogPath = Join-Path $logsRoot ($project.Name + ".log")
        $projectErrorLogPath = Join-Path $logsRoot ($project.Name + ".stderr.log")
        $projectStatusPath = Join-Path $statusRoot ($project.Name + ".status.json")
        $projectArgumentsPath = Join-Path $argsRoot ($project.Name + ".arguments.json")
        $projectStatus = [ordered]@{
            name = $project.Name
            path = $project.Path
            startedAt = (Get-Date).ToString("o")
            completedAt = $null
            durationSeconds = $null
            exitCode = $null
            status = "running"
            logPath = $projectLogPath
            stdoutPath = $projectLogPath
            stderrPath = $projectErrorLogPath
            statusPath = $projectStatusPath
            argumentsPath = $projectArgumentsPath
            dotnetProcessId = $null
            wrapperOutcome = $null
            wrapperHeartbeatUtc = $null
        }

        $statusObject = [pscustomobject]$projectStatus
        $statuses.Add($statusObject)
        Write-Summary -StartedAt $startedAt -Statuses $statuses -PlannedTotalProjects $projects.Count -Path $summaryPath

        $testStartedAt = Get-Date
        $testArgs = @("test", $project.Path, "-c", $Configuration, "-m:1", "--logger", "console;verbosity=minimal")
        if ($NoBuild) {
            $testArgs += "--no-build"
        }
        if ($NoRestore) {
            $testArgs += "--no-restore"
        }

        Write-LogLine ("TEST start [{0}/{1}] {2}" -f ($statuses.Count), $projects.Count, $project.Name)
        Write-LogLine ("Command: dotnet {0}" -f ($testArgs -join " "))

        foreach ($path in @($projectLogPath, $projectErrorLogPath, $projectStatusPath, $projectArgumentsPath)) {
            if (Test-Path -LiteralPath $path) {
                Remove-Item -LiteralPath $path -Force
            }
        }

        $testArgs[2..($testArgs.Count - 1)] | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $projectArgumentsPath -Encoding utf8

        $ambientSnapshot = Clear-RunnerAmbientEnvironment -Names $runnerEnvVarNames
        try {
            try {
                & $wrapperScriptPath `
                    -Target $project.Path `
                    -ArgumentsJsonPath $projectArgumentsPath `
                    -LogPath $projectLogPath `
                    -ErrorLogPath $projectErrorLogPath `
                    -StatusPath $projectStatusPath `
                    -ReturnExitCode
                $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { [int]$LASTEXITCODE }
            }
            catch {
                $exitCode = 9001
                Add-Content -LiteralPath $projectErrorLogPath -Value ("[stable-runner-wrapper-failure] " + $_.Exception.Message) -Encoding utf8
            }
        }
        finally {
            Restore-RunnerAmbientEnvironment -Snapshot $ambientSnapshot
        }

        Append-LogFile -SourcePath $projectLogPath -Targets @($aggregateLogPath)
        Append-LogFile -SourcePath $projectErrorLogPath -Targets @($aggregateLogPath)

        $wrapperStatus = Read-JsonFileOrNull -Path $projectStatusPath

        $durationSeconds = [Math]::Round(((Get-Date) - $testStartedAt).TotalSeconds, 2)
        $statusObject.completedAt = (Get-Date).ToString("o")
        $statusObject.durationSeconds = $durationSeconds
        $statusObject.exitCode = $exitCode
        $statusObject.status = if ($exitCode -eq 0) { "passed" } else { "failed" }
        if ($null -ne $wrapperStatus) {
            $statusObject.dotnetProcessId = $wrapperStatus.processId
            $statusObject.wrapperOutcome = $wrapperStatus.outcome
            $statusObject.wrapperHeartbeatUtc = $wrapperStatus.lastHeartbeatUtc
        }
        Write-Summary -StartedAt $startedAt -Statuses $statuses -PlannedTotalProjects $projects.Count -Path $summaryPath

        Write-LogLine ("TEST {0} [{1}] duration={2}s exitCode={3}" -f $statusObject.status.ToUpperInvariant(), $project.Name, $durationSeconds, $exitCode)

        if ($exitCode -ne 0 -and $StopOnFirstFailure) {
            break
        }
    }

    $failedProjects = @($statuses | Where-Object { $_.status -eq "failed" })
    $totalDurationSeconds = [Math]::Round(((Get-Date) - $startedAt).TotalSeconds, 2)
    Write-LogLine ("Completed stable solution test matrix in {0}s; failedProjects={1}" -f $totalDurationSeconds, $failedProjects.Count)
    Write-Summary -StartedAt $startedAt -Statuses $statuses -PlannedTotalProjects $projects.Count -Path $summaryPath

    if ($failedProjects.Count -gt 0) {
        exit 1
    }

    exit 0
}
finally {
    if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
        $global:PSNativeCommandUseErrorActionPreference = $originalNativeErrorActionPreference
    }
    Pop-Location
}
