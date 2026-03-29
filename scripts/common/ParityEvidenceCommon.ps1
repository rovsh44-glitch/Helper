function Read-ParityReportLineValue {
    param(
        [string[]]$Lines,
        [string]$Prefix
    )

    foreach ($line in $Lines) {
        if ($line.StartsWith($Prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $line.Substring($Prefix.Length).Trim()
        }
    }

    return ""
}

function Convert-ParityYesNoToBool {
    param([string]$Value)

    return (-not [string]::IsNullOrWhiteSpace($Value)) -and $Value.Equals("YES", [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-BlindEvalReportMetadata {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return [PSCustomObject]@{
            path = $Path
            exists = $false
            evidenceLevel = "missing"
            authoritative = $false
            formatStatus = "missing"
            provenanceStatus = "missing"
            blindCollectionStatus = "missing"
            reviewerDiversityStatus = "missing"
            integrityStatus = "missing"
            integritySufficiency = "missing"
        }
    }

    $lines = Get-Content $Path
    [PSCustomObject]@{
        path = $Path
        exists = $true
        evidenceLevel = Read-ParityReportLineValue -Lines $lines -Prefix "Evidence level:"
        authoritative = Convert-ParityYesNoToBool (Read-ParityReportLineValue -Lines $lines -Prefix "Authoritative evidence:")
        formatStatus = Read-ParityReportLineValue -Lines $lines -Prefix "Format status:"
        provenanceStatus = Read-ParityReportLineValue -Lines $lines -Prefix "Provenance status:"
        blindCollectionStatus = Read-ParityReportLineValue -Lines $lines -Prefix "Blind collection status:"
        reviewerDiversityStatus = Read-ParityReportLineValue -Lines $lines -Prefix "Reviewer diversity status:"
        integrityStatus = Read-ParityReportLineValue -Lines $lines -Prefix "Integrity status:"
        integritySufficiency = Read-ParityReportLineValue -Lines $lines -Prefix "Integrity sufficiency:"
    }
}

function Get-BlindHumanEvalReportMetadata {
    param([string]$Path)

    return Get-BlindEvalReportMetadata -Path $Path
}

function Get-RealModelEvalReportMetadata {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return [PSCustomObject]@{
            path = $Path
            exists = $false
            mode = "missing"
            requestedEvidenceLevel = "missing"
            evidenceLevel = "missing"
            authoritativeGateStatus = "missing"
            authoritative = $false
            status = "missing"
            nonAuthoritativeReasons = "missing"
            traceabilityStatus = "missing"
        }
    }

    $lines = Get-Content $Path
    [PSCustomObject]@{
        path = $Path
        exists = $true
        mode = Read-ParityReportLineValue -Lines $lines -Prefix "Mode:"
        requestedEvidenceLevel = Read-ParityReportLineValue -Lines $lines -Prefix "Requested evidence level:"
        evidenceLevel = Read-ParityReportLineValue -Lines $lines -Prefix "Evidence level:"
        authoritativeGateStatus = Read-ParityReportLineValue -Lines $lines -Prefix "Authoritative gate status:"
        authoritative = Convert-ParityYesNoToBool (Read-ParityReportLineValue -Lines $lines -Prefix "Authoritative evidence:")
        status = Read-ParityReportLineValue -Lines $lines -Prefix "Status:"
        nonAuthoritativeReasons = Read-ParityReportLineValue -Lines $lines -Prefix "Non-authoritative reasons:"
        traceabilityStatus = Read-ParityReportLineValue -Lines $lines -Prefix "Traceability status:"
    }
}

function Get-ParityCertificationReportMetadata {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return [PSCustomObject]@{
            path = $Path
            exists = $false
            evidenceLevel = "missing"
            authoritative = $false
            windowComplete = $false
            go = $false
            evidenceCompletenessStatus = "missing"
            authoritativeSourceStatus = "missing"
            noGoReasons = "missing"
            status = "missing"
        }
    }

    $lines = Get-Content $Path
    [PSCustomObject]@{
        path = $Path
        exists = $true
        evidenceLevel = Read-ParityReportLineValue -Lines $lines -Prefix "Evidence level:"
        authoritative = Convert-ParityYesNoToBool (Read-ParityReportLineValue -Lines $lines -Prefix "Authoritative evidence:")
        windowComplete = Convert-ParityYesNoToBool (Read-ParityReportLineValue -Lines $lines -Prefix "Certification window complete (14 days):")
        go = (Read-ParityReportLineValue -Lines $lines -Prefix "Go/No-Go:").Equals("GO", [System.StringComparison]::OrdinalIgnoreCase)
        evidenceCompletenessStatus = Read-ParityReportLineValue -Lines $lines -Prefix "Evidence completeness status:"
        authoritativeSourceStatus = Read-ParityReportLineValue -Lines $lines -Prefix "Authoritative source status:"
        noGoReasons = Read-ParityReportLineValue -Lines $lines -Prefix "NO-GO reasons:"
        status = Read-ParityReportLineValue -Lines $lines -Prefix "Status:"
    }
}

function Get-ParityEvidenceBundleMetadata {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return [PSCustomObject]@{
            path = $Path
            exists = $false
            status = "missing"
            claimEligible = $false
        }
    }

    try {
        $bundle = Get-Content -Raw $Path | ConvertFrom-Json
        return [PSCustomObject]@{
            path = $Path
            exists = $true
            status = if ([string]::IsNullOrWhiteSpace([string]$bundle.status)) { "unknown" } else { [string]$bundle.status }
            claimEligible = [bool]$bundle.claimEligible
        }
    }
    catch {
        return [PSCustomObject]@{
            path = $Path
            exists = $true
            status = "invalid"
            claimEligible = $false
        }
    }
}

