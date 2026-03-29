param(
    [int]$TimeoutSec = 30,
    [string]$TemplateProjectPath = "",
    [string]$OutputJsonPath = "temp/verification/pdfepub_controlled_harness.json",
    [string]$OutputMarkdownPath = "temp/verification/pdfepub_controlled_harness.md"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-RepoRoot {
    $current = Resolve-Path (Join-Path $PSScriptRoot "..")
    while ($null -ne $current) {
        if (Test-Path (Join-Path $current.Path "Helper.sln")) {
            return $current.Path
        }

        $parentPath = Split-Path $current.Path -Parent
        if ([string]::IsNullOrWhiteSpace($parentPath) -or ($parentPath -eq $current.Path)) {
            break
        }

        $current = Resolve-Path $parentPath
    }

    throw "Repo root was not found."
}

function Resolve-TemplateProjectPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$RequestedPath
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidate = if ([System.IO.Path]::IsPathRooted($RequestedPath)) {
            $RequestedPath
        }
        else {
            Join-Path $RepoRoot $RequestedPath
        }

        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }

        throw "Template project path was not found: $RequestedPath"
    }

    $candidates = @(
        (Join-Path $RepoRoot "library\forge_templates\Template_PdfEpubConverter\Project.csproj"),
        "D:\HELPER_DATA\library\forge_templates\Template_PdfEpubConverter\Project.csproj",
        (Join-Path $RepoRoot "temp\pdfepub_probe_short_clean\Project.csproj"),
        (Join-Path $RepoRoot "temp\pdfepub_cert_probe_clean\Project.csproj"),
        (Join-Path $RepoRoot "temp\Template_PdfEpubConverter_work\Project.csproj")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "No Template_PdfEpubConverter project file was found for the controlled harness."
}

