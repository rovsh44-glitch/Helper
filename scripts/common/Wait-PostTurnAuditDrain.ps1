function Wait-PostTurnAuditDrain {
    param(
        [Parameter(Mandatory = $true)][string]$ApiBase,
        [hashtable]$Headers = @{},
        [string]$AuditTracePath = "",
        [int]$ExpectedTraceEntries = 0,
        [int]$TimeoutSec = 20,
        [int]$PollIntervalMs = 750,
        [int]$StablePollsRequired = 2
    )

    $startedAtUtc = [datetimeoffset]::UtcNow
    $deadline = $startedAtUtc.AddSeconds([math]::Max(2, $TimeoutSec))
    $lastSnapshotSignature = ""
    $stablePolls = 0

    while ([datetimeoffset]::UtcNow -lt $deadline) {
        $pollRecordedAtUtc = [datetimeoffset]::UtcNow
        $controlPlane = $null
        $traceLineCount = -1
        $traceExists = $false
        $readStatus = "control_plane_unreachable"

        try {
            $controlPlane = Invoke-RestMethod -Uri ("{0}/api/control-plane" -f $ApiBase.TrimEnd('/')) -Method Get -Headers $Headers -TimeoutSec 10
            $readStatus = "ok"
        }
        catch {
            $readStatus = "control_plane_failed"
        }

        if (-not [string]::IsNullOrWhiteSpace($AuditTracePath) -and (Test-Path $AuditTracePath)) {
            $traceExists = $true
            try {
                $traceLineCount = [System.IO.File]::ReadAllLines($AuditTracePath).Length
            }
            catch {
                $traceLineCount = -1
            }
        }

        $auditQueue = if ($null -ne $controlPlane) { $controlPlane.AuditQueue } else { $null }
        $pending = if ($null -eq $auditQueue) { -1 } else { [int]$auditQueue.Pending }
        $processed = if ($null -eq $auditQueue) { -1 } else { [int]$auditQueue.Processed }
        $failed = if ($null -eq $auditQueue) { -1 } else { [int]$auditQueue.Failed }
        $lastProcessedAt = if ($null -eq $auditQueue) { "" } else { [string]$auditQueue.LastProcessedAt }

        $signature = "$pending|$processed|$failed|$traceLineCount|$lastProcessedAt"
        if ($signature -eq $lastSnapshotSignature) {
            $stablePolls++
        }
        else {
            $stablePolls = 0
            $lastSnapshotSignature = $signature
        }

        $pendingDrained = ($pending -eq 0)
        $requiredTraceEntries = if ($ExpectedTraceEntries -gt 0) { $ExpectedTraceEntries } else { 1 }
        $traceAvailable = $traceExists -and ($traceLineCount -ge $requiredTraceEntries)
        $allowStableFallback = $ExpectedTraceEntries -le 0
        $stableEnough = $stablePolls -ge [math]::Max(1, $StablePollsRequired - 1)

        if ($pendingDrained -and ($traceAvailable -or ($allowStableFallback -and $stableEnough))) {
            return [PSCustomObject]@{
                status = if ($traceAvailable) { "PASS" } else { "WARN_RUNTIME_PARTIAL" }
                readStatus = $readStatus
                pending = $pending
                processed = $processed
                failed = $failed
                traceExists = $traceExists
                traceLineCount = $traceLineCount
                stablePolls = $stablePolls
                elapsedSec = [math]::Round(([datetimeoffset]::UtcNow - $startedAtUtc).TotalSeconds, 2)
                reason = if ($traceAvailable) { "queue_drained_and_trace_available" } else { "queue_drained_and_runtime_stable_without_trace_growth" }
                lastProcessedAtUtc = $lastProcessedAt
                polledAtUtc = $pollRecordedAtUtc.ToString("o")
            }
        }

        Start-Sleep -Milliseconds ([math]::Max(200, $PollIntervalMs))
    }

    return [PSCustomObject]@{
        status = "WARN_RUNTIME_PARTIAL"
        readStatus = "timeout"
        pending = $pending
        processed = $processed
        failed = $failed
        traceExists = $traceExists
        traceLineCount = $traceLineCount
        stablePolls = $stablePolls
        elapsedSec = [math]::Round(([datetimeoffset]::UtcNow - $startedAtUtc).TotalSeconds, 2)
        reason = "timed_out_waiting_for_audit_drain"
        lastProcessedAtUtc = $lastProcessedAt
        polledAtUtc = [datetimeoffset]::UtcNow.ToString("o")
    }
}
