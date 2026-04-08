param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$NoRestore,
    [switch]$StopOnFirstFailure,
    [string]$RunRoot = "",
    [int]$MaxDurationSec = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedProjectPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
if (-not (Test-Path -LiteralPath $resolvedProjectPath)) {
    throw ("Project not found: {0}" -f $resolvedProjectPath)
}

if ([string]::IsNullOrWhiteSpace($RunRoot)) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedProjectPath)
    $RunRoot = Join-Path $repoRoot ("temp\{0}_file_runs_{1}" -f $projectName, (Get-Date -Format "yyyy-MM-dd_HH-mm-ss"))
}
elseif (-not [System.IO.Path]::IsPathRooted($RunRoot)) {
    $RunRoot = Join-Path $repoRoot $RunRoot
}

$logDirectory = Join-Path $RunRoot "logs"
$statusDirectory = Join-Path $RunRoot "status"
$argsDirectory = Join-Path $RunRoot "args"
$aggregateLogPath = Join-Path $RunRoot "live_run.log"
$summaryPath = Join-Path $RunRoot "summary.json"

foreach ($directory in @($RunRoot, $logDirectory, $statusDirectory, $argsDirectory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

function Write-LogLine {
    param([string]$Message)

    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-ddTHH:mm:ssK"), $Message
    $line | Tee-Object -FilePath $aggregateLogPath -Append
}

function Convert-ToRelativePath {
    param([string]$Path)

    $fullRoot = [System.IO.Path]::GetFullPath($repoRoot)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($fullRoot.Length).TrimStart('\').Replace('\', '/')
    }

    return $fullPath
}

function Write-Summary {
    param(
        [datetime]$StartedAt,
        [System.Collections.Generic.List[object]]$Entries,
        [string]$ProjectName
    )

    $payload = [ordered]@{
        startedAt = $StartedAt.ToString("o")
        updatedAt = (Get-Date).ToString("o")
        projectName = $ProjectName
        projectPath = Convert-ToRelativePath $resolvedProjectPath
        runRoot = $RunRoot
        aggregateLogPath = Convert-ToRelativePath $aggregateLogPath
        totalFiles = $Entries.Count
        passedFiles = @($Entries | Where-Object { $_.status -eq "passed" }).Count
        failedFiles = @($Entries | Where-Object { $_.status -eq "failed" }).Count
        skippedFiles = @($Entries | Where-Object { $_.status -eq "skipped" }).Count
        interruptedFiles = @($Entries | Where-Object { $_.status -eq "interrupted" }).Count
        runningFiles = @($Entries | Where-Object { $_.status -eq "running" }).Count
        files = @($Entries)
    }

    $payload | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding utf8
}

function Get-TestCompileFilePaths {
    param([string]$ProjectFilePath)

    [xml]$project = Get-Content -LiteralPath $ProjectFilePath -Raw -Encoding utf8
    $projectDirectory = Split-Path -Parent $ProjectFilePath
    return @(
        $project.SelectNodes('//Compile') |
            Where-Object { $_.Include -and $_.Include -like '*.cs' } |
            ForEach-Object { [System.IO.Path]::GetFullPath((Join-Path $projectDirectory $_.Include)) }
    )
}

function Get-TestDiscovery {
    param(
        [string]$ResolvedFilePath,
        [string]$ResolvedProjectPath
    )

    $discoveryJson = & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot "run_test_file.ps1") `
        -FilePath $ResolvedFilePath `
        -ProjectPath $ResolvedProjectPath `
        -Configuration $Configuration `
        -ListOnly `
        -EmitJson

    return $discoveryJson | ConvertFrom-Json
}

function Write-WrapperBootstrapStatus {
    param(
        [string]$ResolvedStatusPath,
        [string]$CommandDisplay,
        [string]$ResolvedLogPath,
        [string]$ResolvedErrorLogPath
    )

    $payload = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        startedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        lastHeartbeatUtc = (Get-Date).ToUniversalTime().ToString("o")
        phase = "launching"
        outcome = "RUNNING"
        details = "Launching wrapper process."
        command = $CommandDisplay
        processId = $null
        exitCode = $null
        artifacts = [ordered]@{
            logPath = Convert-ToRelativePath $ResolvedLogPath
            errorLogPath = Convert-ToRelativePath $ResolvedErrorLogPath
            statusPath = Convert-ToRelativePath $ResolvedStatusPath
        }
    }

    $payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ResolvedStatusPath -Encoding utf8
}

function Invoke-FileTestProcess {
    param(
        [string]$ResolvedProjectPath,
        [string]$ArgumentsJsonPath,
        [string]$FileLogPath,
        [string]$FileErrorLogPath,
        [string]$FileStatusPath
    )

    $wrapperScriptPath = Join-Path $PSScriptRoot "run_dotnet_test_with_monitor.ps1"
    $commandDisplay = ("& `"{0}`" -Target `"{1}`" -ArgumentsJsonPath `"{2}`" -LogPath `"{3}`" -ErrorLogPath `"{4}`" -StatusPath `"{5}`" -MaxDurationSec {6} -ReturnExitCode" -f $wrapperScriptPath, $ResolvedProjectPath, $ArgumentsJsonPath, $FileLogPath, $FileErrorLogPath, $FileStatusPath, $MaxDurationSec)
    Write-WrapperBootstrapStatus -ResolvedStatusPath $FileStatusPath -CommandDisplay $commandDisplay -ResolvedLogPath $FileLogPath -ResolvedErrorLogPath $FileErrorLogPath

    $null = & $wrapperScriptPath `
        -Target $ResolvedProjectPath `
        -ArgumentsJsonPath $ArgumentsJsonPath `
        -LogPath $FileLogPath `
        -ErrorLogPath $FileErrorLogPath `
        -StatusPath $FileStatusPath `
        -MaxDurationSec $MaxDurationSec `
        -ReturnExitCode

    return $(if ($null -eq $LASTEXITCODE) { 0 } else { [int]$LASTEXITCODE })
}

$projectName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedProjectPath)
$startedAt = Get-Date
$entries = New-Object System.Collections.Generic.List[object]
$filePaths = @(Get-TestCompileFilePaths -ProjectFilePath $resolvedProjectPath)

