param(
    [int]$TimeoutSec = 60,
    [string]$OutputJsonPath = "temp/verification/pdfepub_input_matrix.json",
    [string]$OutputMarkdownPath = "temp/verification/pdfepub_input_matrix.md"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-ReinvokeArgumentList {
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $PSBoundParameters.GetEnumerator()) {
        $items.Add("-" + [string]$entry.Key)
        $items.Add([string]$entry.Value)
    }

    foreach ($argument in $MyInvocation.UnboundArguments) {
        $items.Add([string]$argument)
    }

    return @($items.ToArray())
}

if ($PSVersionTable.PSEdition -eq "Core") {
    $windowsPowerShell = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
    if (Test-Path $windowsPowerShell) {
        & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath @(Get-ReinvokeArgumentList)
        exit $LASTEXITCODE
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function ConvertTo-QuotedArgument {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return '""'
    }

    if (($Value -notmatch '\s') -and (-not $Value.Contains('"'))) {
        return $Value
    }

    return '"' + $Value.Replace('"', '\"') + '"'
}

function ConvertTo-ArgumentLine {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    return ($Arguments | ForEach-Object { ConvertTo-QuotedArgument -Value $_ }) -join " "
}

function Get-TextTail {
    param(
        [Parameter(Mandatory = $true)][string[]]$Lines,
        [int]$Count = 10
    )

    if ($Lines.Count -eq 0) {
        return ""
    }

    return ($Lines | Select-Object -Last $Count) -join "`n"
}

function New-Epub2Fixture {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $entries = @(
                [pscustomobject]@{
                    Name = "mimetype"
                    Compression = [System.IO.Compression.CompressionLevel]::NoCompression
                    Content = "application/epub+zip"
                },
                [pscustomobject]@{
                    Name = "META-INF/container.xml"
                    Compression = [System.IO.Compression.CompressionLevel]::Optimal
                    Content = @"
<?xml version="1.0" encoding="utf-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles>
    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
  </rootfiles>
</container>
"@
                },
                [pscustomobject]@{
                    Name = "OEBPS/content.opf"
                    Compression = [System.IO.Compression.CompressionLevel]::Optimal
                    Content = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="2.0">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:title>Smoke Fixture</dc:title>
    <dc:creator>HELPER</dc:creator>
    <dc:language>en</dc:language>
    <dc:identifier id="BookId">urn:uuid:11111111-1111-1111-1111-111111111111</dc:identifier>
  </metadata>
  <manifest>
    <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
    <item id="chapter1" href="Text/chapter1.xhtml" media-type="application/xhtml+xml"/>
  </manifest>
  <spine toc="ncx">
    <itemref idref="chapter1"/>
  </spine>
</package>
"@
                },
                [pscustomobject]@{
                    Name = "OEBPS/toc.ncx"
                    Compression = [System.IO.Compression.CompressionLevel]::Optimal
                    Content = @"
<?xml version="1.0" encoding="utf-8"?>
<ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
  <head>
    <meta name="dtb:uid" content="urn:uuid:11111111-1111-1111-1111-111111111111"/>
    <meta name="dtb:depth" content="1"/>
    <meta name="dtb:totalPageCount" content="0"/>
    <meta name="dtb:maxPageNumber" content="0"/>
  </head>
  <docTitle>
    <text>Smoke Fixture</text>
  </docTitle>
  <navMap>
    <navPoint id="navPoint-1" playOrder="1">
      <navLabel><text>Chapter 1</text></navLabel>
      <content src="Text/chapter1.xhtml"/>
    </navPoint>
  </navMap>
</ncx>
"@
                },
                [pscustomobject]@{
                    Name = "OEBPS/Text/chapter1.xhtml"
                    Compression = [System.IO.Compression.CompressionLevel]::Optimal
                    Content = @"
<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml">
  <head><title>Smoke Fixture</title></head>
  <body>
    <h1>Smoke Fixture</h1>
    <p>Minimal EPUB2 payload.</p>
  </body>
</html>
"@
                }
            )

            foreach ($entry in $entries) {
                $zipEntry = $archive.CreateEntry($entry.Name, $entry.Compression)
                $writer = New-Object System.IO.StreamWriter($zipEntry.Open())
                try {
                    $writer.Write($entry.Content)
                }
                finally {
                    $writer.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $fs.Dispose()
    }
}

function New-Epub3Fixture {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $entries = @(
                [pscustomobject]@{
                    Name = "mimetype"
                    Compression = [System.IO.Compression.CompressionLevel]::NoCompression
                    Content = "application/epub+zip"
                },
                [pscustomobject]@{
                    Name = "META-INF/container.xml"
                    Compression = [System.IO.Compression.CompressionLevel]::Optimal
                    Content = @"
<?xml version="1.0" encoding="utf-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles>
    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
  </rootfiles>
</container>
"@
                },
                [pscustomobject]@{
                    Name = "OEBPS/content.opf"
                    Compression = [System.IO.Compression.CompressionLevel]::Optimal
                    Content = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="BookId">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="BookId">urn:helper:pdfepub-smoke</dc:identifier>
    <dc:title>Helper Smoke</dc:title>
    <dc:language>en</dc:language>
  </metadata>
  <manifest>
    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
    <item id="chapter1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
  </manifest>
  <spine>
    <itemref idref="chapter1"/>
  </spine>
</package>
"@
                },
                [pscustomobject]@{
                    Name = "OEBPS/nav.xhtml"
                    Compression = [System.IO.Compression.CompressionLevel]::Optimal
                    Content = @"
<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
  <head><title>Navigation</title></head>
  <body>
    <nav epub:type="toc" id="toc">
      <ol>
        <li><a href="chapter1.xhtml">Chapter 1</a></li>
      </ol>
    </nav>
  </body>
</html>
"@
                },
                [pscustomobject]@{
                    Name = "OEBPS/chapter1.xhtml"
                    Compression = [System.IO.Compression.CompressionLevel]::Optimal
                    Content = @"
<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml">
  <head><title>Helper Smoke</title></head>
  <body>
    <h1>Smoke Test</h1>
    <p>Minimal EPUB3 payload.</p>
  </body>
</html>
"@
                }
            )

            foreach ($entry in $entries) {
                $zipEntry = $archive.CreateEntry($entry.Name, $entry.Compression)
                $writer = New-Object System.IO.StreamWriter($zipEntry.Open())
                try {
                    $writer.Write($entry.Content)
                }
                finally {
                    $writer.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $fs.Dispose()
    }
}

function Invoke-ConversionProbe {
    param(
        [Parameter(Mandatory = $true)][string]$CaseId,
        [Parameter(Mandatory = $true)][string]$InputPath,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][int]$TimeoutSec
    )

    $stdoutPath = Join-Path $WorkingDirectory ($CaseId + ".stdout.log")
    $stderrPath = Join-Path $WorkingDirectory ($CaseId + ".stderr.log")
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = "C:\Program Files\Calibre2\ebook-convert.exe"
    $startInfo.Arguments = ConvertTo-ArgumentLine -Arguments $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $stdoutLines = New-Object System.Collections.Generic.List[string]
    $stderrLines = New-Object System.Collections.Generic.List[string]
    $stdoutHandler = [System.Diagnostics.DataReceivedEventHandler]{
        param($sender, $eventArgs)
        if ($null -ne $eventArgs.Data) {
            $stdoutLines.Add([string]$eventArgs.Data)
        }
    }
    $stderrHandler = [System.Diagnostics.DataReceivedEventHandler]{
        param($sender, $eventArgs)
        if ($null -ne $eventArgs.Data) {
            $stderrLines.Add([string]$eventArgs.Data)
        }
    }

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    $process.EnableRaisingEvents = $true
    $process.add_OutputDataReceived($stdoutHandler)
    $process.add_ErrorDataReceived($stderrHandler)
    $null = $process.Start()
    $process.BeginOutputReadLine()
    $process.BeginErrorReadLine()
    $timeline = New-Object System.Collections.Generic.List[object]

    try {
        for ($elapsed = 5; $elapsed -le $TimeoutSec; $elapsed += 5) {
            if (-not $process.WaitForExit(5000)) {
                $process.Refresh()
            }

            $running = -not $process.HasExited
            $cpu = $null
            if ($running) {
                try {
                    $cpu = (Get-Process -Id $process.Id -ErrorAction Stop).CPU
                }
                catch {
                    $cpu = $null
                }
            }

            $stdoutTailPreview = (Get-TextTail -Lines @($stdoutLines.ToArray()) -Count 3) -replace "`n", " || "
            $stderrTailPreview = (Get-TextTail -Lines @($stderrLines.ToArray()) -Count 3) -replace "`n", " || "

            $timeline.Add([pscustomobject]@{
                TickSec = $elapsed
                Running = $running
                Cpu = $cpu
                OutputExists = (Test-Path $OutputPath)
                OutputBytes = if (Test-Path $OutputPath) { (Get-Item $OutputPath).Length } else { 0 }
                StdoutTail = $stdoutTailPreview
                StderrTail = $stderrTailPreview
            })

            if ($process.HasExited) {
                break
            }
        }

        $timedOut = $false
        if (-not $process.HasExited) {
            $timedOut = $true
            $process.Kill($true)
            $process.WaitForExit()
        }

        $stopwatch.Stop()
        Set-Content -Path $stdoutPath -Value @($stdoutLines.ToArray()) -Encoding UTF8
        Set-Content -Path $stderrPath -Value @($stderrLines.ToArray()) -Encoding UTF8
        return [pscustomobject]@{
            CaseId = $CaseId
            InputPath = $InputPath
            OutputPath = $OutputPath
            TimedOut = $timedOut
            ExitCode = if ($timedOut) { $null } else { $process.ExitCode }
            DurationMs = [int]$stopwatch.ElapsedMilliseconds
            OutputExists = (Test-Path $OutputPath)
            OutputBytes = if (Test-Path $OutputPath) { (Get-Item $OutputPath).Length } else { 0 }
            StdoutPath = $stdoutPath
            StderrPath = $stderrPath
            StdoutTail = Get-TextTail -Lines @($stdoutLines.ToArray()) -Count 10
            StderrTail = Get-TextTail -Lines @($stderrLines.ToArray()) -Count 10
            Timeline = @($timeline)
        }
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            try {
                $process.Kill($true)
                $process.WaitForExit()
            }
            catch {
                # Best effort cleanup.
            }
        }

        if ($null -ne $process) {
            try {
                $process.remove_OutputDataReceived($stdoutHandler)
                $process.remove_ErrorDataReceived($stderrHandler)
            }
            catch {
                # Best effort cleanup.
            }

            $process.Dispose()
        }
    }
}

function Invoke-ProbeCase {
    param(
        [Parameter(Mandatory = $true)][string]$CaseId,
        [Parameter(Mandatory = $true)][string]$InputPath,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][int]$TimeoutSec
    )

    Write-Host ("[PdfEpubDiag][START] {0}" -f $CaseId) -ForegroundColor Cyan
    $result = Invoke-ConversionProbe `
        -CaseId $CaseId `
        -InputPath $InputPath `
        -OutputPath $OutputPath `
        -WorkingDirectory $WorkingDirectory `
        -Arguments $Arguments `
        -TimeoutSec $TimeoutSec

    $status = if ($result.TimedOut) {
        "TIMEOUT"
    }
    elseif ($result.OutputExists -and $result.ExitCode -eq 0) {
        "PASS"
    }
    else {
        "FAIL"
    }

    Write-Host ("[PdfEpubDiag][{0}] {1} output={2} bytes={3}" -f $status, $CaseId, $result.OutputExists, $result.OutputBytes) -ForegroundColor $(if ($status -eq "PASS") { "Green" } elseif ($status -eq "TIMEOUT") { "Yellow" } else { "Red" })
    return $result
}

