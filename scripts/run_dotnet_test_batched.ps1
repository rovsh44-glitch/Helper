param(
    [Parameter(Mandatory = $true)][string]$Target,
    [string[]]$BaseArguments = @("--no-build", "--blame-hang", "--blame-hang-timeout", "2m"),
    [string]$LogPath = "temp/verification/dotnet_test.log",
    [string]$ErrorLogPath = "temp/verification/dotnet_test.stderr.log",
    [string]$StatusPath = "temp/verification/dotnet_test_status.json",
    [string]$ResultsRoot = "temp/verification/dotnet_test_batches",
    [string[]]$ClassNames = @(),
    [int]$MaxDurationSec = 900,
    [int]$ListTimeoutSec = 300
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-RepoRelativePathOrOriginal {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    if (-not $fullRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar) -and -not $fullRoot.EndsWith([System.IO.Path]::AltDirectorySeparatorChar)) {
        $fullRoot += [System.IO.Path]::DirectorySeparatorChar
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($fullRoot.Length).Replace('\', '/')
    }

    return $fullPath
}

function Write-SuiteLog {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [string]$Color = "Gray"
    )

    Write-Host $Message -ForegroundColor $Color
    Add-Content -Path $script:ResolvedLogPath -Value $Message -Encoding UTF8 -ErrorAction SilentlyContinue
}

function Write-SuiteStatusFile {
    param(
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][string]$Outcome,
        [Parameter(Mandatory = $true)][string]$Details,
        [AllowNull()]$CurrentBatch = $null,
        [AllowNull()][int]$ExitCode = $null
    )

    $payload = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        startedAtUtc = $script:StartedAtUtc.ToString("o")
        lastHeartbeatUtc = (Get-Date).ToUniversalTime().ToString("o")
        phase = $Phase
        outcome = $Outcome
        details = $Details
        command = $script:CommandDisplay
        processId = $PID
        exitCode = $ExitCode
        mode = "class_batches"
        batchCount = $script:ClassList.Count
        artifacts = $script:Artifacts
        currentBatch = $CurrentBatch
        completedBatches = @($script:CompletedBatches.ToArray())
    }

    Set-Content -Path $script:ResolvedStatusPath -Value ($payload | ConvertTo-Json -Depth 12) -Encoding UTF8
}

function Get-ResolvedDotnetPath {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    }

    if ($null -eq $dotnetCommand) {
        throw "[DotnetTestBatch] dotnet executable not found."
    }

    return $dotnetCommand.Source
}

function TryGet-TestClassNameFromListLine {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Line)

    $trimmed = $Line.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $null
    }

    if ($trimmed.StartsWith("Test run for ", [System.StringComparison]::Ordinal) -or
        $trimmed.StartsWith("VSTest version ", [System.StringComparison]::Ordinal) -or
        $trimmed.StartsWith("The following Tests are available:", [System.StringComparison]::Ordinal)) {
        return $null
    }

    $signature = $trimmed
    $parameterListIndex = $signature.IndexOf('(')
    if ($parameterListIndex -gt 0) {
        $signature = $signature.Substring(0, $parameterListIndex).Trim()
    }

    if (-not ($signature -match '^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$')) {
        return $null
    }

    $lastDotIndex = $signature.LastIndexOf('.')
    if ($lastDotIndex -lt 1) {
        return $null
    }

    return $signature.Substring(0, $lastDotIndex)
}

function Get-TestClassList {
    param(
        [Parameter(Mandatory = $true)][string]$DotnetPath,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][int]$TimeoutSec,
        [string[]]$ExplicitClassNames = @()
    )

    if ($ExplicitClassNames.Count -gt 0) {
        return @(
            $ExplicitClassNames |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                ForEach-Object { $_.Trim() } |
                Sort-Object -Unique
        )
    }

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $process.StartInfo.FileName = $DotnetPath
    $process.StartInfo.Arguments = ('test "{0}" --no-build --list-tests' -f $TargetPath)
    $process.StartInfo.WorkingDirectory = $RepoRoot
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true

    $null = $process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSec * 1000)) {
        try {
            $process.Kill()
        }
        catch {
        }

        throw "[DotnetTestBatch] dotnet test --list-tests timed out after $TimeoutSec seconds."
    }

    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
        $combined = @($stdout, $stderr) -join [Environment]::NewLine
        throw "[DotnetTestBatch] dotnet test --list-tests failed with exit code $($process.ExitCode). $combined"
    }

    $classes = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($stdout -split "`r?`n")) {
        $className = TryGet-TestClassNameFromListLine -Line $line
        if ([string]::IsNullOrWhiteSpace($className)) {
            continue
        }

        $classes.Add($className) | Out-Null
    }

    return @($classes | Sort-Object -Unique)
}

