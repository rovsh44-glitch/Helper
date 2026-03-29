[CmdletBinding()]
param(
    [string]$WorkspaceRoot = ".",
    [string]$OutputDir = "",
    [string]$ReportPath = "",
    [int]$PdfProbeTimeoutSec = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

function Read-QueueMap {
    param(
        [string]$Path
    )

    $map = @{}
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $map
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $map
    }

    $json = $raw | ConvertFrom-Json
    foreach ($property in @($json.PSObject.Properties)) {
        $map[$property.Name] = [string]$property.Value
    }

    return $map
}

function Resolve-CurrentQueuePath {
    param(
        [Parameter(Mandatory = $true)][object]$PathConfig
    )

    return Join-Path $PathConfig.DataRoot "indexing_queue.json"
}

function Resolve-LatestBackupQueuePath {
    param(
        [Parameter(Mandatory = $true)][object]$PathConfig
    )

    $runtimeRoot = $PathConfig.OperatorRuntimeRoot
    if (-not (Test-Path -LiteralPath $runtimeRoot)) {
        return $null
    }

    $candidate = Get-ChildItem -LiteralPath $runtimeRoot -File -Filter "queue_backup*.json" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $candidate) {
        return $null
    }

    return $candidate.FullName
}

function Resolve-PdfQuickProbePath {
    param(
        [Parameter(Mandatory = $true)][string]$HelperRoot
    )

    $candidates = @(
        (Join-Path $HelperRoot "sandbox\diagnostics\PdfQuickProbe\bin\Debug\net8.0\PdfQuickProbe.exe"),
        (Join-Path $HelperRoot "sandbox\buildcheck\PdfQuickProbe\PdfQuickProbe.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
}

function Get-IndexableFiles {
    param(
        [Parameter(Mandatory = $true)][string]$LibraryDocsRoot
    )

    $allowed = @(
        ".pdf",
        ".epub",
        ".html",
        ".htm",
        ".docx",
        ".fb2",
        ".md",
        ".markdown",
        ".djvu"
    )

    return Get-ChildItem -LiteralPath $LibraryDocsRoot -Recurse -File |
        Where-Object { $allowed -contains $_.Extension.ToLowerInvariant() } |
        Sort-Object FullName
}

function Get-DomainFromPath {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$LibraryDocsRoot
    )

    $relative = [System.IO.Path]::GetRelativePath($LibraryDocsRoot, $FilePath)
    $parts = $relative -split "[\\/]"
    if ($parts.Count -ge 2) {
        return $parts[0]
    }

    return "unknown"
}

function Invoke-PdfQuickProbe {
    param(
        [Parameter(Mandatory = $true)][string]$ProbePath,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][int]$TimeoutSec
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ProbePath
    $null = $startInfo.ArgumentList.Add($FilePath)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $null = $process.Start()

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    if (-not $process.WaitForExit($TimeoutSec * 1000)) {
        try {
            $process.Kill($true)
        } catch {
        }

        return [PSCustomObject]@{
            ProbeStatus = "timeout"
            ExitCode = $null
            PageCount = $null
            SamplePagesTotal = 0
            SamplePagesWithText = 0
            SampleZeroTextPages = 0
            SampleTextPageRatio = 0.0
            SampleAvgTextLen = 0.0
            ProbeError = "probe_timeout"
            StdOut = ""
            StdErr = ""
        }
    }

    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $process.WaitForExit()

    $pageCount = $null
    if ($stdout -match "PAGE_COUNT=(\d+)") {
        $pageCount = [int]$matches[1]
    }

    $sampleRecords = New-Object System.Collections.Generic.List[object]
    foreach ($line in ($stdout -split "`r?`n")) {
        if ($line -match "^PAGE=(\d+)\s+LETTERS=(\d+)\s+WORDS=(\d+)\s+TEXT_LEN=(\d+)\s+SAMPLE=(.*)$") {
            $sampleRecords.Add([PSCustomObject]@{
                    Page = [int]$matches[1]
                    Letters = [int]$matches[2]
                    Words = [int]$matches[3]
                    TextLen = [int]$matches[4]
                    Sample = [string]$matches[5]
                })
        }
    }

    $samplePagesTotal = $sampleRecords.Count
    $samplePagesWithText = @($sampleRecords | Where-Object { $_.TextLen -gt 0 -or $_.Words -gt 0 -or $_.Letters -gt 0 }).Count
    $sampleZeroTextPages = [Math]::Max(0, $samplePagesTotal - $samplePagesWithText)
    $sampleTextPageRatio = if ($samplePagesTotal -gt 0) { [Math]::Round($samplePagesWithText / [double]$samplePagesTotal, 4) } else { 0.0 }
    $sampleAvgTextLen = if ($samplePagesTotal -gt 0) { [Math]::Round((($sampleRecords | Measure-Object -Property TextLen -Average).Average), 2) } else { 0.0 }

    $probeError = $null
    if ($process.ExitCode -ne 0) {
        $probeError = @(
            ($stderr -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }),
            ($stdout -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        ) | Select-Object -Last 1
    }

    return [PSCustomObject]@{
        ProbeStatus = "ok"
        ExitCode = $process.ExitCode
        PageCount = $pageCount
        SamplePagesTotal = $samplePagesTotal
        SamplePagesWithText = $samplePagesWithText
        SampleZeroTextPages = $sampleZeroTextPages
        SampleTextPageRatio = $sampleTextPageRatio
        SampleAvgTextLen = $sampleAvgTextLen
        ProbeError = $probeError
        StdOut = $stdout
        StdErr = $stderr
    }
}

function Get-Fb2Flags {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath
    )

    $bytes = [System.IO.File]::ReadAllBytes($FilePath)
    $length = [Math]::Min($bytes.Length, 1024)
    $header = [System.Text.Encoding]::ASCII.GetString($bytes, 0, $length)
    $headerLower = $header.ToLowerInvariant()

    return [PSCustomObject]@{
        HasWindows1251 = $headerLower.Contains("windows-1251")
        HeaderSnippet = $header.Trim()
    }
}

