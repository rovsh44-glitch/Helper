using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Helper.Runtime.Infrastructure
{
    public class ShellExecutor
    {
        public async Task<(bool Success, string Output)> ExecuteSequenceAsync(string workingDir, List<string> commands)
        {
            var fullOutput = new StringBuilder();
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            
            // Создаем временный файл скрипта для сохранения состояния (например, cd)
            string scriptExt = isWindows ? ".bat" : ".sh";
            string scriptName = "helper_exec_" + Guid.NewGuid().ToString("N")[..8] + scriptExt;
            string scriptPath = Path.Combine(workingDir, scriptName);
            
            var scriptContent = new StringBuilder();
            if (isWindows)
            {
                scriptContent.AppendLine("@echo off");
                foreach (var cmd in commands)
                {
                    scriptContent.AppendLine(cmd);
                    scriptContent.AppendLine("if %errorlevel% neq 0 exit /b %errorlevel%");
                }
            }
            else
            {
                scriptContent.AppendLine("#!/bin/bash");
                scriptContent.AppendLine("set -e");
                foreach (var cmd in commands)
                {
                    scriptContent.AppendLine(cmd);
                }
            }

            await File.WriteAllTextAsync(scriptPath, scriptContent.ToString());

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = HostCommandResolver.GetCommandShellExecutable(),
                        Arguments = isWindows ? $"/d /c \"{scriptPath}\"" : scriptPath,
                        WorkingDirectory = workingDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                fullOutput.AppendLine(stdout);
                if (!string.IsNullOrEmpty(stderr)) fullOutput.AppendLine($"ERROR: {stderr}");

                return (process.ExitCode == 0, fullOutput.ToString());
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }
    }
}

