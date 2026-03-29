function Get-HumanEvalCriteria {
    return @("clarity", "empathy_appropriateness", "usefulness", "factuality")
}

function Get-HumanEvalStructuredNoteDefinitions {
    return @(
        [PSCustomObject]@{
            Name = "robotic_repetition"
            Allowed = @("none", "minor", "clear")
            Description = "Repeated openings, mirrored phrasing, or visible canned repetition."
        },
        [PSCustomObject]@{
            Name = "unnatural_templating"
            Allowed = @("none", "minor", "clear")
            Description = "Rigid protocol-like framing or obvious template scaffolding."
        },
        [PSCustomObject]@{
            Name = "language_mismatch"
            Allowed = @("none", "minor", "clear")
            Description = "Unexpected language drift, mixed-language response, or wrong-language answer."
        },
        [PSCustomObject]@{
            Name = "clarification_helpfulness"
            Allowed = @("n_a", "weak", "mixed", "strong")
            Description = "For clarification turns only: whether the clarification feels useful and progress-making."
        },
        [PSCustomObject]@{
            Name = "naturalness_feel"
            Allowed = @("low", "mixed", "high")
            Description = "Overall felt naturalness of the response surface."
        }
    )
}

function Get-HumanEvalStructuredNoteColumns {
    return @(Get-HumanEvalStructuredNoteDefinitions | ForEach-Object { $_.Name })
}

function Get-HumanEvalRequiredColumns {
    return @("conversation_id", "variant", "language", "reviewer_id") + (Get-HumanEvalCriteria)
}

function New-HumanEvalReviewerSubmissionRow {
    param(
        [Parameter(Mandatory = $true)][string]$PacketId,
        [Parameter(Mandatory = $true)][string]$ConversationId,
        [Parameter(Mandatory = $true)][string]$BlindLabel,
        [Parameter(Mandatory = $true)][string]$ReviewerId,
        [Parameter(Mandatory = $true)][string]$Language,
        [Parameter(Mandatory = $true)][string]$TaskFamily,
        [Parameter(Mandatory = $true)][string]$SourceScenarioId,
        [Parameter(Mandatory = $true)][string]$CollectionDate,
        [Parameter(Mandatory = $true)][string]$CollectionMode,
        [string]$Prompt = "",
        [string]$CandidateResponse = ""
    )

    $row = [ordered]@{
        packet_id = $PacketId
        conversation_id = $ConversationId
        blind_label = $BlindLabel
        reviewer_id = $ReviewerId
        language = $Language
        task_family = $TaskFamily
        source_scenario_id = $SourceScenarioId
        collection_date = $CollectionDate
        collection_mode = $CollectionMode
        prompt = $Prompt
        candidate_response = $CandidateResponse
    }

    foreach ($criterion in (Get-HumanEvalCriteria)) {
        $row[$criterion] = ""
    }

    foreach ($column in (Get-HumanEvalStructuredNoteColumns)) {
        $row[$column] = ""
    }

    return [PSCustomObject]$row
}

function Get-HumanEvalStructuredNoteCoverageSummary {
    param(
        [Parameter(Mandatory = $true)]$Rows
    )

    $rowArray = @($Rows)
    $totalRows = $rowArray.Count
    $structuredNoteDefinitions = Get-HumanEvalStructuredNoteDefinitions
    $columns = New-Object System.Collections.Generic.List[object]

    foreach ($definition in $structuredNoteDefinitions) {
        $columnName = [string]$definition.Name
        $present = ($totalRows -gt 0) -and ($rowArray[0].PSObject.Properties.Name -contains $columnName)
        $filledCount = 0
        $valueCounts = @{}

        if ($present) {
            foreach ($row in $rowArray) {
                $rawValue = [string]$row.$columnName
                if ([string]::IsNullOrWhiteSpace($rawValue)) {
                    continue
                }

                $normalizedValue = $rawValue.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_")
                $filledCount++
                if ($valueCounts.ContainsKey($normalizedValue)) {
                    $valueCounts[$normalizedValue] = [int]$valueCounts[$normalizedValue] + 1
                }
                else {
                    $valueCounts[$normalizedValue] = 1
                }
            }
        }

        $valueSummary = @(
            $valueCounts.GetEnumerator() |
                Sort-Object Name |
                ForEach-Object {
                    [PSCustomObject]@{
                        value = [string]$_.Key
                        count = [int]$_.Value
                        share = if ($filledCount -eq 0) { 0.0 } else { [math]::Round([int]$_.Value / [double]$filledCount, 4) }
                    }
                }
        )

        $columns.Add([PSCustomObject]@{
            name = $columnName
            present = $present
            filledRows = $filledCount
            missingRows = [math]::Max(0, $totalRows - $filledCount)
            completionRate = if ($totalRows -eq 0) { 0.0 } else { [math]::Round($filledCount / [double]$totalRows, 4) }
            values = $valueSummary
        })
    }

    $columnArray = @($columns.ToArray())
    return [PSCustomObject]@{
        totalRows = $totalRows
        allColumnsPresent = ($totalRows -gt 0) -and (@($columnArray | Where-Object { -not $_.present }).Count -eq 0)
        allRowsComplete = ($totalRows -gt 0) -and (@($columnArray | Where-Object { $_.filledRows -lt $totalRows }).Count -eq 0)
        columns = $columnArray
    }
}