function Get-FormatPriority {
    param(
        [Parameter(Mandatory = $true)][string]$Extension
    )

    switch ($Extension.ToLowerInvariant()) {
        ".epub" { return 0 }
        ".html" { return 0 }
        ".htm" { return 0 }
        ".md" { return 0 }
        ".markdown" { return 0 }
        ".docx" { return 0 }
        ".fb2" { return 1 }
        ".pdf" { return 2 }
        ".djvu" { return 2 }
        default { return 9 }
    }
}

function Get-PageCountSortValue {
    param(
        $PageCount
    )

    if ($null -eq $PageCount) {
        return 2147483647
    }

    return [int]$PageCount
}

function Get-WarningPenalty {
    param(
        [Parameter(Mandatory = $true)][System.Collections.Generic.List[string]]$Flags
    )

    return $Flags.Count
}

function Classify-FileRecord {
    param(
        [Parameter(Mandatory = $true)][System.IO.FileInfo]$File,
        [Parameter(Mandatory = $true)][string]$LibraryDocsRoot,
        [Parameter(Mandatory = $true)][hashtable]$CurrentQueueMap,
        [Parameter(Mandatory = $true)][hashtable]$BackupQueueMap,
        [string]$PdfProbePath,
        [Parameter(Mandatory = $true)][int]$PdfProbeTimeoutSec
    )

    $extension = $File.Extension.ToLowerInvariant()
    $domain = Get-DomainFromPath -FilePath $File.FullName -LibraryDocsRoot $LibraryDocsRoot
    $fileSizeMb = [Math]::Round($File.Length / 1MB, 2)
    $flags = New-Object System.Collections.Generic.List[string]
    $classification = "QUARANTINE"
    $reason = ""
    $pageCount = $null
    $samplePagesTotal = 0
    $samplePagesWithText = 0
    $sampleZeroTextPages = 0
    $sampleTextPageRatio = 0.0
    $sampleAvgTextLen = 0.0
    $probeStatus = "not_required"
    $probeError = $null

    if ($CurrentQueueMap.ContainsKey($File.FullName)) {
        $flags.Add("current_queue:" + $CurrentQueueMap[$File.FullName])
    }

    if ($BackupQueueMap.ContainsKey($File.FullName) -and -not $CurrentQueueMap.ContainsKey($File.FullName)) {
        $flags.Add("missing_from_current_queue_after_backup")
    }

    switch ($extension) {
        ".epub" {
            $classification = "TEXT_FAST"
            $reason = "text_native_format=epub"
            break
        }
        ".html" {
            $classification = "TEXT_FAST"
            $reason = "text_native_format=html"
            break
        }
        ".htm" {
            $classification = "TEXT_FAST"
            $reason = "text_native_format=html"
            break
        }
        ".md" {
            $classification = "TEXT_FAST"
            $reason = "text_native_format=markdown"
            break
        }
        ".markdown" {
            $classification = "TEXT_FAST"
            $reason = "text_native_format=markdown"
            break
        }
        ".docx" {
            $classification = "TEXT_FAST"
            $reason = "text_native_format=docx"
            break
        }
        ".fb2" {
            $fb2Flags = Get-Fb2Flags -FilePath $File.FullName
            if ($fb2Flags.HasWindows1251) {
                $classification = "QUARANTINE"
                $reason = "fb2_encoding=windows-1251"
                $flags.Add("fb2_windows_1251")
            } else {
                $classification = "TEXT_FAST"
                $reason = "text_native_format=fb2"
            }
            break
        }
        ".djvu" {
            $classification = "QUARANTINE"
            $reason = "djvu_preflight_probe_not_implemented"
            $flags.Add("djvu_probe_unavailable")
            break
        }
        ".pdf" {
            if ([string]::IsNullOrWhiteSpace($PdfProbePath)) {
                $classification = "QUARANTINE"
                $reason = "pdf_probe_unavailable"
                $flags.Add("pdf_probe_unavailable")
                break
            }

            $probe = Invoke-PdfQuickProbe -ProbePath $PdfProbePath -FilePath $File.FullName -TimeoutSec $PdfProbeTimeoutSec
            $probeStatus = $probe.ProbeStatus
            $pageCount = $probe.PageCount
            $samplePagesTotal = $probe.SamplePagesTotal
            $samplePagesWithText = $probe.SamplePagesWithText
            $sampleZeroTextPages = $probe.SampleZeroTextPages
            $sampleTextPageRatio = $probe.SampleTextPageRatio
            $sampleAvgTextLen = $probe.SampleAvgTextLen
            $probeError = $probe.ProbeError

            $zeroTextRatio = if ($samplePagesTotal -gt 0) {
                $sampleZeroTextPages / [double]$samplePagesTotal
            } else {
                1.0
            }

            if ($probe.ProbeStatus -eq "timeout") {
                $classification = "QUARANTINE"
                $reason = "pdf_probe_timeout"
                $flags.Add("probe_timeout")
                break
            }

            if ($probe.ExitCode -ne 0) {
                $classification = "QUARANTINE"
                $reason = "pdf_probe_exception"
                $flags.Add("pdf_probe_exception")
                break
            }

            if ($samplePagesTotal -eq 0) {
                $classification = "QUARANTINE"
                $reason = "pdf_probe_no_sample_pages"
                $flags.Add("pdf_probe_no_samples")
                break
            }

            if ($sampleTextPageRatio -lt 0.20 -or $sampleAvgTextLen -lt 64 -or $zeroTextRatio -ge 0.80) {
                $classification = "VISION_ONLY"
                $reason = "pdf_scan_heavy_or_no_text_layer"
                break
            }

            if ($sampleTextPageRatio -lt 0.70 -or $sampleAvgTextLen -lt 300 -or $fileSizeMb -ge 16 -or (($null -ne $pageCount) -and $pageCount -ge 400)) {
                $classification = "TEXT_COMPLEX"
                $reason = "pdf_text_present_but_complex"
                break
            }

            $classification = "TEXT_FAST"
            $reason = "pdf_text_rich_stable_sample"
            break
        }
        default {
            $classification = "QUARANTINE"
            $reason = "unsupported_extension"
            $flags.Add("unsupported_extension")
            break
        }
    }

    if ($classification -eq "VISION_ONLY" -and [string]::IsNullOrWhiteSpace($PdfProbePath)) {
        $flags.Add("vision_fallback_dependency_unknown")
    }

    return [PSCustomObject]@{
        FilePath = $File.FullName
        FileName = $File.Name
        Domain = $domain
        Extension = $extension
        FileSizeBytes = $File.Length
        FileSizeMb = $fileSizeMb
        LastWriteTime = $File.LastWriteTime.ToString("s")
        Classification = $classification
        ClassificationReason = $reason
        InCurrentQueue = $CurrentQueueMap.ContainsKey($File.FullName)
        CurrentQueueStatus = if ($CurrentQueueMap.ContainsKey($File.FullName)) { $CurrentQueueMap[$File.FullName] } else { $null }
        InLatestQueueBackup = $BackupQueueMap.ContainsKey($File.FullName)
        PageCount = $pageCount
        SamplePagesTotal = $samplePagesTotal
        SamplePagesWithText = $samplePagesWithText
        SampleZeroTextPages = $sampleZeroTextPages
        SampleTextPageRatio = $sampleTextPageRatio
        SampleAvgTextLen = $sampleAvgTextLen
        ProbeStatus = $probeStatus
        ProbeError = $probeError
        WarningPenalty = Get-WarningPenalty -Flags $flags
        Flags = @($flags)
        FormatPriority = Get-FormatPriority -Extension $extension
        PageCountSortValue = Get-PageCountSortValue -PageCount $pageCount
    }
}