Write-LogLine ("Starting per-file project run. Project={0}; Files={1}" -f $projectName, $filePaths.Count)
Write-Summary -StartedAt $startedAt -Entries $entries -ProjectName $projectName

$currentEntry = $null

try {
    foreach ($filePath in $filePaths) {
        $leafName = Split-Path -Leaf $filePath
        $safeLeafName = $leafName -replace '[^A-Za-z0-9_.-]', '_'

        if ($leafName -notmatch '(?i)tests?(\.[A-Za-z0-9_]+)?\.cs$') {
            $skipEntry = [pscustomobject][ordered]@{
                file = Convert-ToRelativePath $filePath
                status = "skipped"
                reason = "support_file_name"
                startedAt = (Get-Date).ToString("o")
                completedAt = (Get-Date).ToString("o")
                durationSeconds = 0
                exitCode = 0
                logPath = $null
                errorLogPath = $null
                statusPath = $null
            }
            $entries.Add($skipEntry)
            Write-Summary -StartedAt $startedAt -Entries $entries -ProjectName $projectName
            Write-LogLine ("FILE SKIPPED (support file): {0}" -f $filePath)
            continue
        }

        $discovery = Get-TestDiscovery -ResolvedFilePath $filePath -ResolvedProjectPath $resolvedProjectPath
        if (-not $discovery.hasTests) {
            $skipEntry = [pscustomobject][ordered]@{
                file = Convert-ToRelativePath $filePath
                status = "skipped"
                reason = "no_test_classes"
                startedAt = (Get-Date).ToString("o")
                completedAt = (Get-Date).ToString("o")
                durationSeconds = 0
                exitCode = 0
                logPath = $null
                errorLogPath = $null
                statusPath = $null
            }
            $entries.Add($skipEntry)
            Write-Summary -StartedAt $startedAt -Entries $entries -ProjectName $projectName
            Write-LogLine ("FILE SKIPPED (no test classes): {0}" -f $filePath)
            continue
        }

        $fileLogPath = Join-Path $logDirectory ("{0}.log" -f $safeLeafName)
        $fileErrorLogPath = Join-Path $logDirectory ("{0}.stderr.log" -f $safeLeafName)
        $fileStatusPath = Join-Path $statusDirectory ("{0}.status.json" -f $safeLeafName)
        $argumentsJsonPath = Join-Path $argsDirectory ("{0}.args.json" -f $safeLeafName)

        $wrapperArguments = @("-c", $Configuration, "-m:1", "--filter", [string]$discovery.filter)
        if ($NoBuild) {
            $wrapperArguments += "--no-build"
        }
        if ($NoRestore) {
            $wrapperArguments += "--no-restore"
        }
        $wrapperArguments | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $argumentsJsonPath -Encoding utf8

        $currentEntry = [pscustomobject][ordered]@{
            file = Convert-ToRelativePath $filePath
            status = "running"
            reason = "running"
            startedAt = (Get-Date).ToString("o")
            completedAt = $null
            durationSeconds = $null
            exitCode = $null
            logPath = Convert-ToRelativePath $fileLogPath
            errorLogPath = Convert-ToRelativePath $fileErrorLogPath
            statusPath = Convert-ToRelativePath $fileStatusPath
            classNames = @($discovery.classNames)
            filter = [string]$discovery.filter
            childProcessId = $null
            wrapperLastHeartbeatUtc = $null
        }
        $entries.Add($currentEntry)
        Write-Summary -StartedAt $startedAt -Entries $entries -ProjectName $projectName
        Write-LogLine ("FILE start: {0}" -f $filePath)

        $caseStarted = Get-Date
        $exitCode = Invoke-FileTestProcess `
            -ResolvedProjectPath $resolvedProjectPath `
            -ArgumentsJsonPath $argumentsJsonPath `
            -FileLogPath $fileLogPath `
            -FileErrorLogPath $fileErrorLogPath `
            -FileStatusPath $fileStatusPath

        $currentEntry.completedAt = (Get-Date).ToString("o")
        $currentEntry.durationSeconds = [Math]::Round(((Get-Date) - $caseStarted).TotalSeconds, 2)
        $currentEntry.exitCode = $exitCode
        $currentEntry.status = if ($exitCode -eq 0) { "passed" } else { "failed" }
        $currentEntry.reason = if ($exitCode -eq 0) { "completed" } else { "non_zero_exit" }

        if (Test-Path -LiteralPath $fileStatusPath) {
            try {
                $fileStatus = Get-Content -LiteralPath $fileStatusPath -Raw -Encoding utf8 | ConvertFrom-Json
                $currentEntry.wrapperLastHeartbeatUtc = $fileStatus.lastHeartbeatUtc
                if ($fileStatus.outcome -eq "PASS") {
                    $currentEntry.status = "passed"
                    $currentEntry.reason = "completed"
                }
                elseif ($fileStatus.outcome -eq "FAIL") {
                    $currentEntry.status = "failed"
                    $currentEntry.reason = "wrapper_failed"
                }
            }
            catch {
            }
        }

        Write-Summary -StartedAt $startedAt -Entries $entries -ProjectName $projectName
        Write-LogLine ("FILE {0}: {1} (exitCode={2}, duration={3}s)" -f $currentEntry.status.ToUpperInvariant(), $filePath, $exitCode, $currentEntry.durationSeconds)

        if ($currentEntry.status -eq "failed" -and $StopOnFirstFailure) {
            Write-LogLine ("Stopping on first failing file: {0}" -f $filePath)
            exit 1
        }

        $currentEntry = $null
    }
}
finally {
    if ($null -ne $currentEntry -and $currentEntry.status -eq "running") {
        $currentEntry.completedAt = (Get-Date).ToString("o")
        $currentEntry.durationSeconds = [Math]::Round(((Get-Date) - [datetime]$currentEntry.startedAt).TotalSeconds, 2)
        $currentEntry.status = "interrupted"
        $currentEntry.reason = "parent_runner_interrupted"
        Write-Summary -StartedAt $startedAt -Entries $entries -ProjectName $projectName
        Write-LogLine ("FILE INTERRUPTED: {0}" -f $currentEntry.file)
    }
}

Write-LogLine "Completed per-file project run."
exit 0