function Copy-TemplateProjectTree {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$TargetRoot
    )

    New-Item -ItemType Directory -Force -Path $TargetRoot | Out-Null

    foreach ($directory in Get-ChildItem $SourceRoot -Directory -Recurse -Force) {
        $relative = $directory.FullName.Substring($SourceRoot.Length).TrimStart('\', '/')
        if ($relative.Split([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) -contains "bin") { continue }
        if ($relative.Split([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) -contains "obj") { continue }
        if ($relative.Split([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) -contains ".compile_gate") { continue }
        New-Item -ItemType Directory -Force -Path (Join-Path $TargetRoot $relative) | Out-Null
    }

    foreach ($file in Get-ChildItem $SourceRoot -File -Recurse -Force) {
        $relative = $file.FullName.Substring($SourceRoot.Length).TrimStart('\', '/')
        if ($relative.Split([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) -contains "bin") { continue }
        if ($relative.Split([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) -contains "obj") { continue }
        if ($relative.Split([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) -contains ".compile_gate") { continue }

        $destination = Join-Path $TargetRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item $file.FullName $destination -Force
    }
}

function Get-TargetFramework {
    param([Parameter(Mandatory = $true)][string]$ProjectFilePath)

    [xml]$document = Get-Content -Path $ProjectFilePath -Raw -Encoding UTF8
    $single = ($document.Project.PropertyGroup | Where-Object { $_.TargetFramework } | Select-Object -First 1).TargetFramework
    if (-not [string]::IsNullOrWhiteSpace($single)) {
        return [string]$single
    }

    $multi = ($document.Project.PropertyGroup | Where-Object { $_.TargetFrameworks } | Select-Object -First 1).TargetFrameworks
    if (-not [string]::IsNullOrWhiteSpace($multi)) {
        return ([string]$multi).Split(';')[0].Trim()
    }

    throw "TargetFramework was not found in $ProjectFilePath"
}

function Write-FileUtf8 {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function New-CaseMarkdown {
    param([Parameter(Mandatory = $true)]$Summary)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# PDF/EPUB Controlled Harness Report")
    $lines.Add("")
    $lines.Add(("Generated: ``" + [string]$Summary.generatedAt + "``"))
    $lines.Add(("Template project: ``" + [string]$Summary.templateProjectPath + "``"))
    $lines.Add(("Template source root: ``" + [string]$Summary.templateSourceRoot + "``"))
    $lines.Add(("Run root: ``" + [string]$Summary.runRoot + "``"))
    $lines.Add(("Exit code: ``" + [string]$Summary.exitCode + "``"))
    $lines.Add(("Status: ``" + [string]$Summary.status + "``"))
    $lines.Add(("Console log: [controlled_harness.console.log](" + [string]$Summary.consoleLogPath + ")"))
    $lines.Add("")
    $lines.Add("| Case | Input | Success | Timed Out | Output | Bytes | Duration ms |")
    $lines.Add("| --- | --- | --- | --- | --- | ---: | ---: |")

    foreach ($case in $Summary.cases) {
        $row = "| " `
            + [string]$case.caseId `
            + " | " `
            + [string]$case.inputKind `
            + " | " `
            + $(if ($case.success) { "yes" } else { "no" }) `
            + " | " `
            + $(if ($case.timedOut) { "yes" } else { "no" }) `
            + " | " `
            + $(if ($case.outputExists) { "yes" } else { "no" }) `
            + " | " `
            + [string]$case.outputBytes `
            + " | " `
            + [string]$case.durationMs `
            + " |"
        $lines.Add($row)
    }

    $lines.Add("")
    $lines.Add("## Details")
    $lines.Add("")

    foreach ($case in $Summary.cases) {
        $lines.Add(("### " + [string]$case.caseId))
        $lines.Add("")
        $lines.Add(("- Input kind: ``" + [string]$case.inputKind + "``"))
        $lines.Add(("- Success: ``" + [string]$case.success + "``"))
        $lines.Add(("- Timed out: ``" + [string]$case.timedOut + "``"))
        $lines.Add(("- Output path: ``" + [string]$case.outputPath + "``"))
        $lines.Add(("- Output bytes: ``" + [string]$case.outputBytes + "``"))
        $lines.Add(("- Duration ms: ``" + [string]$case.durationMs + "``"))
        $lines.Add(("- Message: ``" + [string]$case.message + "``"))
        $lines.Add(("- Diagnostic arguments: ``" + [string]$case.diagnosticArguments + "``"))
        $lines.Add("")
    }

    return ($lines -join "`r`n")
}

$repoRoot = Get-RepoRoot
$resolvedTemplateProjectPath = Resolve-TemplateProjectPath -RepoRoot $repoRoot -RequestedPath $TemplateProjectPath
$templateSourceRoot = Split-Path $resolvedTemplateProjectPath -Parent
$targetFramework = Get-TargetFramework -ProjectFilePath $resolvedTemplateProjectPath
$useWpf = $targetFramework.Contains("-windows")

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$runRoot = Join-Path $repoRoot ("temp\verification\pdfepub_controlled_harness_" + $timestamp)
$templateCopyRoot = Join-Path $runRoot "template"
$harnessRoot = Join-Path $runRoot "harness"
$harnessProjectPath = Join-Path $harnessRoot "PdfEpubControlledHarness.csproj"
$harnessProgramPath = Join-Path $harnessRoot "Program.cs"
$resultsPath = Join-Path $runRoot "controlled_harness_results.json"
$consoleLogPath = Join-Path $runRoot "controlled_harness.console.log"

foreach ($path in @($runRoot, $templateCopyRoot, $harnessRoot, (Split-Path $OutputJsonPath -Parent), (Split-Path $OutputMarkdownPath -Parent))) {
    if (-not [string]::IsNullOrWhiteSpace($path)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }
}

Copy-TemplateProjectTree -SourceRoot $templateSourceRoot -TargetRoot $templateCopyRoot
$copiedTemplateProjectPath = Join-Path $templateCopyRoot ([System.IO.Path]::GetFileName($resolvedTemplateProjectPath))
$escapedProjectPath = [System.Security.SecurityElement]::Escape($copiedTemplateProjectPath)

$projectContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$targetFramework</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    $(if ($useWpf) { "<UseWPF>true</UseWPF>" } else { "" })
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$escapedProjectPath" />
  </ItemGroup>
</Project>
"@

$programContent = @'
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using PdfEpubConverter;
using PdfEpubConverter.Models;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: PdfEpubControlledHarness <run-root> <timeout-seconds>");
    return 2;
}

string runRoot = args[0];
if (!int.TryParse(args[1], out int timeoutSec) || timeoutSec <= 0)
{
    Console.Error.WriteLine("Invalid timeout.");
    return 3;
}

Directory.CreateDirectory(runRoot);
string inputsRoot = Path.Combine(runRoot, "inputs");
string outputsRoot = Path.Combine(runRoot, "outputs");
string timelinePath = Path.Combine(runRoot, "timeline.log");
string resultsPath = Path.Combine(runRoot, "controlled_harness_results.json");
Directory.CreateDirectory(inputsRoot);
Directory.CreateDirectory(outputsRoot);

WriteTimeline(timelinePath, "[Harness][START] timeoutSec=" + timeoutSec);

var settings = new ConversionSettings
{
    OutputDirectory = outputsRoot,
    VerboseLogging = false,
    InsertMetadataPage = false,
    PreferMetadataCover = false,
    EnableHeuristics = false,
    AutoTrimPdfHeadersAndFooters = false,
    CreateInlineEpubToc = false,
    AddPdfPageNumbers = false,
    AddPdfToc = false,
    EmbedAllFonts = false,
    OutputProfile = "default",
    EpubVersion = "3",
    PdfPaperSize = "a4"
};

var service = new CalibreConversionService();
var cases = new[]
{
    new HarnessCase("html_to_pdf", "html", Path.Combine(inputsRoot, "input_simple.html"), Path.Combine(outputsRoot, "html_to_pdf.pdf"), CreateHtmlFixture),
    new HarnessCase("epub2_to_pdf", "epub2", Path.Combine(inputsRoot, "input_epub2.epub"), Path.Combine(outputsRoot, "epub2_to_pdf.pdf"), CreateEpub2Fixture),
    new HarnessCase("epub3_to_pdf", "epub3", Path.Combine(inputsRoot, "input_epub3.epub"), Path.Combine(outputsRoot, "epub3_to_pdf.pdf"), CreateEpub3Fixture)
};

var results = new List<CaseResult>();
foreach (var testCase in cases)
{
    testCase.CreateFixture(testCase.InputPath);
    string diagnosticArgs = BuildDiagnosticArguments(service, settings, testCase.InputPath, testCase.OutputPath);
    WriteTimeline(timelinePath, $"[Harness][START] {testCase.CaseId} input={testCase.InputKind}");

    var stopwatch = Stopwatch.StartNew();
    try
    {
        var startInfo = TryBuildStartInfo(service, settings, testCase.InputPath, testCase.OutputPath);
        if (startInfo is null)
        {
            throw new InvalidOperationException("Failed to construct ProcessStartInfo.");
        }

        HarnessProcessResult outcome = await RunProcessAsync(startInfo, testCase.InputPath, testCase.OutputPath, TimeSpan.FromSeconds(timeoutSec));
        stopwatch.Stop();

        bool outputExists = outcome.OutputExists;
        long outputBytes = outcome.OutputBytes;
        bool timedOut = outcome.TimedOut;
        bool success = outcome.Success;
        string message = outcome.Message;

        WriteTimeline(timelinePath, $"[Harness][{(success ? "PASS" : timedOut ? "TIMEOUT" : "FAIL")}] {testCase.CaseId} durationMs={stopwatch.ElapsedMilliseconds} bytes={outputBytes}");
        results.Add(new CaseResult(
            testCase.CaseId,
            testCase.InputKind,
            success,
            timedOut,
            outputExists,
            outputBytes,
            stopwatch.ElapsedMilliseconds,
            message,
            diagnosticArgs,
            testCase.OutputPath));
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        WriteTimeline(timelinePath, $"[Harness][ERROR] {testCase.CaseId} {ex.GetType().Name}: {ex.Message}");
        results.Add(new CaseResult(
            testCase.CaseId,
            testCase.InputKind,
            false,
            false,
            File.Exists(testCase.OutputPath),
            File.Exists(testCase.OutputPath) ? new FileInfo(testCase.OutputPath).Length : 0L,
            stopwatch.ElapsedMilliseconds,
            ex.GetType().Name + ": " + ex.Message,
            diagnosticArgs,
            testCase.OutputPath));
    }
}

var payload = new HarnessPayload(
    DateTimeOffset.UtcNow,
    Environment.CurrentDirectory,
    timelinePath,
    results);

await File.WriteAllTextAsync(resultsPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
WriteTimeline(timelinePath, "[Harness][DONE] results=" + resultsPath);
return 0;

static string BuildDiagnosticArguments(
    CalibreConversionService service,
    ConversionSettings settings,
    string sourcePath,
    string outputPath)
{
    try
    {
        MethodInfo? method = typeof(CalibreConversionService).GetMethod(
            "BuildStartInfo",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method?.Invoke(service, new object[] { sourcePath, outputPath, settings }) is not ProcessStartInfo startInfo)
        {
            return "ProcessStartInfo unavailable.";
        }

        return "Command=" + startInfo.FileName + " Args=" + startInfo.Arguments;
    }
    catch (Exception ex)
    {
        return "ProcessStartInfo inspection failed: " + ex.Message;
    }
}

static ProcessStartInfo? TryBuildStartInfo(
    CalibreConversionService service,
    ConversionSettings settings,
    string sourcePath,
    string outputPath)
{
    try
    {
        MethodInfo? method = typeof(CalibreConversionService).GetMethod(
            "BuildStartInfo",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return method?.Invoke(service, new object[] { sourcePath, outputPath, settings }) as ProcessStartInfo;
    }
    catch
    {
        return null;
    }
}

static async Task<HarnessProcessResult> RunProcessAsync(
    ProcessStartInfo startInfo,
    string sourcePath,
    string outputPath,
    TimeSpan timeout)
{
    startInfo.WorkingDirectory = ResolveWorkingDirectory(sourcePath, outputPath, startInfo.WorkingDirectory);
    startInfo.UseShellExecute = false;
    startInfo.RedirectStandardOutput = true;
    startInfo.RedirectStandardError = true;
    startInfo.CreateNoWindow = true;
    startInfo.StandardOutputEncoding = Encoding.UTF8;
    startInfo.StandardErrorEncoding = Encoding.UTF8;

    using var process = new Process
    {
        StartInfo = startInfo,
        EnableRaisingEvents = true
    };
    var stdoutLines = new List<string>();
    var stderrLines = new List<string>();
    DataReceivedEventHandler stdoutHandler = (_, eventArgs) =>
    {
        if (!string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            lock (stdoutLines)
            {
                stdoutLines.Add(eventArgs.Data);
            }
        }
    };
    DataReceivedEventHandler stderrHandler = (_, eventArgs) =>
    {
        if (!string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            lock (stderrLines)
            {
                stderrLines.Add(eventArgs.Data);
            }
        }
    };

    process.OutputDataReceived += stdoutHandler;
    process.ErrorDataReceived += stderrHandler;
    if (!process.Start())
    {
        return new HarnessProcessResult(false, false, false, 0L, "Failed to start ebook-convert.");
    }

    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    Task exitTask = process.WaitForExitAsync();

    try
    {
        Task completedTask = await Task.WhenAny(exitTask, Task.Delay(timeout));
        if (!ReferenceEquals(completedTask, exitTask))
        {
            TryKillProcessTree(process);
            await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(5)));
            bool timedOutOutputExists = File.Exists(outputPath);
            long timedOutOutputBytes = timedOutOutputExists ? new FileInfo(outputPath).Length : 0L;
            return new HarnessProcessResult(
                false,
                true,
                timedOutOutputExists,
                timedOutOutputBytes,
                "Conversion timed out after " + (int)timeout.TotalSeconds + "s. stdout=" + Tail(stdoutLines) + " stderr=" + Tail(stderrLines));
        }

        await exitTask;
        bool outputExists = File.Exists(outputPath);
        long outputBytes = outputExists ? new FileInfo(outputPath).Length : 0L;
        if (process.ExitCode == 0 && outputExists && outputBytes > 0)
        {
            return new HarnessProcessResult(true, false, outputExists, outputBytes, "Completed successfully.");
        }

        return new HarnessProcessResult(
            false,
            false,
            outputExists,
            outputBytes,
            "ExitCode=" + process.ExitCode + " outputExists=" + outputExists + " outputBytes=" + outputBytes + " stdout=" + Tail(stdoutLines) + " stderr=" + Tail(stderrLines));
    }
    finally
    {
        try
        {
            process.CancelOutputRead();
        }
        catch
        {
        }

        try
        {
            process.CancelErrorRead();
        }
        catch
        {
        }

        process.OutputDataReceived -= stdoutHandler;
        process.ErrorDataReceived -= stderrHandler;
    }
}

static void TryKillProcessTree(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
    }
    catch
    {
        try
        {
            using var fallback = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/PID " + process.Id + " /T /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            fallback.Start();
            fallback.WaitForExit(5000);
        }
        catch
        {
        }
    }
}

static string ResolveWorkingDirectory(string sourcePath, string outputPath, string fallbackWorkingDirectory)
{
    string? sourceRoot = Path.GetDirectoryName(Path.GetDirectoryName(sourcePath));
    string? outputRoot = Path.GetDirectoryName(Path.GetDirectoryName(outputPath));
    if (!string.IsNullOrWhiteSpace(sourceRoot)
        && !string.IsNullOrWhiteSpace(outputRoot)
        && string.Equals(sourceRoot, outputRoot, StringComparison.OrdinalIgnoreCase)
        && Directory.Exists(sourceRoot))
    {
        return sourceRoot;
    }

    return fallbackWorkingDirectory;
}

static string Tail(IEnumerable<string> lines)
{
    string[] snapshot = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
    if (snapshot.Length == 0)
    {
        return "<empty>";
    }

    return string.Join(" || ", snapshot.TakeLast(8));
}

static void CreateHtmlFixture(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(
        path,
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\" /><title>Helper Harness</title></head><body><h1>Helper Harness</h1><p>HTML to PDF controlled fixture.</p></body></html>",
        new UTF8Encoding(false));
}

static void CreateEpub2Fixture(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    if (File.Exists(path))
    {
        File.Delete(path);
    }

    using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
    WriteZipEntry(archive.CreateEntry("mimetype", CompressionLevel.NoCompression), "application/epub+zip");
    WriteZipEntry(
        archive.CreateEntry("META-INF/container.xml", CompressionLevel.Optimal),
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\n  <rootfiles>\n    <rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/>\n  </rootfiles>\n</container>");
    WriteZipEntry(
        archive.CreateEntry("OEBPS/content.opf", CompressionLevel.Optimal),
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<package xmlns=\"http://www.idpf.org/2007/opf\" unique-identifier=\"BookId\" version=\"2.0\">\n  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n    <dc:title>Controlled EPUB2</dc:title>\n    <dc:creator>HELPER</dc:creator>\n    <dc:language>en</dc:language>\n    <dc:identifier id=\"BookId\">urn:uuid:22222222-2222-2222-2222-222222222222</dc:identifier>\n  </metadata>\n  <manifest>\n    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>\n    <item id=\"chapter1\" href=\"Text/chapter1.xhtml\" media-type=\"application/xhtml+xml\"/>\n  </manifest>\n  <spine toc=\"ncx\">\n    <itemref idref=\"chapter1\"/>\n  </spine>\n</package>");
    WriteZipEntry(
        archive.CreateEntry("OEBPS/toc.ncx", CompressionLevel.Optimal),
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">\n  <head>\n    <meta name=\"dtb:uid\" content=\"urn:uuid:22222222-2222-2222-2222-222222222222\"/>\n    <meta name=\"dtb:depth\" content=\"1\"/>\n    <meta name=\"dtb:totalPageCount\" content=\"0\"/>\n    <meta name=\"dtb:maxPageNumber\" content=\"0\"/>\n  </head>\n  <docTitle>\n    <text>Controlled EPUB2</text>\n  </docTitle>\n  <navMap>\n    <navPoint id=\"navPoint-1\" playOrder=\"1\">\n      <navLabel><text>Chapter 1</text></navLabel>\n      <content src=\"Text/chapter1.xhtml\"/>\n    </navPoint>\n  </navMap>\n</ncx>");
    WriteZipEntry(
        archive.CreateEntry("OEBPS/Text/chapter1.xhtml", CompressionLevel.Optimal),
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n  <head><title>Controlled EPUB2</title></head>\n  <body>\n    <h1>Controlled EPUB2</h1>\n    <p>EPUB2 to PDF controlled fixture.</p>\n  </body>\n</html>");
}

static void CreateEpub3Fixture(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    if (File.Exists(path))
    {
        File.Delete(path);
    }

    using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
    WriteZipEntry(archive.CreateEntry("mimetype", CompressionLevel.NoCompression), "application/epub+zip");
    WriteZipEntry(
        archive.CreateEntry("META-INF/container.xml", CompressionLevel.Optimal),
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\n  <rootfiles>\n    <rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/>\n  </rootfiles>\n</container>");
    WriteZipEntry(
        archive.CreateEntry("OEBPS/content.opf", CompressionLevel.Optimal),
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"BookId\">\n  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n    <dc:identifier id=\"BookId\">urn:helper:controlled-epub3</dc:identifier>\n    <dc:title>Controlled EPUB3</dc:title>\n    <dc:language>en</dc:language>\n  </metadata>\n  <manifest>\n    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n    <item id=\"chapter1\" href=\"chapter1.xhtml\" media-type=\"application/xhtml+xml\"/>\n  </manifest>\n  <spine>\n    <itemref idref=\"chapter1\"/>\n  </spine>\n</package>");
    WriteZipEntry(
        archive.CreateEntry("OEBPS/nav.xhtml", CompressionLevel.Optimal),
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n  <head><title>Navigation</title></head>\n  <body>\n    <nav epub:type=\"toc\" id=\"toc\">\n      <ol>\n        <li><a href=\"chapter1.xhtml\">Chapter 1</a></li>\n      </ol>\n    </nav>\n  </body>\n</html>");
    WriteZipEntry(
        archive.CreateEntry("OEBPS/chapter1.xhtml", CompressionLevel.Optimal),
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n  <head><title>Controlled EPUB3</title></head>\n  <body>\n    <h1>Controlled EPUB3</h1>\n    <p>EPUB3 to PDF controlled fixture.</p>\n  </body>\n</html>");
}

static void WriteZipEntry(ZipArchiveEntry entry, string content)
{
    using Stream stream = entry.Open();
    using StreamWriter writer = new(stream, new UTF8Encoding(false));
    writer.Write(content);
    writer.Flush();
}

static void WriteTimeline(string timelinePath, string message)
{
    string line = DateTimeOffset.UtcNow.ToString("O") + " " + message;
    Console.WriteLine(line);
    File.AppendAllText(timelinePath, line + Environment.NewLine);
}

internal sealed record HarnessCase(string CaseId, string InputKind, string InputPath, string OutputPath, Action<string> CreateFixture);
internal sealed record CaseResult(string CaseId, string InputKind, bool Success, bool TimedOut, bool OutputExists, long OutputBytes, long DurationMs, string Message, string DiagnosticArguments, string OutputPath);
internal sealed record HarnessProcessResult(bool Success, bool TimedOut, bool OutputExists, long OutputBytes, string Message);
internal sealed record HarnessPayload(DateTimeOffset GeneratedAtUtc, string WorkingDirectory, string TimelinePath, IReadOnlyList<CaseResult> Cases);
'@

Write-FileUtf8 -Path $harnessProjectPath -Content $projectContent
Write-FileUtf8 -Path $harnessProgramPath -Content $programContent

Write-Host ("[PdfEpubHarness][START] runRoot={0}" -f $runRoot) -ForegroundColor Cyan
$originalNativeErrorPreference = $false
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
    $originalNativeErrorPreference = $global:PSNativeCommandUseErrorActionPreference
    $global:PSNativeCommandUseErrorActionPreference = $false
}

try {
    & dotnet run --project $harnessProjectPath --verbosity quiet -- $runRoot $TimeoutSec *> $consoleLogPath
}
finally {
    if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
        $global:PSNativeCommandUseErrorActionPreference = $originalNativeErrorPreference
    }
}

$exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }

if (-not (Test-Path $resultsPath)) {
    $summary = [pscustomobject]@{
        generatedAt = [DateTime]::UtcNow.ToString("o")
        exitCode = $exitCode
        templateProjectPath = $resolvedTemplateProjectPath
        templateSourceRoot = $templateSourceRoot
        runRoot = $runRoot
        consoleLogPath = $consoleLogPath
        cases = @()
        status = "fail"
        message = "Controlled harness did not produce results."
    }
}
else {
    $payload = Get-Content -Path $resultsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $caseItems = @($payload.Cases)
    $failedCaseCount = @($caseItems | Where-Object { $_.Success -ne $true }).Count
    $summary = [pscustomobject]@{
        generatedAt = [DateTime]::UtcNow.ToString("o")
        exitCode = $exitCode
        templateProjectPath = $resolvedTemplateProjectPath
        templateSourceRoot = $templateSourceRoot
        runRoot = $runRoot
        consoleLogPath = $consoleLogPath
        cases = $caseItems
        status = $(if (($exitCode -eq 0) -and ($failedCaseCount -eq 0)) { "pass" } else { "fail" })
        message = $(if ($failedCaseCount -eq 0) { "ok" } else { "$failedCaseCount case(s) failed." })
    }
}

$summaryJson = $summary | ConvertTo-Json -Depth 10
Write-FileUtf8 -Path (Join-Path $repoRoot $OutputJsonPath) -Content $summaryJson
Write-FileUtf8 -Path (Join-Path $repoRoot $OutputMarkdownPath) -Content (New-CaseMarkdown -Summary $summary)

$finalExitCode = if ([string]$summary.status -eq "pass") { 0 } else { 1 }
Write-Host ("[PdfEpubHarness][DONE] exitCode={0} status={1} json={2}" -f $finalExitCode, $summary.status, (Join-Path $repoRoot $OutputJsonPath)) -ForegroundColor Green
exit $finalExitCode

