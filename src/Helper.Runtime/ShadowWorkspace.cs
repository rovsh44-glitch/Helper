using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class ShadowWorkspace
    {
        private readonly string _shadowRoot;
        private readonly string _sourceRoot;
        private readonly IDotnetService _dotnet;

        public ShadowWorkspace(string sourceRoot, IDotnetService dotnet)
        {
            _sourceRoot = sourceRoot;
            _dotnet = dotnet;
            _shadowRoot = Path.Combine(Path.GetTempPath(), "helper_shadow", Guid.NewGuid().ToString("N"));
        }

        public async Task<bool> CloneAsync(CancellationToken ct = default)
        {
            try
            {
                if (Directory.Exists(_shadowRoot)) Directory.Delete(_shadowRoot, true);
                Directory.CreateDirectory(_shadowRoot);
                await CopyDirectoryAsync(_sourceRoot, _shadowRoot, ct);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Shadow] Clone failed: {ex.Message}");
                return false;
            }
        }

        public async Task ApplyPatchAsync(string relativePath, string newContent, CancellationToken ct = default)
        {
            var targetPath = Path.Combine(_shadowRoot, relativePath);
            var dir = Path.GetDirectoryName(targetPath);
            if (dir != null) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(targetPath, newContent, ct);
        }

        public async Task<List<BuildError>> VerifyShadowBuildAsync(CancellationToken ct = default)
        {
            return await _dotnet.BuildAsync(_shadowRoot, ct);
        }

        public string GetSourcePath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(_sourceRoot, relativePath));
        }

        private async Task CopyDirectoryAsync(string source, string target, CancellationToken ct)
        {
            foreach (string dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                if (dirPath.Contains(@"\bin") || dirPath.Contains(@"\obj") || dirPath.Contains(".git") || dirPath.Contains("node_modules")) continue;
                Directory.CreateDirectory(dirPath.Replace(source, target));
            }

            foreach (string filePath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
            {
                if (filePath.Contains(@"\bin") || filePath.Contains(@"\obj") || filePath.Contains(".git") || filePath.Contains("node_modules")) continue;
                File.Copy(filePath, filePath.Replace(source, target), true);
            }
            await Task.CompletedTask;
        }

        public string GetShadowPath() => _shadowRoot;
    }
}

