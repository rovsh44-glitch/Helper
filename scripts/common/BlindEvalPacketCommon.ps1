. (Join-Path $PSScriptRoot "HumanEvalCommon.ps1")

function Get-BlindEvalOptionalRowValue {
    param(
        [Parameter(Mandatory = $true)]$Row,
        [Parameter(Mandatory = $true)][string]$PropertyName,
        $DefaultValue = $null
    )

    if ($null -eq $Row) {
        return $DefaultValue
    }

    $property = $Row.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return $DefaultValue
    }

    return $property.Value
}

function Get-BlindEvalRevealLabels {
    param(
        [Parameter(Mandatory = $true)][string]$ConversationId
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($ConversationId)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
    }
    finally {
        $sha.Dispose()
    }

    $helperFirst = ($hash[0] % 2) -eq 0
    return [PSCustomObject]@{
        helper = if ($helperFirst) { "A" } else { "B" }
        baseline = if ($helperFirst) { "B" } else { "A" }
    }
}

function Import-BlindEvalResponsePairs {
    param(
        [Parameter(Mandatory = $true)][string]$InputPath
    )

    if (-not (Test-Path $InputPath)) {
        throw "[BlindEval] Response-pairs input not found: $InputPath"
    }

    $extension = [System.IO.Path]::GetExtension($InputPath)
    $rows = @()
    if ($extension -ieq ".jsonl") {
        $lines = Get-Content -Path $InputPath -Encoding UTF8 | Where-Object { $_ -and -not $_.TrimStart().StartsWith("#") }
        foreach ($line in $lines) {
            $rows += ($line | ConvertFrom-Json)
        }
    }
    else {
        $rows = Import-Csv -Path $InputPath
    }

    if (-not $rows -or $rows.Count -eq 0) {
        throw "[BlindEval] Response-pairs input is empty."
    }

    return @(
        foreach ($row in $rows) {
            $conversationId = [string]$(if ($null -ne (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "conversation_id")) { (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "conversation_id") } else { (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "id") })
            $taskFamily = [string]$(if ($null -ne (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "task_family")) { (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "task_family") } else { (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "kind") })
            $sourceScenarioId = [string]$(if ($null -ne (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "source_scenario_id")) { (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "source_scenario_id") } elseif ($null -ne (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "id")) { (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "id") } else { $conversationId })
            $prompt = [string](Get-BlindEvalOptionalRowValue -Row $row -PropertyName "prompt" -DefaultValue "")
            $helperResponse = [string]$(if ($null -ne (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "helper_response")) { (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "helper_response") } else { (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "HelperResponse") })
            $baselineResponse = [string]$(if ($null -ne (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "baseline_response")) { (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "baseline_response") } else { (Get-BlindEvalOptionalRowValue -Row $row -PropertyName "BaselineResponse") })
            $language = [string](Get-BlindEvalOptionalRowValue -Row $row -PropertyName "language" -DefaultValue "")

            if ([string]::IsNullOrWhiteSpace($conversationId) -or
                [string]::IsNullOrWhiteSpace($language) -or
                [string]::IsNullOrWhiteSpace($taskFamily) -or
                [string]::IsNullOrWhiteSpace($prompt) -or
                [string]::IsNullOrWhiteSpace($helperResponse) -or
                [string]::IsNullOrWhiteSpace($baselineResponse)) {
                throw "[BlindEval] Response-pairs row is missing required fields."
            }

            [PSCustomObject]@{
                conversation_id = $conversationId
                source_scenario_id = $sourceScenarioId
                language = $language
                task_family = $taskFamily
                prompt = $prompt
                helper_response = $helperResponse
                baseline_response = $baselineResponse
            }
        }
    )
}

function Import-BlindEvalReviewerPool {
    param(
        [string]$ReviewerPoolCsv = "",
        [string[]]$ReviewerIds = @()
    )

    if (-not [string]::IsNullOrWhiteSpace($ReviewerPoolCsv)) {
        if (-not (Test-Path $ReviewerPoolCsv)) {
            throw "[BlindEval] Reviewer pool CSV not found: $ReviewerPoolCsv"
        }

        $rows = Import-Csv -Path $ReviewerPoolCsv
        if (-not $rows -or $rows.Count -eq 0) {
            throw "[BlindEval] Reviewer pool CSV is empty."
        }

        return @(
            foreach ($row in $rows) {
                $reviewerId = [string]$row.reviewer_id
                if ([string]::IsNullOrWhiteSpace($reviewerId)) {
                    throw "[BlindEval] Reviewer pool row is missing reviewer_id."
                }

                [PSCustomObject]@{
                    reviewer_id = $reviewerId
                    languages = @(([string]$row.languages -split '[,; ]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim().ToLowerInvariant() }))
                    task_families = @(([string]$row.task_families -split '[,; ]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim().ToLowerInvariant() }))
                }
            }
        )
    }

    if ($ReviewerIds.Count -eq 0) {
        throw "[BlindEval] No reviewer ids provided."
    }

    return @(
        $ReviewerIds |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique |
            ForEach-Object {
                [PSCustomObject]@{
                    reviewer_id = [string]$_
                    languages = @()
                    task_families = @()
                }
            }
    )
}