function Sort-ClassifiedItems {
    param(
        [Parameter(Mandatory = $true)][object[]]$Items
    )

    return $Items | Sort-Object `
        @{ Expression = { $_.FormatPriority } ; Ascending = $true }, `
        @{ Expression = { $_.PageCountSortValue } ; Ascending = $true }, `
        @{ Expression = { $_.FileSizeMb } ; Ascending = $true }, `
        @{ Expression = { -1 * [double]$_.SampleTextPageRatio } ; Ascending = $true }, `
        @{ Expression = { $_.WarningPenalty } ; Ascending = $true }, `
        @{ Expression = { $_.FilePath } ; Ascending = $true }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    $directory = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath $Path -Value $json -Encoding utf8
}

function Write-MarkdownReport {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object[]]$Records,
        [Parameter(Mandatory = $true)][string]$OutputDir
    )

    $classOrder = @("TEXT_FAST", "TEXT_COMPLEX", "VISION_ONLY", "QUARANTINE")
    $builder = [System.Text.StringBuilder]::new()

    [void]$builder.AppendLine("# Current Library Preflight Queues")
    [void]$builder.AppendLine()
    $generatedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    [void]$builder.AppendLine(("Date: {0}" -f $generatedAt))
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("## Summary")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine(("- Total files: {0}" -f (($Records | Measure-Object).Count)))
    foreach ($className in $classOrder) {
        $count = @($Records | Where-Object { $_.Classification -eq $className }).Count
        [void]$builder.AppendLine(("- {0}: {1}" -f $className, $count))
    }
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("## Queue Files")
    [void]$builder.AppendLine()
    foreach ($className in $classOrder) {
        $queueFileName = ("queue_" + $className.ToLowerInvariant() + ".json")
        [void]$builder.AppendLine(("- {0}: {1}" -f $className, (Join-Path $OutputDir $queueFileName)))
    }
    [void]$builder.AppendLine()
    [void]$builder.AppendLine(("- Master JSON: {0}" -f (Join-Path $OutputDir "library_preflight_master.json")))
    [void]$builder.AppendLine()

    foreach ($className in $classOrder) {
        $classItems = @($Records | Where-Object { $_.Classification -eq $className })
        [void]$builder.AppendLine("## $className")
        [void]$builder.AppendLine()
        [void]$builder.AppendLine(("- Count: {0}" -f (($classItems | Measure-Object).Count)))
        $domainGroups = $classItems |
            Group-Object Domain |
            Sort-Object -Property @{ Expression = { $_.Count } ; Descending = $true }, @{ Expression = { $_.Name } ; Descending = $false }
        if (@($domainGroups).Count -gt 0) {
            [void]$builder.AppendLine("- Domains:")
            foreach ($group in $domainGroups) {
                [void]$builder.AppendLine("  - $($group.Name): $($group.Count)")
            }
        }

        $examples = $classItems | Select-Object -First 12
        if (@($examples).Count -gt 0) {
            [void]$builder.AppendLine("- First files:")
            foreach ($item in $examples) {
                $suffix = if ([string]::IsNullOrWhiteSpace($item.ClassificationReason)) { "" } else { " [$($item.ClassificationReason)]" }
                [void]$builder.AppendLine("  - $($item.FilePath)$suffix")
            }
        }

        [void]$builder.AppendLine()
    }

    $missingFromCurrentQueue = $Records | Where-Object {
        -not $_.InCurrentQueue -and $_.InLatestQueueBackup
    }
    if (@($missingFromCurrentQueue).Count -gt 0) {
        [void]$builder.AppendLine("## Missing From Current Queue")
        [void]$builder.AppendLine()
        foreach ($item in $missingFromCurrentQueue) {
            [void]$builder.AppendLine(("- {0} -> {1}" -f $item.FilePath, $item.Classification))
        }
        [void]$builder.AppendLine()
    }

    $directory = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    Set-Content -LiteralPath $Path -Value $builder.ToString() -Encoding utf8
}

