using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class PlatformGuard : IPlatformGuard
    {
        public PlatformCapabilities DetectPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return new PlatformCapabilities(
                    Helper.Runtime.Core.OSPlatform.Windows, '\\', "WPF/WinUI", HostCommandResolver.GetPreferredShellName(), 
                    new List<string> { "sudo", "apt-get", "launchd" });
            }
            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                return new PlatformCapabilities(
                    Helper.Runtime.Core.OSPlatform.MacOS, '/', "Avalonia/Uno", "zsh", 
                    new List<string> { "Registry", "WPF", "IIS", "ActiveDirectory" });
            }
            return new PlatformCapabilities(
                Helper.Runtime.Core.OSPlatform.Linux, '/', "Avalonia", "bash", 
                new List<string> { "WPF", "WinForms", "COM" });
        }

        public void ValidateTechStack(string tech, Helper.Runtime.Core.OSPlatform targetOS)
        {
            var caps = DetectPlatform(); // Simple for now, ideally matches targetOS
            foreach (var forbidden in caps.ForbiddenTech)
            {
                if (tech.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException($"[PlatformGuard] 🛡️ Violation: '{forbidden}' is not compatible with {targetOS}.");
                }
            }
        }
    }
}

