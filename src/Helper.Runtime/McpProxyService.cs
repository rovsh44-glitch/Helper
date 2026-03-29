using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public interface IMcpProxyService
    {
        Task<List<McpTool>> DiscoverExternalToolsAsync(string serverPath, string arguments, CancellationToken ct = default);
        Task<List<McpTool>> DiscoverExternalToolsAsync(string serverPath, IReadOnlyList<string> arguments, CancellationToken ct = default);
        Task<ToolExecutionResult> CallExternalToolAsync(string serverPath, string arguments, string toolName, Dictionary<string, object> args, CancellationToken ct = default);
        Task<ToolExecutionResult> CallExternalToolAsync(string serverPath, IReadOnlyList<string> arguments, string toolName, Dictionary<string, object> args, CancellationToken ct = default);
    }

    public class McpProxyService : IMcpProxyService
    {
        public async Task<List<McpTool>> DiscoverExternalToolsAsync(string serverPath, string arguments, CancellationToken ct = default)
        {
            var request = new { jsonrpc = "2.0", method = "tools/list", @params = new { }, id = "1" };
            var response = await SendMcpRequestAsync(serverPath, arguments, request, ct);
            
            try 
            {
                var doc = JsonDocument.Parse(response);
                var toolsArray = doc.RootElement.GetProperty("result").GetProperty("tools");
                return JsonSerializer.Deserialize<List<McpTool>>(toolsArray.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[McpProxyService] Failed to parse external MCP tools response: {ex.Message}");
                return new List<McpTool>();
            }
        }

        public Task<List<McpTool>> DiscoverExternalToolsAsync(string serverPath, IReadOnlyList<string> arguments, CancellationToken ct = default)
            => DiscoverExternalToolsAsync(serverPath, BuildArguments(arguments), ct);

        public async Task<ToolExecutionResult> CallExternalToolAsync(string serverPath, string arguments, string toolName, Dictionary<string, object> args, CancellationToken ct = default)
        {
            var request = new { jsonrpc = "2.0", method = "tools/call", @params = new { name = toolName, arguments = args }, id = "2" };
            var response = await SendMcpRequestAsync(serverPath, arguments, request, ct);

            try
            {
                var doc = JsonDocument.Parse(response);
                var result = doc.RootElement.GetProperty("result");
                return new ToolExecutionResult(true, result.GetRawText());
            }
            catch (Exception ex)
            {
                return new ToolExecutionResult(false, "", "MCP Error: " + ex.Message);
            }
        }

        public Task<ToolExecutionResult> CallExternalToolAsync(string serverPath, IReadOnlyList<string> arguments, string toolName, Dictionary<string, object> args, CancellationToken ct = default)
            => CallExternalToolAsync(serverPath, BuildArguments(arguments), toolName, args, ct);

        private async Task<string> SendMcpRequestAsync(string serverPath, string arguments, object request, CancellationToken ct)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = serverPath,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return "{}";

            var json = JsonSerializer.Serialize(request);
            await process.StandardInput.WriteLineAsync(json);
            await process.StandardInput.FlushAsync();

            // В реальном MCP сервер может слать уведомления, нам нужно найти именно наш ответ по ID
            // Для упрощения читаем первую строку ответа
            var response = await process.StandardOutput.ReadLineAsync(ct);
            
            if (!process.HasExited) process.Kill();

            return response ?? "{}";
        }

        private static string BuildArguments(IReadOnlyList<string> arguments)
        {
            if (arguments.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(" ", arguments.Select(EscapeArgument));
        }

        private static string EscapeArgument(string argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                return "\"\"";
            }

            var escaped = argument.Replace("\"", "\\\"", StringComparison.Ordinal);
            return escaped.IndexOfAny(new[] { ' ', '\t', '\n', '\r' }) >= 0
                ? $"\"{escaped}\""
                : escaped;
        }
    }
}

