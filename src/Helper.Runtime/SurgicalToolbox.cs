using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class SurgicalToolbox : ISurgicalToolbox
    {
        public async Task<List<GrepResult>> GrepAsync(string directory, string pattern, string include = "*.cs", CancellationToken ct = default)
        {
            var results = new List<GrepResult>();
            if (!Directory.Exists(directory)) return results;

            var files = Directory.GetFiles(directory, include, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var lines = await File.ReadAllLinesAsync(file, ct);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (Regex.IsMatch(lines[i], pattern, RegexOptions.IgnoreCase))
                    {
                        results.Add(new GrepResult(file, i + 1, lines[i].Trim()));
                    }
                }
                if (results.Count > 100) break; // Safety limit
            }
            return results;
        }

        public async Task<bool> ReplaceAsync(ReplaceRequest request, CancellationToken ct = default)
        {
            if (!File.Exists(request.FilePath)) return false;

            var content = await File.ReadAllTextAsync(request.FilePath, ct);
            if (!content.Contains(request.OldText)) return false;

            // In a real surgical tool, we would verify context lines here.
            // For this implementation, we do a safe literal replacement.
            var newContent = content.Replace(request.OldText, request.NewString);
            await File.WriteAllTextAsync(request.FilePath, newContent, ct);
            return true;
        }

        public Task<string> GetDirectoryTreeAsync(string path, int depth = 3, CancellationToken ct = default)
        {
            if (!Directory.Exists(path)) return Task.FromResult("(New Project / Directory does not exist yet)");
            var sb = new StringBuilder();
            BuildTree(new DirectoryInfo(path), sb, "", 0, depth);
            return Task.FromResult(sb.ToString());
        }

        private void BuildTree(DirectoryInfo dir, StringBuilder sb, string indent, int currentDepth, int maxDepth)
        {
            if (currentDepth > maxDepth) return;
            sb.AppendLine($"{indent}├── {dir.Name}/");
            
            foreach (var d in dir.GetDirectories().Where(x => !x.Name.StartsWith(".") && x.Name != "bin" && x.Name != "obj").Take(10))
            {
                BuildTree(d, sb, indent + "│   ", currentDepth + 1, maxDepth);
            }

            foreach (var f in dir.GetFiles().Take(10))
            {
                sb.AppendLine($"{indent}│   └── {f.Name}");
            }
        }
    }
}

