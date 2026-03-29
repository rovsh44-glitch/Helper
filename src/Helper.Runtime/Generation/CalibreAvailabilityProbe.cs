using System.Diagnostics;
using System.Text;

namespace Helper.Runtime.Generation;

internal static class CalibreAvailabilityProbe
{
    private const int DefaultVersionProbeTimeoutMs = 2000;
    private const int DefaultConversionProbeTimeoutMs = 4000;
    private static readonly Lazy<CalibreAvailabilityResult> CachedResult = new(ProbeCore, isThreadSafe: true);

    public static CalibreAvailabilityResult GetCurrent() => CachedResult.Value;

    private static CalibreAvailabilityResult ProbeCore()
    {
        string? ebookConvertPath = FindExecutable("ebook-convert.exe");
        if (string.IsNullOrWhiteSpace(ebookConvertPath) || !File.Exists(ebookConvertPath))
        {
            return new CalibreAvailabilityResult(false, ebookConvertPath, "ebook-convert.exe was not found.");
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "helper_calibre_probe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        string inputPath = Path.Combine(tempRoot, "probe.html");
        string outputPath = Path.Combine(tempRoot, "probe.pdf");

        try
        {
            var versionProbe = RunProcessProbe(
                ebookConvertPath,
                arguments: new[] { "--version" },
                workingDirectory: tempRoot,
                timeoutMs: GetTimeoutMs("HELPER_CALIBRE_VERSION_PROBE_TIMEOUT_MS", DefaultVersionProbeTimeoutMs));
            if (!versionProbe.Started)
            {
                return new CalibreAvailabilityResult(false, ebookConvertPath, "ebook-convert.exe could not be started for --version.");
            }

            if (versionProbe.TimedOut)
            {
                return new CalibreAvailabilityResult(
                    false,
                    ebookConvertPath,
                    $"ebook-convert.exe timed out during --version preflight. stdout={Tail(versionProbe.StdoutLines)} stderr={Tail(versionProbe.StderrLines)}");
            }

            if (versionProbe.ExitCode != 0)
            {
                return new CalibreAvailabilityResult(
                    false,
                    ebookConvertPath,
                    $"ebook-convert.exe failed --version preflight: exitCode={versionProbe.ExitCode}; stdout={Tail(versionProbe.StdoutLines)} stderr={Tail(versionProbe.StderrLines)}");
            }

            File.WriteAllText(
                inputPath,
                """
                <!doctype html>
                <html>
                  <head><meta charset="utf-8"><title>HELPER Calibre Probe</title></head>
                  <body><h1>HELPER Calibre Probe</h1><p>Minimal HTML to PDF preflight.</p></body>
                </html>
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var conversionProbe = RunProcessProbe(
                ebookConvertPath,
                arguments: new[] { inputPath, outputPath },
                workingDirectory: tempRoot,
                timeoutMs: GetTimeoutMs("HELPER_CALIBRE_CONVERSION_PROBE_TIMEOUT_MS", DefaultConversionProbeTimeoutMs));
            if (!conversionProbe.Started)
            {
                return new CalibreAvailabilityResult(false, ebookConvertPath, "ebook-convert.exe could not be started for the conversion preflight.");
            }

            if (conversionProbe.TimedOut)
            {
                return new CalibreAvailabilityResult(
                    false,
                    ebookConvertPath,
                    $"ebook-convert.exe responded to --version but a minimal HTML->PDF preflight did not complete within {conversionProbe.TimeoutMs} ms. stdout={Tail(conversionProbe.StdoutLines)} stderr={Tail(conversionProbe.StderrLines)}");
            }

            bool outputExists = File.Exists(outputPath);
            long outputBytes = outputExists ? new FileInfo(outputPath).Length : 0L;
            if (conversionProbe.ExitCode == 0 && outputExists && outputBytes > 0)
            {
                return new CalibreAvailabilityResult(
                    true,
                    ebookConvertPath,
                    $"ebook-convert.exe passed quick HTML->PDF preflight in {conversionProbe.DurationMs} ms.");
            }

            return new CalibreAvailabilityResult(
                false,
                ebookConvertPath,
                $"ebook-convert.exe responded to --version but failed minimal HTML->PDF preflight: exitCode={conversionProbe.ExitCode}; outputExists={outputExists}; outputBytes={outputBytes}; stdout={Tail(conversionProbe.StdoutLines)} stderr={Tail(conversionProbe.StderrLines)}");
        }
        catch (Exception ex)
        {
            return new CalibreAvailabilityResult(false, ebookConvertPath, $"ebook-convert.exe preflight threw: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static string? FindExecutable(string fileName)
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Calibre2", fileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Calibre2", fileName)
        };

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            candidates.AddRange(pathValue
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(path => Path.Combine(path, fileName)));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static ProcessProbeResult RunProcessProbe(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutMs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

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

        var startedAt = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                return new ProcessProbeResult(false, false, null, timeoutMs, 0, Array.Empty<string>(), Array.Empty<string>());
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                TryKillProcessTree(process);
                process.WaitForExit(1000);
                return new ProcessProbeResult(
                    true,
                    true,
                    null,
                    timeoutMs,
                    (int)startedAt.ElapsedMilliseconds,
                    stdoutLines.ToArray(),
                    stderrLines.ToArray());
            }

            process.WaitForExit();
            return new ProcessProbeResult(
                true,
                false,
                process.ExitCode,
                timeoutMs,
                (int)startedAt.ElapsedMilliseconds,
                stdoutLines.ToArray(),
                stderrLines.ToArray());
        }
        finally
        {
            startedAt.Stop();
            process.OutputDataReceived -= stdoutHandler;
            process.ErrorDataReceived -= stderrHandler;
        }
    }

    private static int GetTimeoutMs(string variableName, int fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(variableName);
        if (int.TryParse(raw, out int parsed) && parsed > 0)
        {
            return parsed;
        }

        return fallback;
    }

    private static string Tail(IReadOnlyCollection<string> lines)
    {
        if (lines.Count == 0)
        {
            return "<empty>";
        }

        return string.Join(" || ", lines.TakeLast(8));
    }

    private static void TryKillProcessTree(Process process)
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
            // Best effort cleanup only.
        }
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
            // Keep probe artifacts only if cleanup fails.
        }
    }
}

internal sealed record CalibreAvailabilityResult(bool IsOperational, string? EbookConvertPath, string Details);
internal sealed record ProcessProbeResult(
    bool Started,
    bool TimedOut,
    int? ExitCode,
    int TimeoutMs,
    int DurationMs,
    IReadOnlyList<string> StdoutLines,
    IReadOnlyList<string> StderrLines);

