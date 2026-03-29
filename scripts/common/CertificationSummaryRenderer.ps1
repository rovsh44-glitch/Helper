Set-StrictMode -Version Latest

function Get-ExecutionBoardProgressMap {
    param([Parameter(Mandatory = $true)][string]$BoardPath)

    $map = [ordered]@{}
    if (-not (Test-Path $BoardPath)) {
        return $map
    }

    foreach ($line in Get-Content -Path $BoardPath -Encoding UTF8) {
        if ($line -match '^- `(?<id>W\d-PR\d+)`: (?<status>.+?)\.$') {
            $map[$Matches["id"]] = $Matches["status"].Trim().ToLowerInvariant()
        }
    }

    return $map
}

function Get-ExecutionBoardStatus {
    param([Parameter(Mandatory = $true)][string]$BoardPath)

    $progress = Get-ExecutionBoardProgressMap -BoardPath $BoardPath
    if ($progress.Count -eq 0) {
        return "unknown"
    }

    foreach ($wave in 1..3) {
        $waveStatuses = @(
            $progress.Keys |
                Where-Object { $_ -like ("W{0}-*" -f $wave) } |
                ForEach-Object { [string]$progress[$_] }
        )
        if ($waveStatuses.Count -eq 0) {
            continue
        }

        $allCompleted = (@($waveStatuses | Where-Object { $_ -like "completed*" }).Count -eq $waveStatuses.Count)
        if (-not $allCompleted) {
            return ("wave{0}_in_progress" -f $wave)
        }
    }

    return "complete"
}

function ConvertTo-CountedDaySummaryMarkdown {
    param([Parameter(Mandatory = $true)]$Summary)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add(('# DAILY_CERT_SUMMARY_day{0}' -f $Summary.DayText))
    $lines.Add("")
    $lines.Add(('Generated: `{0}`' -f $Summary.ExecutionDateIso))
    $lines.Add(('Cycle: `{0}`' -f $Summary.CycleId))
    $lines.Add(('Day: `{0}`' -f $Summary.DayText))
    $lines.Add(('Real calendar execution date: `{0}`' -f $Summary.ExecutionDateIso))
    $lines.Add(('Earliest official Day 01: `{0}`' -f $Summary.EarliestOfficialDay01Utc))
    $lines.Add(('Earliest official Day 02: `{0}`' -f $Summary.EarliestOfficialDay02Utc))
    $lines.Add(('Official window still closed: `{0}`' -f $(if ($Summary.IsOfficialWindowOpen) { "NO" } else { "YES" })))
    $lines.Add("")
    $lines.Add("## Package Status")
    $lines.Add("")
    $lines.Add("| Package | Status | Evidence | Key notes |")
    $lines.Add("|---|---|---|---|")
    foreach ($package in @($Summary.Packages)) {
        $lines.Add(('| {0} | `{1}` | `{2}` | {3} |' -f $package.Name, $package.Status, $package.Evidence, $package.Notes.Replace("|", "/")))
    }
    $lines.Add("")
    $lines.Add("## KPI Snapshot")
    $lines.Add("")
    $lines.Add(('1. Golden Hit Rate: `{0}`' -f $Summary.ParityGoldenHitRate))
    $lines.Add(('2. Generation Success Rate: `{0}`' -f $Summary.ParitySuccessRate))
    $lines.Add(('3. P95 Ready: `{0}`' -f $Summary.ParityP95Ready))
    $lines.Add(('4. Unknown Error Rate: `{0}`' -f $Summary.ParityUnknownErrorRate))
    $lines.Add(('5. Smoke compile pass rate: `{0}` (`{1}/{2}`)' -f $Summary.SmokePassRate, $Summary.SmokeCompilePass, $Summary.SmokeRuns))
    $lines.Add(('6. Smoke p95 duration: `{0}`' -f $Summary.SmokeP95Duration))
    $lines.Add(('7. Real-model eval: `{0}` scenario(s), runtime errors `{1}`, quality failures `{2}`, pass rate `{3}`' -f $Summary.EvalScenariosRun, $Summary.EvalRuntimeErrors, $Summary.EvalQualityFailures, $Summary.EvalPassRate))
    $lines.Add(('8. Human parity sample: `{0}`, status `{1}`' -f $Summary.HumanSampleSize, $Summary.HumanSampleStatus))
    if ($Summary.ParitySnapshotUsedFallback) {
        $lines.Add(('9. Parity snapshot source fallback: `{0}`' -f $Summary.ParitySnapshotFile))
    }
    $lines.Add("")
    $lines.Add("## Governance Checks")
    $lines.Add("")
    $lines.Add(('1. Sequential day guard satisfied: `{0}`' -f $Summary.SequentialGuard))
    $lines.Add(('2. Real calendar date guard satisfied: `{0}`' -f $Summary.CalendarGuard))
    $lines.Add(('3. `HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE=false`: `{0}`' -f $Summary.StrictWindowGuard))
    $lines.Add(('4. Soft bypass flags used: `{0}`' -f $Summary.SoftBypassFlagsUsed))
    $lines.Add(('5. Locked pre-cert anchor reference: `{0}`' -f $Summary.CycleId))
    $lines.Add("")
    $lines.Add("## Interpretation")
    $lines.Add("")
    foreach ($item in @($Summary.InterpretationLines)) {
        $lines.Add(('1. {0}' -f $item))
    }
    $lines.Add("")
    $lines.Add("## Day Result")
    $lines.Add("")
    $lines.Add(('1. Day status: `{0}`' -f $Summary.Verdict))
    $lines.Add(('2. Pre-cert counter: `{0}`' -f $Summary.PreCertCounter))
    $lines.Add(('3. Official counter: `{0}`' -f $Summary.OfficialCounter))
    $lines.Add(('4. Release decision for next day: `{0}`' -f $Summary.ReleaseDecision))
    $lines.Add(('5. Next executable profile: `{0}`' -f $Summary.NextExecutableProfile))

    return ($lines -join "`r`n")
}