function Test-ParityDailySnapshot {
    param(
        [Parameter(Mandatory = $true)]$Snapshot,
        [string]$Path = ""
    )

    $requiredFields = @(
        "schemaVersion",
        "generatedAt",
        "date",
        "evidenceLevel",
        "ttft_local_ms",
        "ttft_network_ms",
        "conversation_success_rate",
        "helpfulness",
        "citation_precision",
        "citation_coverage",
        "tool_correctness",
        "security_incidents",
        "open_p0_p1",
        "blind_human_eval_status",
        "real_model_eval_status",
        "release_baseline_status",
        "sourceLinks"
    )

    $missing = @()
    foreach ($field in $requiredFields) {
        if ($null -eq $Snapshot.PSObject.Properties[$field] -or $null -eq $Snapshot.$field) {
            $missing += $field
        }
    }

    $allowedEvidenceLevels = @("sample", "synthetic", "dry_run", "live_non_authoritative", "authoritative")
    $evidenceLevel = [string]$Snapshot.evidenceLevel
    $validEvidenceLevel = $allowedEvidenceLevels -contains $evidenceLevel
    $countsTowardWindow = $evidenceLevel -in @("live_non_authoritative", "authoritative")

    [PSCustomObject]@{
        path = $Path
        valid = ($missing.Count -eq 0) -and $validEvidenceLevel
        missingFields = $missing
        validEvidenceLevel = $validEvidenceLevel
        countsTowardWindow = $countsTowardWindow
    }
}

function Import-ParityDailySnapshots {
    param([string]$DailyDir)

    if (-not (Test-Path $DailyDir)) {
        return [PSCustomObject]@{
            validSnapshots = @()
            countedSnapshots = @()
            invalidSnapshots = @()
        }
    }

    $validSnapshots = New-Object System.Collections.Generic.List[object]
    $countedSnapshots = New-Object System.Collections.Generic.List[object]
    $invalidSnapshots = New-Object System.Collections.Generic.List[object]

    $files = Get-ChildItem -Path $DailyDir -Filter "*.json" -File | Sort-Object Name
    foreach ($file in $files) {
        try {
            $snapshot = Get-Content $file.FullName -Raw | ConvertFrom-Json
            $validation = Test-ParityDailySnapshot -Snapshot $snapshot -Path $file.FullName
            if (-not $validation.valid) {
                $invalidSnapshots.Add([PSCustomObject]@{
                    path = $file.FullName
                    reason = if (-not $validation.validEvidenceLevel) { "invalid_evidence_level" } else { "missing_fields" }
                    details = if ($validation.missingFields.Count -gt 0) { $validation.missingFields -join ", " } else { "" }
                })
                continue
            }

            $snapshotWithPath = [PSCustomObject]@{
                path = $file.FullName
                snapshot = $snapshot
                countsTowardWindow = $validation.countsTowardWindow
            }
            $validSnapshots.Add($snapshotWithPath)
            if ($validation.countsTowardWindow) {
                $countedSnapshots.Add($snapshotWithPath)
            }
        }
        catch {
            $invalidSnapshots.Add([PSCustomObject]@{
                path = $file.FullName
                reason = "invalid_json"
                details = $_.Exception.Message
            })
        }
    }

    $validSnapshotArray = [object[]]$validSnapshots.ToArray()
    $countedSnapshotArray = [object[]]$countedSnapshots.ToArray()
    $invalidSnapshotArray = [object[]]$invalidSnapshots.ToArray()

    [PSCustomObject]@{
        validSnapshots = $validSnapshotArray
        countedSnapshots = $countedSnapshotArray
        invalidSnapshots = $invalidSnapshotArray
    }
}
