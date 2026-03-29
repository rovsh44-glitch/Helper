using System;
using System.IO;
using System.Threading.Tasks;

namespace Helper.Runtime.Infrastructure
{
    public class WindowsSandboxProvider
    {
        public async Task<string> GenerateConfigAsync(string projectPath)
        {
            var sandboxPath = Path.Combine(projectPath, "helper_sandbox.wsb");
            var wsbConfig = $@"
<Configuration>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>{projectPath}</HostFolder>
      <SandboxFolder>C:\Users\WDAGUtilityAccount\Desktop\Project</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>cmd /c echo 'Helper Sandbox Ready'</Command>
  </LogonCommand>
</Configuration>";

            await File.WriteAllTextAsync(sandboxPath, wsbConfig);
            return sandboxPath;
        }

        public void Launch(string configPath)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "windows-sandbox",
                Arguments = $"\"{configPath}\"",
                UseShellExecute = true
            });
        }
    }
}

