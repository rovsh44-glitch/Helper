using System.Diagnostics;
using System.Security;
using System.Text.Json;
using System.Xml.Linq;

namespace Helper.Runtime.Generation;

internal static class PdfEpubSmokeScenarioRunner
{
    public static async Task<TemplateCertificationSmokeScenario> EvaluateAsync(
        string templatePath,
        string scenarioId,
        CancellationToken ct)
    {
        const string description = "Template performs a real EPUB->PDF->EPUB roundtrip through Calibre.";
        if (!OperatingSystem.IsWindows())
        {
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: description,
                Passed: false,
                Details: "The pdf/epub roundtrip smoke test requires Windows.");
        }

        var calibreAvailability = CalibreAvailabilityProbe.GetCurrent();
        if (!calibreAvailability.IsOperational)
        {
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: description,
                Passed: false,
                Details: $"Calibre is installed but not operational: {calibreAvailability.Details}");
        }

        var projectFilePath = TryResolveProjectFilePath(templatePath);
        var assemblyPath = TryResolveBuiltAssemblyPath(templatePath);
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: description,
                Passed: false,
                Details: "Template project file was not found.");
        }

        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
        {
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: description,
                Passed: false,
                Details: "Built template assembly was not found. Run artifact validation before this smoke scenario.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "helper_pdfepub_smoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        bool cleanupTempRoot = true;

        try
        {
            var harnessRoot = Path.Combine(tempRoot, "harness");
            Directory.CreateDirectory(harnessRoot);
            var harnessProjectPath = Path.Combine(harnessRoot, "PdfEpubSmokeHarness.csproj");
            var harnessProgramPath = Path.Combine(harnessRoot, "Program.cs");

            var harnessTargetFramework = TryReadTargetFramework(projectFilePath) ?? "net8.0";
            await File.WriteAllTextAsync(harnessProjectPath, BuildSmokeHarnessProject(projectFilePath, harnessTargetFramework), ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(harnessProgramPath, BuildSmokeHarnessProgram(), ct).ConfigureAwait(false);

            var harnessResult = await RunSmokeHarnessAsync(harnessProjectPath, tempRoot, ct).ConfigureAwait(false);
            if (!harnessResult.Success)
            {
                cleanupTempRoot = false;
                return new TemplateCertificationSmokeScenario(
                    Id: scenarioId,
                    Description: description,
                    Passed: false,
                    Details: $"{harnessResult.Message} | DiagnosticsRoot={tempRoot}");
            }

            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: description,
                Passed: true,
                Details: $"Roundtrip succeeded. PDF={harnessResult.PdfBytes} bytes, EPUB={harnessResult.EpubBytes} bytes.");
        }
        catch (Exception ex)
        {
            cleanupTempRoot = false;
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: description,
                Passed: false,
                Details: $"Roundtrip execution failed: {ex.Message} | DiagnosticsRoot={tempRoot}");
        }
        finally
        {
            if (cleanupTempRoot)
            {
                TryDeleteDirectory(tempRoot);
            }
        }
    }

    private static async Task<SmokeHarnessResult> RunSmokeHarnessAsync(
        string harnessProjectPath,
        string workingDirectory,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(harnessProjectPath) ?? workingDirectory
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(harnessProjectPath);
        startInfo.ArgumentList.Add("--verbosity");
        startInfo.ArgumentList.Add("quiet");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(workingDirectory);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the pdf/epub smoke harness.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        Task waitForExitTask = process.WaitForExitAsync(timeout.Token);
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask, waitForExitTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort timeout cleanup.
            }

            return new SmokeHarnessResult(false, "Smoke harness timed out after 3 minutes.", 0, 0);
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);

        string[] stdoutLines = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? payload = stdoutLines.LastOrDefault(line => line.StartsWith("{", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                var result = JsonSerializer.Deserialize<SmokeHarnessResult>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (result is not null)
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                return new SmokeHarnessResult(false, $"Smoke harness produced an unreadable result: {ex.Message}. Payload: {payload}", 0, 0);
            }
        }

        string failureOutput = string.Join(
            Environment.NewLine,
            new[] { stdout.Trim(), stderr.Trim() }.Where(text => !string.IsNullOrWhiteSpace(text)));
        if (string.IsNullOrWhiteSpace(failureOutput))
        {
            failureOutput = $"Smoke harness exited with code {process.ExitCode} and did not produce a result file.";
        }

        return new SmokeHarnessResult(false, failureOutput, 0, 0);
    }

    private static string? TryResolveProjectFilePath(string templatePath)
    {
        return Directory.EnumerateFiles(templatePath, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(path => !TemplateSmokeScenarioCatalog.IsBuildArtifactPath(path));
    }

    private static string? TryResolveBuiltAssemblyPath(string templatePath)
    {
        var projectFile = TryResolveProjectFilePath(templatePath);
        if (string.IsNullOrWhiteSpace(projectFile))
        {
            return null;
        }

        var assemblyName = TryReadAssemblyName(projectFile) ?? Path.GetFileNameWithoutExtension(projectFile);
        var binRoot = Path.Combine(Path.GetDirectoryName(projectFile) ?? templatePath, "bin");
        if (!Directory.Exists(binRoot))
        {
            return null;
        }

        return Directory.EnumerateFiles(binRoot, $"{assemblyName}.dll", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => new FileInfo(path).Length)
            .FirstOrDefault();
    }

    private static string? TryReadAssemblyName(string projectFile)
    {
        try
        {
            var document = XDocument.Load(projectFile);
            return document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "AssemblyName", StringComparison.OrdinalIgnoreCase))
                ?.Value
                .Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadTargetFramework(string projectFile)
    {
        try
        {
            var document = XDocument.Load(projectFile);
            var singleTarget = document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "TargetFramework", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim();
            if (!string.IsNullOrWhiteSpace(singleTarget))
            {
                return singleTarget;
            }

            var targetFrameworks = document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim();
            if (string.IsNullOrWhiteSpace(targetFrameworks))
            {
                return null;
            }

            return targetFrameworks
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSmokeHarnessProject(string templateProjectPath, string targetFramework)
    {
        bool useWpf = targetFramework.Contains("-windows", StringComparison.OrdinalIgnoreCase);
        string escapedProjectPath = SecurityElement.Escape(templateProjectPath) ?? templateProjectPath;
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>{{targetFramework}}</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                {{(useWpf ? "<UseWPF>true</UseWPF>" : string.Empty)}}
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{escapedProjectPath}}" />
              </ItemGroup>
            </Project>
            """;
    }

    private static string BuildSmokeHarnessProgram()
    {
        return @"
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using PdfEpubConverter;
using PdfEpubConverter.Models;

if (args.Length < 1)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Success = false, Message = ""Missing working directory."", PdfBytes = 0L, EpubBytes = 0L }));
    return;
}

var root = args[0];
var inputDir = Path.Combine(root, ""input"");
var outputDir = Path.Combine(root, ""output"");
Directory.CreateDirectory(inputDir);
Directory.CreateDirectory(outputDir);

var epubPath = Path.Combine(inputDir, ""sample.epub"");
CreateMinimalEpub(epubPath);

var settings = new ConversionSettings
{
    OutputDirectory = outputDir,
    VerboseLogging = false,
    InsertMetadataPage = false,
    PreferMetadataCover = false,
    EnableHeuristics = false,
    AutoTrimPdfHeadersAndFooters = false,
    CreateInlineEpubToc = false,
    AddPdfPageNumbers = false,
    AddPdfToc = false,
    EmbedAllFonts = false,
    OutputProfile = ""default"",
    EpubVersion = ""3"",
    PdfPaperSize = ""a4""
};

var converter = new CalibreConversionService();
var pdfPath = Path.Combine(outputDir, ""sample.pdf"");
var pdfOutcome = await ExecuteConversionAsync(converter, settings, epubPath, pdfPath);
if (!pdfOutcome.Success)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Success = false, Message = ""EPUB->PDF failed: "" + pdfOutcome.Message, PdfBytes = 0L, EpubBytes = 0L }));
    return;
}

var roundTripEpubPath = Path.Combine(outputDir, ""sample-roundtrip.epub"");
var epubOutcome = await ExecuteConversionAsync(converter, settings, pdfPath, roundTripEpubPath);
if (!epubOutcome.Success)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Success = false, Message = ""PDF->EPUB failed: "" + epubOutcome.Message, PdfBytes = new FileInfo(pdfPath).Length, EpubBytes = 0L }));
    return;
}

var pdfInfo = new FileInfo(pdfPath);
var epubInfo = new FileInfo(roundTripEpubPath);
Console.WriteLine(JsonSerializer.Serialize(new
{
    Success = pdfInfo.Exists && epubInfo.Exists && pdfInfo.Length > 0 && epubInfo.Length > 0,
    Message = ""ok"",
    PdfBytes = pdfInfo.Exists ? pdfInfo.Length : 0,
    EpubBytes = epubInfo.Exists ? epubInfo.Length : 0
}));

static async Task<HarnessConversionOutcome> ExecuteConversionAsync(
    CalibreConversionService service,
    ConversionSettings settings,
    string sourcePath,
    string outputPath)
{
    string diagnosticArguments = BuildDiagnosticArguments(service, settings, sourcePath, outputPath);
    var startInfo = TryBuildStartInfo(service, settings, sourcePath, outputPath);
    if (startInfo is null)
    {
        return new HarnessConversionOutcome(false, ""Failed to construct ProcessStartInfo. | "" + diagnosticArguments);
    }

    HarnessProcessResult processResult = await RunProcessAsync(startInfo, sourcePath, outputPath, TimeSpan.FromSeconds(45));
    if (processResult.Success)
    {
        return new HarnessConversionOutcome(true, processResult.Message);
    }

    return new HarnessConversionOutcome(false, processResult.Message + "" | "" + diagnosticArguments);
}

static string BuildDiagnosticArguments(
    CalibreConversionService service,
    ConversionSettings settings,
    string sourcePath,
    string outputPath)
{
    try
    {
        var method = typeof(CalibreConversionService).GetMethod(
            ""BuildStartInfo"",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method?.Invoke(service, new object[] { sourcePath, outputPath, settings }) is not ProcessStartInfo startInfo)
        {
            return ""Failed to inspect ProcessStartInfo."";
        }

        return ""Command="" + startInfo.FileName + "" Args="" + startInfo.Arguments;
    }
    catch (Exception ex)
    {
        return ""Failed to inspect ProcessStartInfo: "" + ex.Message;
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
        var method = typeof(CalibreConversionService).GetMethod(
            ""BuildStartInfo"",
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
    string workingDirectory = ResolveWorkingDirectory(sourcePath, outputPath, startInfo.WorkingDirectory);
    startInfo.WorkingDirectory = workingDirectory;

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

    if (!process.Start())
    {
        return new HarnessProcessResult(false, false, ""Failed to start calibre conversion process."");
    }

    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
    Task<string> stderrTask = process.StandardError.ReadToEndAsync();
    Task exitTask = process.WaitForExitAsync();
    Task timeoutTask = Task.Delay(timeout);
    Task completedTask = await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask, exitTask), timeoutTask).ConfigureAwait(false);
    if (completedTask == timeoutTask)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort timeout cleanup.
        }

        return new HarnessProcessResult(false, true, ""Conversion timed out after "" + (int)Math.Ceiling(timeout.TotalSeconds) + ""s."");
    }

    string stdout = await stdoutTask.ConfigureAwait(false);
    string stderr = await stderrTask.ConfigureAwait(false);
    bool outputExists = File.Exists(outputPath);
    long outputBytes = outputExists ? new FileInfo(outputPath).Length : 0;
    bool success = process.ExitCode == 0 && outputExists && outputBytes > 0;
    if (success)
    {
        return new HarnessProcessResult(true, false, ""Completed successfully."");
    }

    string combined = string.Join(
        Environment.NewLine,
        new[] { stdout.Trim(), stderr.Trim() }.Where(text => !string.IsNullOrWhiteSpace(text)));
    if (string.IsNullOrWhiteSpace(combined))
    {
        combined = ""ExitCode="" + process.ExitCode + "" outputExists="" + outputExists + "" outputBytes="" + outputBytes;
    }

    return new HarnessProcessResult(false, false, combined);
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

static void CreateMinimalEpub(string epubPath)
{
    Directory.CreateDirectory(Path.GetDirectoryName(epubPath)!);
    if (File.Exists(epubPath))
    {
        File.Delete(epubPath);
    }

    using var archive = ZipFile.Open(epubPath, ZipArchiveMode.Create);
    WriteZipEntry(archive.CreateEntry(""mimetype"", CompressionLevel.NoCompression), ""application/epub+zip"");
    WriteZipEntry(
        archive.CreateEntry(""META-INF/container.xml"", CompressionLevel.Optimal),
        ""<?xml version=\""1.0\"" encoding=\""utf-8\""?>\n<container version=\""1.0\"" xmlns=\""urn:oasis:names:tc:opendocument:xmlns:container\"">\n  <rootfiles>\n    <rootfile full-path=\""OEBPS/content.opf\"" media-type=\""application/oebps-package+xml\""/>\n  </rootfiles>\n</container>"");
    WriteZipEntry(
        archive.CreateEntry(""OEBPS/content.opf"", CompressionLevel.Optimal),
        ""<?xml version=\""1.0\"" encoding=\""utf-8\""?>\n<package xmlns=\""http://www.idpf.org/2007/opf\"" unique-identifier=\""BookId\"" version=\""2.0\"">\n  <metadata xmlns:dc=\""http://purl.org/dc/elements/1.1/\"">\n    <dc:title>Smoke Fixture</dc:title>\n    <dc:creator>HELPER</dc:creator>\n    <dc:language>en</dc:language>\n    <dc:identifier id=\""BookId\"">urn:uuid:11111111-1111-1111-1111-111111111111</dc:identifier>\n  </metadata>\n  <manifest>\n    <item id=\""ncx\"" href=\""toc.ncx\"" media-type=\""application/x-dtbncx+xml\""/>\n    <item id=\""chapter1\"" href=\""Text/chapter1.xhtml\"" media-type=\""application/xhtml+xml\""/>\n  </manifest>\n  <spine toc=\""ncx\"">\n    <itemref idref=\""chapter1\""/>\n  </spine>\n</package>"");
    WriteZipEntry(
        archive.CreateEntry(""OEBPS/toc.ncx"", CompressionLevel.Optimal),
        ""<?xml version=\""1.0\"" encoding=\""utf-8\""?>\n<ncx xmlns=\""http://www.daisy.org/z3986/2005/ncx/\"" version=\""2005-1\"">\n  <head>\n    <meta name=\""dtb:uid\"" content=\""urn:uuid:11111111-1111-1111-1111-111111111111\""/>\n    <meta name=\""dtb:depth\"" content=\""1\""/>\n    <meta name=\""dtb:totalPageCount\"" content=\""0\""/>\n    <meta name=\""dtb:maxPageNumber\"" content=\""0\""/>\n  </head>\n  <docTitle>\n    <text>Smoke Fixture</text>\n  </docTitle>\n  <navMap>\n    <navPoint id=\""navPoint-1\"" playOrder=\""1\"">\n      <navLabel><text>Chapter 1</text></navLabel>\n      <content src=\""Text/chapter1.xhtml\""/>\n    </navPoint>\n  </navMap>\n</ncx>"");
    WriteZipEntry(
        archive.CreateEntry(""OEBPS/Text/chapter1.xhtml"", CompressionLevel.Optimal),
        ""<?xml version=\""1.0\"" encoding=\""utf-8\""?>\n<html xmlns=\""http://www.w3.org/1999/xhtml\"">\n  <head><title>Chapter 1</title></head>\n  <body>\n    <h1>Smoke Fixture</h1>\n    <p>This EPUB is generated by the HELPER smoke runner.</p>\n    <p>It exercises EPUB to PDF and PDF to EPUB roundtrip conversion.</p>\n  </body>\n</html>"");
}

static void WriteZipEntry(ZipArchiveEntry entry, string content)
{
    using Stream stream = entry.Open();
    using StreamWriter writer = new(stream, new UTF8Encoding(false));
    writer.Write(content);
    writer.Flush();
}

sealed record HarnessConversionOutcome(bool Success, string Message);
sealed record HarnessProcessResult(bool Success, bool TimedOut, string Message);
";
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Keep diagnostics artifacts if cleanup fails.
        }
    }

    private sealed record SmokeHarnessResult(bool Success, string Message, long PdfBytes, long EpubBytes);
}

