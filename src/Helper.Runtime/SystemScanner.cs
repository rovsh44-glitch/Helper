using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class SystemScanner
    {
        public async Task<SystemCapabilities> ScanAsync(CancellationToken ct = default)
        {
            double vram = 0;
            string gpuModel = "Unknown";
            double ram = 0;
            int cores = Environment.ProcessorCount;

            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                    foreach (var obj in searcher.Get().Cast<ManagementObject>())
                    {
                        gpuModel = obj["Name"]?.ToString() ?? gpuModel;
                        var mem = obj["AdapterRAM"];
                        if (mem != null) vram = Convert.ToDouble(mem) / (1024 * 1024 * 1024);
                    }

                    using var ramSearcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                    foreach (var obj in ramSearcher.Get().Cast<ManagementObject>())
                    {
                        ram = Convert.ToDouble(obj["TotalPhysicalMemory"]) / (1024 * 1024 * 1024);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SystemScanner] Hardware scan fallback: {ex.Message}");
                }
            }

            if (vram < 1) vram = 2.0;

            var tier = vram switch
            {
                >= 48 => SystemTier.Enterprise,
                >= 20 => SystemTier.HighEnd,
                >= 12 => SystemTier.MidRange,
                _ => SystemTier.LowEnd
            };

            bool distReady = tier >= SystemTier.HighEnd && ram >= 32;

            return await Task.FromResult(new SystemCapabilities(tier, vram, ram, cores, gpuModel, distReady));
        }

        public string GenerateRecommendation(SystemCapabilities caps)
        {
            return caps.Tier switch
            {
                SystemTier.Enterprise => "🚀 ENTERPRISE: Run 70B models locally with full context. Swarm primary node ready.",
                SystemTier.HighEnd => "🔥 HIGH-END: Run 32B models (DeepSeek-R1) comfortably. Swarm node ready.",
                SystemTier.MidRange => "⚡ MID-RANGE: Optimal for 14B models (Qwen2.5-Coder). Good for parallel task execution.",
                _ => "🧊 LOW-END: Use 7B or 1.5B models (MLA). Best as a secondary worker node."
            };
        }
    }
}

