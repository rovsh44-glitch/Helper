Set-StrictMode -Version Latest

$script:PreCertChildShell = $null
$script:PreCertTracePath = ""

function Get-PreCertApiKey {
    param(
        [string]$WorkspaceRoot,
        [string]$ExplicitApiKey
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitApiKey)) {
        return $ExplicitApiKey
    }

    if (-not [string]::IsNullOrWhiteSpace($env:HELPER_API_KEY)) {
        return $env:HELPER_API_KEY
    }

    $envFile = Join-Path $WorkspaceRoot ".env.local"
    if (-not (Test-Path $envFile)) {
        return ""
    }

    $line = Get-Content $envFile | Where-Object { $_ -match '^HELPER_API_KEY=' } | Select-Object -First 1
    if ($null -eq $line) {
        return ""
    }

    return $line.Substring("HELPER_API_KEY=".Length).Trim()
}

function Invoke-PreCertStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    try {
        Write-PreCertTrace -Message ("step_start name={0}" -f $Name)
        & $Action
        Write-PreCertTrace -Message ("step_end name={0} status=PASS" -f $Name)
        return [pscustomobject]@{
            Name = $Name
            Status = "PASS"
            Notes = ""
        }
    }
    catch {
        $message = $_.Exception.Message.Replace("`r", " ").Replace("`n", " ")
        Write-PreCertTrace -Message ("step_end name={0} status=FAIL error={1}" -f $Name, $message)
        return [pscustomobject]@{
            Name = $Name
            Status = "FAIL"
            Notes = $_.Exception.Message
        }
    }
}

function Get-PreCertChildShell {
    if (-not [string]::IsNullOrWhiteSpace($script:PreCertChildShell)) {
        return $script:PreCertChildShell
    }

    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -ne $pwsh) {
        $script:PreCertChildShell = $pwsh.Source
        return $script:PreCertChildShell
    }

    $powershell = Get-Command powershell -ErrorAction Stop
    $script:PreCertChildShell = $powershell.Source
    return $script:PreCertChildShell
}

function Write-PreCertTrace {
    param([Parameter(Mandatory = $true)][string]$Message)

    if ([string]::IsNullOrWhiteSpace($script:PreCertTracePath)) {
        return
    }

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff K"
    Add-Content -Path $script:PreCertTracePath -Value ("[{0}] {1}" -f $timestamp, $Message) -Encoding UTF8
}

function Merge-PreCertStepLogs {
    param(
        [Parameter(Mandatory = $true)][string]$StdoutPath,
        [Parameter(Mandatory = $true)][string]$StderrPath,
        [Parameter(Mandatory = $true)][string]$CombinedPath
    )

    $combined = New-Object System.Collections.Generic.List[string]
    if (Test-Path $StdoutPath) {
        $stdoutLines = @(Get-Content -Path $StdoutPath -ErrorAction SilentlyContinue)
        if ($stdoutLines.Count -gt 0) {
            $combined.AddRange([string[]]$stdoutLines)
        }
    }

    if (Test-Path $StderrPath) {
        $stderrLines = @(Get-Content -Path $StderrPath -ErrorAction SilentlyContinue)
        if (($combined.Count -gt 0) -and ($stderrLines.Count -gt 0)) {
            $combined.Add("")
            $combined.Add("## stderr")
        }

        if ($stderrLines.Count -gt 0) {
            $combined.AddRange([string[]]$stderrLines)
        }
    }

    Set-Content -Path $CombinedPath -Value $combined -Encoding UTF8
}

function Invoke-PreCertChildScript {
    param(
        [Parameter(Mandatory = $true)][string]$StepName,
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$LogDirectory,
        [string]$CombinedLogPath = "",
        [string]$SuccessEvidencePath = "",
        [switch]$AllowNonZeroExitWhenEvidenceExists
    )

    New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null

    $stdoutPath = Join-Path $LogDirectory ($StepName + ".stdout.log")
    $stderrPath = Join-Path $LogDirectory ($StepName + ".stderr.log")
    $childShell = Get-PreCertChildShell
    $fullArguments = @("-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", $ScriptPath) + $Arguments

    Write-PreCertTrace -Message ("child_start step={0} file={1}" -f $StepName, $ScriptPath)
    $process = Start-Process `
        -FilePath $childShell `
        -ArgumentList $fullArguments `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru `
        -Wait

    if (-not [string]::IsNullOrWhiteSpace($CombinedLogPath)) {
        Merge-PreCertStepLogs -StdoutPath $stdoutPath -StderrPath $stderrPath -CombinedPath $CombinedLogPath
    }

    $exitCode = $process.ExitCode
    Write-PreCertTrace -Message ("child_end step={0} exit={1} stdout={2} stderr={3}" -f $StepName, $exitCode, $stdoutPath, $stderrPath)

    if (($exitCode -ne 0) -and $AllowNonZeroExitWhenEvidenceExists.IsPresent -and (-not [string]::IsNullOrWhiteSpace($SuccessEvidencePath)) -and (Test-Path $SuccessEvidencePath)) {
        Write-PreCertTrace -Message ("child_nonzero_accepted step={0} evidence={1}" -f $StepName, $SuccessEvidencePath)
        return
    }

    if ($exitCode -ne 0) {
        throw ("[PreCert] {0} failed with exit code {1}. stdout={2}; stderr={3}" -f $StepName, $exitCode, $stdoutPath, $stderrPath)
    }
}