$pathConfig = Get-HelperPathConfig -WorkspaceRoot $WorkspaceRoot
$helperRoot = $pathConfig.HelperRoot
$libraryDocsRoot = Join-Path $pathConfig.LibraryRoot "docs"

if (-not (Test-Path -LiteralPath $libraryDocsRoot)) {
    throw "Library docs root not found: $libraryDocsRoot"
}

$currentQueuePath = Resolve-CurrentQueuePath -PathConfig $pathConfig
$backupQueuePath = Resolve-LatestBackupQueuePath -PathConfig $pathConfig
$currentQueueMap = Read-QueueMap -Path $currentQueuePath
$backupQueueMap = Read-QueueMap -Path $backupQueuePath
$pdfProbePath = Resolve-PdfQuickProbePath -HelperRoot $helperRoot

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $OutputDir = Join-Path $pathConfig.OperatorRuntimeRoot ("library_preflight_queues_" + $stamp)
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $pathConfig.DocRoot ("library_preflight_queue_report_" + (Get-Date -Format "yyyy-MM-dd_HH-mm-ss") + ".md")
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$records = New-Object System.Collections.Generic.List[object]
$files = @(Get-IndexableFiles -LibraryDocsRoot $libraryDocsRoot)
$totalFiles = $files.Count
$processed = 0