function ConvertTo-CountedDayReadmeMarkdown {
    param([Parameter(Mandatory = $true)]$Summary)

    $content = @()
    $content += "# Pre-Cert Counted Day"
    $content += ""
    $content += ('- Cycle: `{0}`' -f $Summary.CycleId)
    $content += ('- Day: `{0}`' -f $Summary.DayText)
    $content += ('- Calendar date: `{0}`' -f $Summary.ExecutionDateIso)
    $content += ('- Status: `{0}`' -f $Summary.Verdict)
    $content += ('- Counted parity workload: `{0}`' -f $Summary.ParityWorkload)
    $content += ('- Counted smoke workload: `{0}`' -f $Summary.SmokeWorkload)
    $content += ('- Earliest official Day 01: `{0}`' -f $Summary.EarliestOfficialDay01Utc)
    $content += ('- Earliest official Day 14: `{0}`' -f $Summary.EarliestOfficialDay14Utc)
    $content += ""
    $content += "## Guards"
    $content += ('1. Sequential close enforced: previous closed day must be `day-{0}`.' -f ($Summary.Day - 1).ToString("00"))
    $content += ('2. Real calendar date enforced: `startDateUtc + {0}` must equal `{1}`.' -f ($Summary.Day - 1), $Summary.ExecutionDateIso)
    $content += "3. Official parity history uses the shared `doc/parity_nightly/*` root; preview snapshot roots are not used."
    $content += "4. `HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE=false` remains mandatory."
    $content += ""
    $content += "## Primary artifacts"
    foreach ($artifact in @($Summary.PrimaryArtifacts | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })) {
        $content += ('1. `{0}`' -f $artifact)
    }

    return ($content -join "`r`n")
}

function Write-CountedDayArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$MarkdownPath,
        [Parameter(Mandatory = $true)][string]$JsonPath,
        [Parameter(Mandatory = $true)][string]$ReadmePath,
        [Parameter(Mandatory = $true)]$Summary
    )

    $summaryMarkdown = ConvertTo-CountedDaySummaryMarkdown -Summary $Summary
    $readmeMarkdown = ConvertTo-CountedDayReadmeMarkdown -Summary $Summary

    $jsonPayload = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }

    foreach ($property in $Summary.PSObject.Properties) {
        $jsonPayload[$property.Name] = $property.Value
    }

    Set-Content -Path $MarkdownPath -Value $summaryMarkdown -Encoding UTF8
    Set-Content -Path $ReadmePath -Value $readmeMarkdown -Encoding UTF8
    Set-Content -Path $JsonPath -Value ($jsonPayload | ConvertTo-Json -Depth 10) -Encoding UTF8
}