function Copy-PreCertSeedParitySnapshots {
    param(
        [Parameter(Mandatory = $true)][string]$WorkspaceRoot,
        [Parameter(Mandatory = $true)][string]$SnapshotRoot
    )

    $sourceDaily = Join-Path $WorkspaceRoot "doc\parity_nightly\daily"
    $targetDaily = Join-Path $SnapshotRoot "daily"
    $targetHistory = Join-Path $SnapshotRoot "history"

    New-Item -ItemType Directory -Force -Path $targetDaily | Out-Null
    New-Item -ItemType Directory -Force -Path $targetHistory | Out-Null

    if (-not (Test-Path $sourceDaily)) {
        return
    }

    Get-ChildItem -Path $sourceDaily -Filter "*.json" -File | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $targetDaily $_.Name) -Force
    }
}

function ConvertTo-PreCertDayLabel {
    param([int]$Day)

    if ($Day -lt 1 -or $Day -gt 99) {
        throw "[PreCert] Day must be between 1 and 99."
    }

    return "day-" + $Day.ToString("00")
}

function Invoke-PreCertPackageSequence {
    param(
        [Parameter(Mandatory = $true)][string]$WorkspaceRoot,
        [Parameter(Mandatory = $true)][string]$LogDirectory,
        [Parameter(Mandatory = $true)][string]$ParityWorkload,
        [Parameter(Mandatory = $true)][string]$SmokeWorkload,
        [Parameter(Mandatory = $true)][string]$RuntimeDir,
        [Parameter(Mandatory = $true)][string]$ParityBatchReportPath,
        [Parameter(Mandatory = $true)][string]$ParityGateReportPath,
        [Parameter(Mandatory = $true)][string]$ParityWindowReportPath,
        [Parameter(Mandatory = $true)][string]$SmokeCompileReportPath,
        [Parameter(Mandatory = $true)][string]$EvalGateLogPath,
        [string]$LlmPreflightReportPath = "",
        [string]$ClosedLoopReportPath = "",
        [string]$EvalRealModelOutputPath = "",
        [string]$EvalRealModelErrorLogPath = "",
        [string]$EvalRealModelReadinessReportPath = "",
        [string]$HumanParityReportPath = "",
        [string]$SnapshotRoot = "",
        [string]$ApiBase = "http://127.0.0.1:5000",
        [string]$ApiKey = "",
        [int]$ParityRuns = 24,
        [int]$SmokeRuns = 50,
        [int]$EvalScenarios = 200,
        [int]$EvalMinScenarioCount = 200,
        [int]$TimeoutSec = 120,
        [int]$ApiReadyTimeoutSec = 600,
        [int]$ApiReadyPollIntervalMs = 2000,
        [int]$ParityLookbackHours = 24,
        [switch]$SkipEvalRealModel,
        [switch]$SkipHumanParity,
        [switch]$SkipClosedLoop,
        [switch]$SkipLlmPreflight
    )

    New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null

    $steps = New-Object System.Collections.Generic.List[object]

    if ((-not $SkipLlmPreflight.IsPresent) -and (-not [string]::IsNullOrWhiteSpace($LlmPreflightReportPath))) {
        $steps.Add((Invoke-PreCertStep -Name "llm_preflight" -Action {
            Invoke-PreCertChildScript `
                -StepName "llm_preflight" `
                -ScriptPath (Join-Path $WorkspaceRoot "scripts\run_llm_latency_preflight.ps1") `
                -Arguments @(
                    "-Attempts", "2",
                    "-TimeoutSec", "45",
                    "-ReportPath", $LlmPreflightReportPath
                ) `
                -WorkingDirectory $WorkspaceRoot `
                -LogDirectory $LogDirectory
        }))
    }

    $steps.Add((Invoke-PreCertStep -Name "parity_batch" -Action {
        Invoke-PreCertChildScript `
            -StepName "parity_batch" `
            -ScriptPath (Join-Path $WorkspaceRoot "scripts\run_parity_golden_batch.ps1") `
            -Arguments @(
                "-Runs", $ParityRuns.ToString(),
                "-TimeoutSec", $TimeoutSec.ToString(),
                "-WorkloadClass", $ParityWorkload,
                "-ReportPath", $ParityBatchReportPath
            ) `
            -WorkingDirectory $WorkspaceRoot `
            -LogDirectory $LogDirectory
    }))

    $steps.Add((Invoke-PreCertStep -Name "parity_gate" -Action {
        $args = @(
            "-ReportPath", $ParityGateReportPath,
            "-WorkloadClasses", $ParityWorkload,
            "-LookbackHours", $ParityLookbackHours.ToString()
        )

        if (-not [string]::IsNullOrWhiteSpace($SnapshotRoot)) {
            $args += @("-SnapshotRoot", $SnapshotRoot)
        }

        Invoke-PreCertChildScript `
            -StepName "parity_gate" `
            -ScriptPath (Join-Path $WorkspaceRoot "scripts\run_generation_parity_gate.ps1") `
            -Arguments $args `
            -WorkingDirectory $WorkspaceRoot `
            -LogDirectory $LogDirectory
    }))

    $steps.Add((Invoke-PreCertStep -Name "parity_window_raw" -Action {
        $args = @(
            "-ReportPath", $ParityWindowReportPath,
            "-WindowDays", "14"
        )

        if (-not [string]::IsNullOrWhiteSpace($SnapshotRoot)) {
            $args += @("-SnapshotRoot", $SnapshotRoot)
        }

        Invoke-PreCertChildScript `
            -StepName "parity_window_raw" `
            -ScriptPath (Join-Path $WorkspaceRoot "scripts\run_generation_parity_window_gate.ps1") `
            -Arguments $args `
            -WorkingDirectory $WorkspaceRoot `
            -LogDirectory $LogDirectory `
            -SuccessEvidencePath $ParityWindowReportPath `
            -AllowNonZeroExitWhenEvidenceExists
    }))

    $steps.Add((Invoke-PreCertStep -Name "smoke_compile" -Action {
        Invoke-PreCertChildScript `
            -StepName "smoke_compile" `
            -ScriptPath (Join-Path $WorkspaceRoot "scripts\run_smoke_generation_compile_pass.ps1") `
            -Arguments @(
                "-Runs", $SmokeRuns.ToString(),
                "-TimeoutSec", $TimeoutSec.ToString(),
                "-WorkloadClass", $SmokeWorkload,
                "-ReportPath", $SmokeCompileReportPath
            ) `
            -WorkingDirectory $WorkspaceRoot `
            -LogDirectory $LogDirectory
    }))

    if ((-not $SkipClosedLoop.IsPresent) -and (-not [string]::IsNullOrWhiteSpace($ClosedLoopReportPath))) {
        $steps.Add((Invoke-PreCertStep -Name "closed_loop" -Action {
            Invoke-PreCertChildScript `
                -StepName "closed_loop" `
                -ScriptPath (Join-Path $WorkspaceRoot "scripts\run_closed_loop_predictability.ps1") `
                -Arguments @(
                    "-ReportPath", $ClosedLoopReportPath
                ) `
                -WorkingDirectory $WorkspaceRoot `
                -LogDirectory $LogDirectory
        }))
    }

    $steps.Add((Invoke-PreCertStep -Name "eval_gate" -Action {
        Invoke-PreCertChildScript `
            -StepName "eval_gate" `
            -ScriptPath (Join-Path $WorkspaceRoot "scripts\run_eval_gate.ps1") `
            -WorkingDirectory $WorkspaceRoot `
            -LogDirectory $LogDirectory `
            -CombinedLogPath $EvalGateLogPath
    }))

    if (-not $SkipEvalRealModel.IsPresent) {
        if ([string]::IsNullOrWhiteSpace($ApiKey)) {
            throw "[PreCert] HELPER_API_KEY is required for eval real-model execution."
        }

        $evalArgs = @(
            "-ApiBase", $ApiBase,
            "-ApiKey", $ApiKey,
            "-MaxScenarios", $EvalScenarios.ToString(),
            "-MinScenarioCount", $EvalMinScenarioCount.ToString(),
            "-RequireApiReady",
            "-LaunchLocalApiIfUnavailable",
            "-ApiRuntimeDir", $RuntimeDir,
            "-ReadinessTimeoutSec", $ApiReadyTimeoutSec.ToString(),
            "-ReadinessPollIntervalMs", $ApiReadyPollIntervalMs.ToString(),
            "-OutputReport", $EvalRealModelOutputPath,
            "-ErrorLogPath", $EvalRealModelErrorLogPath
        )

        if (-not [string]::IsNullOrWhiteSpace($EvalRealModelReadinessReportPath)) {
            $evalArgs += @("-ReadinessReportPath", $EvalRealModelReadinessReportPath)
        }

        $steps.Add((Invoke-PreCertStep -Name "eval_real_model" -Action {
            Invoke-PreCertChildScript `
                -StepName "eval_real_model" `
                -ScriptPath (Join-Path $WorkspaceRoot "scripts\run_eval_real_model.ps1") `
                -Arguments $evalArgs `
                -WorkingDirectory $WorkspaceRoot `
                -LogDirectory $LogDirectory
        }))
    }

    if ((-not $SkipHumanParity.IsPresent) -and (-not [string]::IsNullOrWhiteSpace($HumanParityReportPath))) {
        $steps.Add((Invoke-PreCertStep -Name "human_parity" -Action {
            Invoke-PreCertChildScript `
                -StepName "human_parity" `
                -ScriptPath (Join-Path $WorkspaceRoot "scripts\generate_human_parity_report.ps1") `
                -Arguments @(
                    "-OutputReport", $HumanParityReportPath,
                    "-FailOnThresholds"
                ) `
                -WorkingDirectory $WorkspaceRoot `
                -LogDirectory $LogDirectory
        }))
    }

    return @($steps.ToArray())
}