foreach ($file in $files) {
    $processed++
    Write-Host ("[PreflightClassify] {0}/{1} {2}" -f $processed, $totalFiles, $file.FullName)
    $record = Classify-FileRecord `
        -File $file `
        -LibraryDocsRoot $libraryDocsRoot `
        -CurrentQueueMap $currentQueueMap `
        -BackupQueueMap $backupQueueMap `
        -PdfProbePath $pdfProbePath `
        -PdfProbeTimeoutSec $PdfProbeTimeoutSec
    $records.Add($record)
}

$sortedRecords = @($records.ToArray())
$classOrder = @("TEXT_FAST", "TEXT_COMPLEX", "VISION_ONLY", "QUARANTINE")
$queues = @{}

foreach ($className in $classOrder) {
    $classItems = Sort-ClassifiedItems -Items @($sortedRecords | Where-Object { $_.Classification -eq $className })
    $index = 0
    foreach ($item in $classItems) {
        $index++
        Add-Member -InputObject $item -NotePropertyName SuggestedOrderIndex -NotePropertyValue $index -Force
    }

    $queues[$className] = $classItems
    $queueFilePath = Join-Path $OutputDir ("queue_" + $className.ToLowerInvariant() + ".json")
    Write-JsonFile -Path $queueFilePath -Value ([PSCustomObject]@{
            GeneratedAt = (Get-Date).ToString("s")
            QueueClass = $className
            Count = @($classItems).Count
            Files = @($classItems | ForEach-Object { $_.FilePath })
        })
}

$masterPath = Join-Path $OutputDir "library_preflight_master.json"
Write-JsonFile -Path $masterPath -Value ([PSCustomObject]@{
        GeneratedAt = (Get-Date).ToString("s")
        WorkspaceRoot = $pathConfig.WorkspaceRoot
        HelperRoot = $helperRoot
        LibraryDocsRoot = $libraryDocsRoot
        CurrentQueuePath = $currentQueuePath
        LatestBackupQueuePath = $backupQueuePath
        PdfQuickProbePath = $pdfProbePath
        TotalFiles = $totalFiles
        ClassCounts = [PSCustomObject]@{
            TEXT_FAST = @($queues["TEXT_FAST"]).Count
            TEXT_COMPLEX = @($queues["TEXT_COMPLEX"]).Count
            VISION_ONLY = @($queues["VISION_ONLY"]).Count
            QUARANTINE = @($queues["QUARANTINE"]).Count
        }
        Records = $sortedRecords
    })

Write-MarkdownReport -Path $ReportPath -Records $sortedRecords -OutputDir $OutputDir

Write-Host ("[PreflightClassify] ReportPath=" + $ReportPath)
Write-Host ("[PreflightClassify] OutputDir=" + $OutputDir)
Write-Host ("[PreflightClassify] TEXT_FAST=" + @($queues["TEXT_FAST"]).Count)
Write-Host ("[PreflightClassify] TEXT_COMPLEX=" + @($queues["TEXT_COMPLEX"]).Count)
Write-Host ("[PreflightClassify] VISION_ONLY=" + @($queues["VISION_ONLY"]).Count)
Write-Host ("[PreflightClassify] QUARANTINE=" + @($queues["QUARANTINE"]).Count)