function Convert-ToHumanEvalScore {
    param(
        [Parameter(Mandatory = $true)]$RawValue,
        [Parameter(Mandatory = $true)][string]$Criterion,
        [Parameter(Mandatory = $true)][string]$ConversationId,
        [Parameter(Mandatory = $true)][string]$Variant
    )

    $rawText = [string]$RawValue
    if ([string]::IsNullOrWhiteSpace($rawText)) {
        throw "[HumanEval] Empty score for '$Criterion' in conversation '$ConversationId' variant '$Variant'."
    }

    $normalized = $rawText.Trim().Replace(",", ".")
    $parsed = 0.0
    if (-not [double]::TryParse($normalized, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        throw "[HumanEval] Invalid numeric score '$rawText' for '$Criterion' in conversation '$ConversationId' variant '$Variant'."
    }

    return $parsed
}

function Convert-ToHumanEvalStructuredNote {
    param(
        [Parameter(Mandatory = $true)]$RawValue,
        [Parameter(Mandatory = $true)][string]$Column,
        [switch]$AllowBlank
    )

    $definition = @(Get-HumanEvalStructuredNoteDefinitions | Where-Object { $_.Name -eq $Column } | Select-Object -First 1)[0]
    if ($null -eq $definition) {
        throw "[HumanEval] Unknown structured note column '$Column'."
    }

    $rawText = [string]$RawValue
    if ([string]::IsNullOrWhiteSpace($rawText)) {
        if ($AllowBlank.IsPresent) {
            return ""
        }

        throw "[HumanEval] Empty structured note for '$Column'."
    }

    $normalized = $rawText.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_")
    if ($definition.Allowed -notcontains $normalized) {
        $allowed = @($definition.Allowed) -join ", "
        throw "[HumanEval] Invalid structured note '$rawText' for '$Column'. Allowed: $allowed."
    }

    return $normalized
}

function Get-HumanEvalTaskFamily {
    param(
        [string]$ConversationId,
        [string]$ExplicitTaskFamily = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitTaskFamily)) {
        return $ExplicitTaskFamily.Trim().ToLowerInvariant()
    }

    if ([string]::IsNullOrWhiteSpace($ConversationId)) {
        return "unknown"
    }

    $tokens = $ConversationId.Split("-", [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($tokens.Length -ge 3) {
        return $tokens[1].Trim().ToLowerInvariant()
    }

    if ($tokens.Length -ge 2) {
        return $tokens[0].Trim().ToLowerInvariant()
    }

    return "unknown"
}

function Assert-HumanEvalRequiredColumns {
    param(
        [Parameter(Mandatory = $true)]$SampleRow,
        [string[]]$AdditionalColumns = @(),
        [string[]]$OptionalColumns = @()
    )

    $requiredColumns = (Get-HumanEvalRequiredColumns) + @($AdditionalColumns)
    foreach ($column in $requiredColumns) {
        if (-not ($SampleRow.PSObject.Properties.Name -contains $column)) {
            throw "[HumanEval] Missing required column: $column"
        }
    }
}

function Import-HumanEvalNormalizedRows {
    param(
        [Parameter(Mandatory = $true)][string]$InputCsv,
        [string[]]$AdditionalColumns = @(),
        [string[]]$OptionalColumns = @()
    )

    if (-not (Test-Path $InputCsv)) {
        throw "[HumanEval] Input CSV not found: $InputCsv"
    }

    $rows = Import-Csv -Path $InputCsv
    if (-not $rows -or $rows.Count -eq 0) {
        throw "[HumanEval] Input CSV is empty."
    }

    Assert-HumanEvalRequiredColumns -SampleRow $rows[0] -AdditionalColumns $AdditionalColumns -OptionalColumns $OptionalColumns
    $criteria = Get-HumanEvalCriteria
    $structuredNoteColumns = Get-HumanEvalStructuredNoteColumns
    $presentStructuredNoteColumns = @($structuredNoteColumns | Where-Object { $rows[0].PSObject.Properties.Name -contains $_ })

    return @(
        foreach ($row in $rows) {
            $conversationId = [string]$row.conversation_id
            $variant = [string]$row.variant

            $explicitTaskFamily = if ($row.PSObject.Properties.Name -contains "task_family") {
                [string]$row.task_family
            }
            else {
                ""
            }

            $normalized = [ordered]@{
                conversation_id = $conversationId
                variant = $variant
                language = [string]$row.language
                reviewer_id = [string]$row.reviewer_id
                task_family = Get-HumanEvalTaskFamily -ConversationId $conversationId -ExplicitTaskFamily $explicitTaskFamily
            }

            foreach ($column in $AdditionalColumns) {
                $normalized[$column] = [string]$row.$column
            }

            foreach ($column in $structuredNoteColumns) {
                if ($presentStructuredNoteColumns -contains $column) {
                    $normalized[$column] = Convert-ToHumanEvalStructuredNote -RawValue $row.$column -Column $column -AllowBlank
                }
                else {
                    $normalized[$column] = ""
                }
            }

            foreach ($criterion in $criteria) {
                $value = Convert-ToHumanEvalScore -RawValue $row.$criterion -Criterion $criterion -ConversationId $conversationId -Variant $variant
                if ($value -lt 1 -or $value -gt 5) {
                    throw "[HumanEval] Score out of range 1..5 for '$criterion' in conversation '$conversationId' variant '$variant'."
                }

                $normalized[$criterion] = [double]$value
            }

            [PSCustomObject]$normalized
        }
    )
}