function Get-SafeBatchName {
    param([Parameter(Mandatory = $true)][string]$ClassName)

    $safe = $ClassName.Replace('.', '_')
    foreach ($char in [System.IO.Path]::GetInvalidFileNameChars()) {
        $safe = $safe.Replace([string]$char, "_")
    }

    return $safe
}

function Get-FreshArtifactPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $Path
    }

    try {
        Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
        return $Path
    }
    catch {
        $directory = Split-Path -Parent $Path
        $fileName = [System.IO.Path]::GetFileNameWithoutExtension($Path)
        $extension = [System.IO.Path]::GetExtension($Path)
        $suffix = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
        $fallbackPath = Join-Path $directory ("{0}.{1}{2}" -f $fileName, $suffix, $extension)
        Write-Host ("[DotnetTestBatch] Reusing alternate artifact path because stale file is locked: {0} -> {1}" -f $Path, $fallbackPath) -ForegroundColor Yellow
        return $fallbackPath
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$script:ResolvedLogPath = if ([System.IO.Path]::IsPathRooted($LogPath)) { $LogPath } else { Join-Path $repoRoot $LogPath }
$resolvedErrorLogPath = if ([System.IO.Path]::IsPathRooted($ErrorLogPath)) { $ErrorLogPath } else { Join-Path $repoRoot $ErrorLogPath }
$script:ResolvedStatusPath = if ([System.IO.Path]::IsPathRooted($StatusPath)) { $StatusPath } else { Join-Path $repoRoot $StatusPath }
$resolvedResultsRoot = if ([System.IO.Path]::IsPathRooted($ResultsRoot)) { $ResultsRoot } else { Join-Path $repoRoot $ResultsRoot }
$wrapperScriptPath = Join-Path $PSScriptRoot "run_dotnet_test_with_monitor.ps1"
$dotnetPath = Get-ResolvedDotnetPath

$script:ResolvedLogPath = Get-FreshArtifactPath -Path $script:ResolvedLogPath
$resolvedErrorLogPath = Get-FreshArtifactPath -Path $resolvedErrorLogPath
$script:ResolvedStatusPath = Get-FreshArtifactPath -Path $script:ResolvedStatusPath

foreach ($path in @($script:ResolvedLogPath, $resolvedErrorLogPath, $script:ResolvedStatusPath, $resolvedResultsRoot)) {
    $directory = if (Test-Path $path -PathType Container) { $path } else { Split-Path -Parent $path }
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
}

$script:StartedAtUtc = (Get-Date).ToUniversalTime()
$script:CompletedBatches = New-Object System.Collections.Generic.List[object]
$script:CommandDisplay = ("dotnet test {0} {1} [batched by class]" -f $Target, ($BaseArguments -join " "))
$script:ClassList = @(Get-TestClassList -DotnetPath $dotnetPath -RepoRoot $repoRoot -TargetPath $Target -TimeoutSec $ListTimeoutSec -ExplicitClassNames $ClassNames)
$script:Artifacts = [ordered]@{
    logPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $script:ResolvedLogPath
    errorLogPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedErrorLogPath
    statusPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $script:ResolvedStatusPath
    resultsRoot = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $resolvedResultsRoot
}

if ($script:ClassList.Count -eq 0) {
    throw "[DotnetTestBatch] No test classes were discovered."
}

Write-SuiteStatusFile -Phase "starting" -Outcome "RUNNING" -Details ("Discovered {0} test classes." -f $script:ClassList.Count)
Write-SuiteLog -Message ("[DotnetTestBatch][START] {0} classes discovered." -f $script:ClassList.Count) -Color Cyan

for ($index = 0; $index -lt $script:ClassList.Count; $index++) {
    $className = [string]$script:ClassList[$index]
    $safeBatchName = Get-SafeBatchName -ClassName $className
    $batchRoot = Join-Path $resolvedResultsRoot $safeBatchName
    $batchLogPath = Join-Path $batchRoot "stdout.log"
    $batchErrorLogPath = Join-Path $batchRoot "stderr.log"
    $batchStatusPath = Join-Path $batchRoot "status.json"
    $batchTrxName = "$safeBatchName.trx"
    $batchArgs = @($BaseArguments + @(
        "--filter", "FullyQualifiedName~$className",
        "--results-directory", $batchRoot,
        "--logger", "trx;LogFileName=$batchTrxName"
    ))

    $currentBatch = [ordered]@{
        className = $className
        batchName = $safeBatchName
        index = ($index + 1)
        total = $script:ClassList.Count
        startedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }

    Write-SuiteStatusFile -Phase "running" -Outcome "RUNNING" -Details ("Running batch {0}/{1}: {2}" -f ($index + 1), $script:ClassList.Count, $className) -CurrentBatch $currentBatch
    Write-SuiteLog -Message ("[DotnetTestBatch][START][{0}/{1}] {2}" -f ($index + 1), $script:ClassList.Count, $className) -Color Cyan

    $batchStartedAt = Get-Date
    & $wrapperScriptPath -Target $Target -Arguments $batchArgs -LogPath $batchLogPath -ErrorLogPath $batchErrorLogPath -StatusPath $batchStatusPath -MaxDurationSec $MaxDurationSec
    $batchExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    $batchDurationMs = [int]((Get-Date) - $batchStartedAt).TotalMilliseconds
    $batchStatus = if (Test-Path $batchStatusPath) { Get-Content -Path $batchStatusPath -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
    $batchOutcome = if ($null -eq $batchStatus) { "UNKNOWN" } else { [string]$batchStatus.outcome }

    $completedBatch = [ordered]@{
        className = $className
        batchName = $safeBatchName
        outcome = $batchOutcome
        exitCode = $batchExitCode
        durationMs = $batchDurationMs
        logPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $batchLogPath
        errorLogPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $batchErrorLogPath
        statusPath = Get-RepoRelativePathOrOriginal -RepoRoot $repoRoot -Path $batchStatusPath
        completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }
    $script:CompletedBatches.Add($completedBatch) | Out-Null

    if ($batchExitCode -ne 0 -or -not [string]::Equals($batchOutcome, "PASS", [System.StringComparison]::OrdinalIgnoreCase)) {
        $stderrTail = if (Test-Path $batchErrorLogPath) {
            (@(Get-Content -Path $batchErrorLogPath -Tail 20 -ErrorAction SilentlyContinue) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) -join " | "
        }
        else {
            ""
        }

        if (-not [string]::IsNullOrWhiteSpace($stderrTail)) {
            Add-Content -Path $resolvedErrorLogPath -Value ("[{0}] {1}" -f $className, $stderrTail) -Encoding UTF8 -ErrorAction SilentlyContinue
        }

        $failureDetails = "Batch failed for $className with exit code $batchExitCode."
        if (-not [string]::IsNullOrWhiteSpace($stderrTail)) {
            $failureDetails += " stderr: $stderrTail"
        }

        Write-SuiteLog -Message ("[DotnetTestBatch][FAIL][{0}/{1}] {2} :: exit={3}" -f ($index + 1), $script:ClassList.Count, $className, $batchExitCode) -Color Red
        Write-SuiteStatusFile -Phase "failed" -Outcome "FAIL" -Details $failureDetails -ExitCode $batchExitCode
        exit $batchExitCode
    }

    Write-SuiteLog -Message ("[DotnetTestBatch][PASS][{0}/{1}] {2} :: {3} ms" -f ($index + 1), $script:ClassList.Count, $className, $batchDurationMs) -Color Green
}

Write-SuiteLog -Message ("[DotnetTestBatch][PASS] All {0} class batches passed." -f $script:ClassList.Count) -Color Green
Write-SuiteStatusFile -Phase "completed" -Outcome "PASS" -Details ("dotnet test completed successfully across {0} class batches." -f $script:ClassList.Count) -ExitCode 0
exit 0