function ConvertTo-DiagnosticMarkdown {
    param([Parameter(Mandatory = $true)]$Report)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# PDF EPUB Input Matrix")
    $lines.Add("")
    $lines.Add(("Generated at: {0}" -f $Report.generatedAtUtc))
    $lines.Add(("Timeout per case: {0}s" -f $Report.timeoutSec))
    $lines.Add(("Root: {0}" -f $Report.root))
    $lines.Add("")
    $lines.Add("| Case | Timed out | Exit | Output | Bytes |")
    $lines.Add("|---|---|---:|---|---:|")
    foreach ($case in @($Report.cases)) {
        $lines.Add(("| {0} | {1} | {2} | {3} | {4} |" -f $case.CaseId, $case.TimedOut, $(if ($null -eq $case.ExitCode) { "n/a" } else { $case.ExitCode }), $case.OutputExists, $case.OutputBytes))
    }

    $lines.Add("")
    $lines.Add("## Interpretation")
    $lines.Add("")
    foreach ($line in @($Report.interpretation)) {
        $lines.Add(("1. {0}" -f $line))
    }

    $lines.Add("")
    $lines.Add("## Case Details")
    $lines.Add("")
    foreach ($case in @($Report.cases)) {
        $lines.Add(("### {0}" -f $case.CaseId))
        $lines.Add("")
        $lines.Add(("- Timed out: {0}" -f $case.TimedOut))
        $lines.Add(("- Exit code: {0}" -f $(if ($null -eq $case.ExitCode) { "n/a" } else { $case.ExitCode })))
        $lines.Add(("- Output exists: {0}" -f $case.OutputExists))
        $lines.Add(("- Output bytes: {0}" -f $case.OutputBytes))
        $lines.Add(("- Stdout log: {0}" -f $case.StdoutPath.Replace('\', '/')))
        $lines.Add(("- Stderr log: {0}" -f $case.StderrPath.Replace('\', '/')))
        if (-not [string]::IsNullOrWhiteSpace([string]$case.StdoutTail)) {
            $lines.Add('- Stdout tail:')
            $lines.Add('```text')
            $lines.Add([string]$case.StdoutTail)
            $lines.Add('```')
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$case.StderrTail)) {
            $lines.Add('- Stderr tail:')
            $lines.Add('```text')
            $lines.Add([string]$case.StderrTail)
            $lines.Add('```')
        }
        $lines.Add("")
    }

    return ($lines -join "`r`n")
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedOutputJsonPath = Join-Path $repoRoot $OutputJsonPath
$resolvedOutputMarkdownPath = Join-Path $repoRoot $OutputMarkdownPath
$root = Join-Path $repoRoot ("temp\verification\pdfepub_input_matrix_" + (Get-Date -Format "yyyyMMdd_HHmmss"))

New-Item -ItemType Directory -Path $root -Force | Out-Null

$htmlPath = Join-Path $root "input_simple.html"
$epub2Path = Join-Path $root "input_epub2.epub"
$epub3Path = Join-Path $root "input_epub3.epub"

Set-Content -Path $htmlPath -Encoding UTF8 -Value @"
<!doctype html>
<html>
  <head><meta charset="utf-8"><title>Helper Smoke HTML</title></head>
  <body>
    <h1>Helper Smoke HTML</h1>
    <p>Minimal HTML payload.</p>
  </body>
</html>
"@
New-Epub2Fixture -Path $epub2Path
New-Epub3Fixture -Path $epub3Path

$htmlCase = Invoke-ProbeCase `
    -CaseId "html_to_pdf" `
    -InputPath $htmlPath `
    -OutputPath (Join-Path $root "html_to_pdf.pdf") `
    -WorkingDirectory $root `
    -Arguments @($htmlPath, (Join-Path $root "html_to_pdf.pdf"), "--paper-size=a4", "--pretty-print") `
    -TimeoutSec $TimeoutSec

$epub2Case = Invoke-ProbeCase `
    -CaseId "epub2_to_pdf" `
    -InputPath $epub2Path `
    -OutputPath (Join-Path $root "epub2_to_pdf.pdf") `
    -WorkingDirectory $root `
    -Arguments @($epub2Path, (Join-Path $root "epub2_to_pdf.pdf"), "--paper-size=a4", "--pretty-print") `
    -TimeoutSec $TimeoutSec

$epub3Case = Invoke-ProbeCase `
    -CaseId "epub3_to_pdf" `
    -InputPath $epub3Path `
    -OutputPath (Join-Path $root "epub3_to_pdf.pdf") `
    -WorkingDirectory $root `
    -Arguments @($epub3Path, (Join-Path $root "epub3_to_pdf.pdf"), "--paper-size=a4", "--pretty-print") `
    -TimeoutSec $TimeoutSec

$cases = @($htmlCase, $epub2Case, $epub3Case)

$interpretation = New-Object System.Collections.Generic.List[string]
if ($htmlCase.OutputExists -and -not $htmlCase.TimedOut) {
    $interpretation.Add("HTML to PDF succeeds, so Calibre is not generally broken on this machine.")
}
else {
    $interpretation.Add("HTML to PDF does not succeed, so the problem is broader than EPUB structure.")
}

if ($epub2Case.OutputExists -and -not $epub2Case.TimedOut -and -not ($epub3Case.OutputExists) -and $epub3Case.TimedOut) {
    $interpretation.Add("EPUB2 succeeds while EPUB3 times out, which points to the current EPUB3 smoke fixture as the likely blocker.")
}
elseif (-not $epub2Case.OutputExists -and $epub2Case.TimedOut -and -not $epub3Case.OutputExists -and $epub3Case.TimedOut) {
    $interpretation.Add("Both EPUB2 and EPUB3 time out, so the blocker is not isolated to the EPUB3 nav-based structure.")
}
elseif ($epub2Case.OutputExists -and $epub3Case.OutputExists) {
    $interpretation.Add("Both EPUB variants succeed; the remaining blocker is likely in harness integration rather than the input payload.")
}
else {
    $interpretation.Add("EPUB results are mixed and require targeted follow-up in the smoke harness.")
}

$report = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    timeoutSec = $TimeoutSec
    root = $root
    cases = $cases
    interpretation = @($interpretation.ToArray())
}

$jsonDirectory = Split-Path -Parent $resolvedOutputJsonPath
$markdownDirectory = Split-Path -Parent $resolvedOutputMarkdownPath
if (-not [string]::IsNullOrWhiteSpace($jsonDirectory)) {
    New-Item -ItemType Directory -Path $jsonDirectory -Force | Out-Null
}
if (-not [string]::IsNullOrWhiteSpace($markdownDirectory)) {
    New-Item -ItemType Directory -Path $markdownDirectory -Force | Out-Null
}

Set-Content -Path $resolvedOutputJsonPath -Value ($report | ConvertTo-Json -Depth 8) -Encoding UTF8
Set-Content -Path $resolvedOutputMarkdownPath -Value (ConvertTo-DiagnosticMarkdown -Report $report) -Encoding UTF8
Write-Host "[PdfEpubDiag] Saved: $OutputMarkdownPath" -ForegroundColor Green
