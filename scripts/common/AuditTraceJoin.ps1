function ConvertTo-TraceJoinLookup {
    param([object[]]$Results)

    $lookup = @{}
    foreach ($result in @($Results)) {
        $conversationId = [string]$result.conversationId
        $turnId = [string]$result.turnId
        if ([string]::IsNullOrWhiteSpace($conversationId) -or [string]::IsNullOrWhiteSpace($turnId)) {
            continue
        }

        $lookup["$conversationId|$turnId"] = $true
    }

    return $lookup
}

function Invoke-AuditTraceJoin {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][object[]]$Results,
        [datetimeoffset]$RunStartedAtUtc = [datetimeoffset]::MinValue,
        [datetimeoffset]$RunCompletedAtUtc = [datetimeoffset]::MaxValue
    )

    $payload = [ordered]@{
        sourcePath = $SourcePath
        readStatus = "not_available"
        readError = ""
        candidateEntries = 0
        matched = 0
        matchStrategy = "conversation_turn + correlation + run_window"
        entries = @()
        breakdown = [ordered]@{
            totalLines = 0
            parsedEntries = 0
            inRunWindow = 0
            candidateKeys = 0
            conversationTurnCandidates = 0
            correlationCandidates = 0
            strictMatches = 0
        }
    }

    if (-not (Test-Path $SourcePath)) {
        $payload.readStatus = "missing_file"
        return [PSCustomObject]$payload
    }

    $joinLookup = ConvertTo-TraceJoinLookup -Results $Results
    $correlationLookup = @{}
    foreach ($result in @($Results)) {
        $correlationId = [string]$result.correlationId
        if (-not [string]::IsNullOrWhiteSpace($correlationId)) {
            $correlationLookup[$correlationId] = $true
        }
    }

    $payload.breakdown.candidateKeys = $joinLookup.Count

    try {
        $traceLines = [System.IO.File]::ReadAllLines($SourcePath)
        $payload.readStatus = "ok"
        $payload.breakdown.totalLines = $traceLines.Length
        $matchedEntries = New-Object System.Collections.Generic.List[object]

        foreach ($line in $traceLines) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            $entry = $null
            try {
                $entry = $line | ConvertFrom-Json
            }
            catch {
                continue
            }

            $payload.breakdown.parsedEntries++

            $recordedAtRaw = [string]$entry.RecordedAtUtc
            $recordedAtUtc = [datetimeoffset]::MinValue
            $hasRecordedAt = [datetimeoffset]::TryParse($recordedAtRaw, [ref]$recordedAtUtc)
            $inRunWindow = $true
            if ($hasRecordedAt) {
                $inRunWindow = ($recordedAtUtc -ge $RunStartedAtUtc) -and ($recordedAtUtc -le $RunCompletedAtUtc.AddMinutes(2))
            }

            if (-not $inRunWindow) {
                continue
            }

            $payload.breakdown.inRunWindow++
            $conversationId = [string]$entry.ConversationId
            $turnId = [string]$entry.TurnId
            $correlationId = [string]$entry.CorrelationId
            $joinKey = if ([string]::IsNullOrWhiteSpace($conversationId) -or [string]::IsNullOrWhiteSpace($turnId)) { "" } else { "$conversationId|$turnId" }
            $conversationTurnMatch = (-not [string]::IsNullOrWhiteSpace($joinKey)) -and $joinLookup.ContainsKey($joinKey)
            $correlationMatch = (-not [string]::IsNullOrWhiteSpace($correlationId)) -and $correlationLookup.ContainsKey($correlationId)

            if ($conversationTurnMatch) {
                $payload.breakdown.conversationTurnCandidates++
            }
            if ($correlationMatch) {
                $payload.breakdown.correlationCandidates++
            }

            if (-not ($conversationTurnMatch -or $correlationMatch)) {
                continue
            }

            $payload.candidateEntries++
            if (-not $conversationTurnMatch) {
                continue
            }

            $payload.breakdown.strictMatches++
            $matchedEntries.Add([PSCustomObject]@{
                conversationId = $conversationId
                turnId = $turnId
                correlationId = $correlationId
                outcome = [string]$entry.Outcome
                feedback = [string]$entry.Feedback
                recordedAtUtc = $recordedAtRaw
                sourceCount = [int]$entry.SourceCount
            })
        }

        $payload.matched = $matchedEntries.Count
        $payload.entries = [object[]]$matchedEntries.ToArray()
    }
    catch {
        $payload.readStatus = "failed"
        $payload.readError = $_.Exception.Message
    }

    return [PSCustomObject]$payload
}