function ConvertTo-CurrentStateMarkdown {
    param(
        [Parameter(Mandatory = $true)]$Snapshot,
        [Parameter(Mandatory = $true)][string]$SnapshotPath
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# HELPER Current State")
    $lines.Add("")
    $lines.Add(('Generated on: `{0}`' -f $Snapshot.generatedOn))
    $lines.Add(('Source of truth snapshot: `{0}`' -f $SnapshotPath.Replace('\', '/')))
    $lines.Add(('Implementation baseline: `{0}`' -f $Snapshot.executionBoard.path.Replace('\', '/')))
    $lines.Add("")
    $lines.Add("## Topline")
    $lines.Add("")
    $lines.Add(('- Overall status: `{0}`' -f $Snapshot.overallStatus))
    $lines.Add(('- Release baseline: `{0}`' -f $Snapshot.releaseBaseline.status))
    $lines.Add(('- Certification status: `{0}`' -f $Snapshot.certification.status))
    $lines.Add(('- Next executable profile: `{0}`' -f $Snapshot.certification.nextExecutableProfile))
    $lines.Add(('- Release decision: `{0}`' -f $Snapshot.releaseDecision))
    $lines.Add("")
    $lines.Add("## Build Status")
    $lines.Add("")
    $lines.Add(('- Frontend build: `{0}`' -f $Snapshot.build.frontend.status))
    $lines.Add(('  Evidence: {0}' -f $Snapshot.build.frontend.evidence))
    $lines.Add(('- Backend build: `{0}`' -f $Snapshot.build.backendApi.status))
    $lines.Add(('  Evidence: {0}' -f $Snapshot.build.backendApi.evidence))
    $lines.Add(('- CLI build: `{0}`' -f $Snapshot.build.runtimeCli.status))
    $lines.Add(('  Evidence: {0}' -f $Snapshot.build.runtimeCli.evidence))
    $lines.Add("")
    $lines.Add("## Release Baseline")
    $lines.Add("")
    $lines.Add(('- Baseline status: `{0}`' -f $Snapshot.releaseBaseline.status))
    if (-not [string]::IsNullOrWhiteSpace([string]$Snapshot.releaseBaseline.path)) {
        $lines.Add(('  Evidence: `{0}`' -f $Snapshot.releaseBaseline.path.Replace('\', '/')))
    }
    $lines.Add(('  Decision: {0}' -f $Snapshot.releaseBaseline.releaseDecision))
    foreach ($gate in @($Snapshot.releaseBaseline.localVerification)) {
        $lines.Add(('- {0}: `{1}`' -f $gate.title, $gate.status))
        $lines.Add(('  Evidence: {0}' -f $gate.evidence))
    }
    $lines.Add("")
    $lines.Add("## Certification Status")
    $lines.Add("")
    $lines.Add(('- Active cycle: `{0}`' -f $Snapshot.certification.activeCycle))
    $lines.Add(('- Cycle status: `{0}`' -f $Snapshot.certification.cycleStatus))
    $lines.Add(('- Last result: `{0}`' -f $Snapshot.certification.lastResult))
    $lines.Add(('- Official window open: `{0}`' -f $(if ($Snapshot.certification.officialWindowOpen) { "YES" } else { "NO" })))
    $lines.Add(('- Operator checklist: `{0}`' -f $Snapshot.certification.operatorChecklist))
    $lines.Add("")
    $lines.Add("## Current Blockers")
    $lines.Add("")
    $index = 1
    foreach ($blocker in @($Snapshot.blockers)) {
        $lines.Add(('{0}. {1}' -f $index, $blocker))
        $index++
    }
    $lines.Add("")
    $lines.Add("## Active Evidence")
    $lines.Add("")
    $evidenceIndex = 1
    foreach ($evidencePath in @($Snapshot.certification.activeEvidence)) {
        $lines.Add(('{0}. `{1}`' -f $evidenceIndex, $evidencePath.Replace('\', '/')))
        $evidenceIndex++
    }

    return ($lines -join "`r`n")
}

function ConvertTo-CurrentCertStateMarkdown {
    param(
        [Parameter(Mandatory = $true)]$Snapshot,
        [Parameter(Mandatory = $true)][string]$SnapshotPath
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# HELPER Current Certification State")
    $lines.Add("")
    $lines.Add(('Generated on: `{0}`' -f $Snapshot.generatedOn))
    $lines.Add(('Snapshot: `{0}`' -f $SnapshotPath.Replace('\', '/')))
    $lines.Add("")
    $lines.Add("## Active Cycle")
    $lines.Add("")
    $lines.Add(('- Active cycle: `{0}`' -f $Snapshot.certification.activeCycle))
    $lines.Add(('- Cycle status: `{0}`' -f $Snapshot.certification.cycleStatus))
    $lines.Add(('- Last result: `{0}`' -f $Snapshot.certification.lastResult))
    $lines.Add(('- Closed days: `{0}/{1}`' -f $Snapshot.certification.closedDays, $Snapshot.certification.windowDays))
    $lines.Add(('- Earliest official Day 01: `{0}`' -f $Snapshot.certification.earliestOfficialDay01Utc))
    $lines.Add(('- Next executable profile: `{0}`' -f $Snapshot.certification.nextExecutableProfile))
    $lines.Add("")
    $lines.Add("## Release Baseline")
    $lines.Add("")
    $lines.Add(('- Baseline status: `{0}`' -f $Snapshot.releaseBaseline.status))
    $lines.Add(('- Release decision: `{0}`' -f $Snapshot.releaseDecision))
    if (-not [string]::IsNullOrWhiteSpace([string]$Snapshot.releaseBaseline.path)) {
        $lines.Add(('- Baseline artifact: `{0}`' -f $Snapshot.releaseBaseline.path.Replace('\', '/')))
    }
    $lines.Add("")
    $lines.Add("## Package Status")
    $lines.Add("")
    foreach ($packageKey in @("3.1", "3.2", "3.3", "3.4", "3.5")) {
        $status = $null
        if ($Snapshot.certification.packages -is [System.Collections.IDictionary]) {
            if ($Snapshot.certification.packages.Contains($packageKey)) {
                $status = $Snapshot.certification.packages[$packageKey]
            }
        }
        else {
            $statusProperty = @($Snapshot.certification.packages.PSObject.Properties | Where-Object { $_.Name -eq $packageKey }) | Select-Object -First 1
            if ($null -ne $statusProperty) {
                $status = $statusProperty.Value
            }
        }

        if ($null -ne $status) {
            $lines.Add(('- `{0}`: `{1}`' -f $packageKey, $status))
        }
    }
    $lines.Add("")
    $lines.Add("## Active Evidence Only")
    $lines.Add("")
    $evidenceIndex = 1
    foreach ($evidencePath in @($Snapshot.certification.activeEvidence)) {
        $lines.Add(('{0}. `{1}`' -f $evidenceIndex, $evidencePath.Replace('\', '/')))
        $evidenceIndex++
    }
    $lines.Add("")
    $lines.Add("## Interpretation")
    $lines.Add("")
    $interpretationIndex = 1
    foreach ($line in @($Snapshot.certification.interpretation)) {
        $lines.Add(('{0}. {1}' -f $interpretationIndex, $line))
        $interpretationIndex++
    }

    return ($lines -join "`r`n")
}

function Write-ActiveGateDocuments {
    param(
        [Parameter(Mandatory = $true)][string]$SnapshotPath,
        [Parameter(Mandatory = $true)][string]$CurrentStatePath,
        [Parameter(Mandatory = $true)][string]$CurrentCertStatePath,
        [string]$SnapshotDisplayPath = "",
        [Parameter(Mandatory = $true)]$Snapshot
    )

    $displayPath = if ([string]::IsNullOrWhiteSpace($SnapshotDisplayPath)) { $SnapshotPath } else { $SnapshotDisplayPath }

    Set-Content -Path $SnapshotPath -Value ($Snapshot | ConvertTo-Json -Depth 10) -Encoding UTF8
    Set-Content -Path $CurrentStatePath -Value (ConvertTo-CurrentStateMarkdown -Snapshot $Snapshot -SnapshotPath $displayPath) -Encoding UTF8
    Set-Content -Path $CurrentCertStatePath -Value (ConvertTo-CurrentCertStateMarkdown -Snapshot $Snapshot -SnapshotPath $displayPath) -Encoding UTF8
}
