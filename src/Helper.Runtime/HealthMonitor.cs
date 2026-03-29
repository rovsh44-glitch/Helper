using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class HealthMonitor : IHealthMonitor
    {
        private readonly string _logPath;

        public HealthMonitor(string? logPath = null)
        {
            _logPath = string.IsNullOrWhiteSpace(logPath)
                ? HelperWorkspacePathResolver.ResolveLogsPath("global_helper_log.txt")
                : Path.GetFullPath(logPath);
        }

        public async Task<HealthStatus> DiagnoseAsync(CancellationToken ct = default)
        {
            if (!File.Exists(_logPath)) return new HealthStatus(true, new(), 0);

            var lines = await File.ReadAllLinesAsync(_logPath, ct);
            var recentLines = lines.TakeLast(500).ToList();

            var errors = recentLines.Where(l => l.Contains("ERROR") || l.Contains("Exception") || l.Contains("fail:")).ToList();
            double errorRate = (double)errors.Count / Math.Max(1, recentLines.Count);

            var issues = new List<string>();
            if (errorRate > 0.1) issues.Add("High error rate detected in recent logs.");
            
            // Detect recurring patterns
            var frequentErrors = errors.GroupBy(e => e.Split(':').FirstOrDefault() ?? "Unknown")
                                       .Where(g => g.Count() > 5)
                                       .Select(g => $"Recurring issue in {g.Key}: {g.Count()} occurrences");
            
            issues.AddRange(frequentErrors);

            double vram = await GetAvailableVramGbAsync();

            return new HealthStatus(errorRate < 0.15, issues, errorRate, vram);
        }

        private async Task<double> GetAvailableVramGbAsync()
        {
            try 
            {
                bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                
                if (isWindows)
                {
                    using var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController");
                    foreach (var obj in searcher.Get().Cast<ManagementObject>())
                    {
                        var raw = obj["AdapterRAM"];
                        if (raw is null)
                        {
                            continue;
                        }

                        long bytes = Convert.ToInt64(raw);
                        return Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 2);
                    }
                }
                else 
                {
                    // Linux: Attempt to read nvidia-smi
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "nvidia-smi",
                            Arguments = "--query-gpu=memory.free --format=csv,noheader,nounits",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    
                    if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double mb))
                    {
                        return Math.Round(mb / 1024.0, 2);
                    }
                }
                
                Console.Error.WriteLine("[HealthMonitor] VRAM probe returned no value.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HealthMonitor] VRAM probe failed: {ex.Message}");
                return 0;
            }
        }
    }
}

