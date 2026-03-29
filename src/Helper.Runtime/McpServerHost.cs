using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    /// <summary>
    /// Implements the Model Context Protocol (MCP) Server host over Standard I/O.
    /// Allows external tools like Cursor or VS Code to use Helper's intelligence.
    /// </summary>
    public class McpServerHost
    {
        private readonly IToolService _toolService;
        private readonly IHelperOrchestrator _orchestrator;
        private readonly IExtensionRegistry? _extensionRegistry;
        private readonly JsonSerializerOptions _jsonOptions;

        public McpServerHost(IToolService toolService, IHelperOrchestrator orchestrator, IExtensionRegistry? extensionRegistry = null)
        {
            _toolService = toolService;
            _orchestrator = orchestrator;
            _extensionRegistry = extensionRegistry;
            _jsonOptions = new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        public async Task RunAsync(CancellationToken ct)
        {
            Console.Error.WriteLine("[MCP] Server started. Listening on Standard Input...");

            using var reader = new StreamReader(Console.OpenStandardInput());
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var request = JsonSerializer.Deserialize<McpRequest>(line, _jsonOptions);
                    if (request == null) continue;

                    var response = await HandleRequestAsync(request, ct);
                    var jsonResponse = JsonSerializer.Serialize(response, _jsonOptions);
                    
                    // MCP requires responses on a single line to StdOut
                    Console.WriteLine(jsonResponse);
                    await Console.Out.FlushAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MCP] Error: {ex.Message}");
                }
            }
        }

        private async Task<McpResponse> HandleRequestAsync(McpRequest request, CancellationToken ct)
        {
            return request.Method switch
            {
                "initialize" => new McpResponse(request.Jsonrpc, request.Id, new {
                    protocolVersion = "2024-11-05",
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "Helper-AGI-Server", version = "2026.4.1" }
                }, null),

                "tools/list" => new McpResponse(request.Jsonrpc, request.Id, new {
                    tools = await GetMcpToolsAsync(ct)
                }, null),

                "tools/call" => await CallToolAsync(request, ct),

                _ => new McpResponse(request.Jsonrpc, request.Id, null, new { code = -32601, message = "Method not found" })
            };
        }

        private async Task<List<object>> GetMcpToolsAsync(CancellationToken ct)
        {
            var internalTools = await _toolService.GetAvailableToolsAsync(ct);
            var mcpTools = new List<object>();

            foreach (var tool in internalTools)
            {
                mcpTools.Add(new {
                    name = tool.Name,
                    description = tool.Description,
                    inputSchema = new {
                        type = "object",
                        properties = tool.Parameters.ToDictionary(p => p.Key, p => new { type = "string", description = p.Value })
                    }
                });
            }

            foreach (var internalAction in GetInternalActionDefinitions())
            {
                mcpTools.Add(internalAction);
            }

            return mcpTools;
        }

        private async Task<McpResponse> CallToolAsync(McpRequest request, CancellationToken ct)
        {
            if (request.Params == null || !request.Params.TryGetValue("name", out var nameObj))
                return new McpResponse(request.Jsonrpc, request.Id, null, new { message = "Tool name missing" });

            string toolName = nameObj.ToString() ?? "";
            var args = new Dictionary<string, object>();
            if (request.Params.TryGetValue("arguments", out var argsObj) && argsObj is JsonElement argsElem)
            {
                args = JsonSerializer.Deserialize<Dictionary<string, object>>(argsElem.GetRawText()) ?? new();
            }

            try
            {
                if (toolName == "helper_research")
                {
                    var topic = args["topic"].ToString()!;
                    var result = await _orchestrator.ConductResearchAsync(topic, 1, null, ct);
                    return new McpResponse(request.Jsonrpc, request.Id, new { content = new[] { new { type = "text", text = result.Summary } } }, null);
                }

                if (toolName == "helper_generate_project")
                {
                    var prompt = args["prompt"].ToString()!;
                    var outputRoot = HelperWorkspacePathResolver.ResolveProjectsPath("mcp_generated");
                    var genRequest = new GenerationRequest(prompt, outputRoot, new());
                    var result = await _orchestrator.GenerateProjectAsync(genRequest, true, null, ct);
                    return new McpResponse(request.Jsonrpc, request.Id, new { content = new[] { new { type = "text", text = $"Project generated at: {result.ProjectPath}. Success: {result.Success}" } } }, null);
                }

                // Default to tool service
                var toolResult = await _toolService.ExecuteToolAsync(toolName, args, ct);
                return new McpResponse(request.Jsonrpc, request.Id, new { 
                    content = new[] { new { type = "text", text = toolResult.Output } },
                    isError = !toolResult.Success
                }, null);
            }
            catch (Exception ex)
            {
                return new McpResponse(request.Jsonrpc, request.Id, null, new { message = ex.Message });
            }
        }

        private IEnumerable<object> GetInternalActionDefinitions()
        {
            var declaredActions = _extensionRegistry?
                .GetByCategory(ExtensionCategory.Internal)
                .SelectMany(x => x.DeclaredTools)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (declaredActions is null || declaredActions.Length == 0)
            {
                declaredActions = new[] { "helper_research", "helper_generate_project" };
            }

            foreach (var action in declaredActions)
            {
                if (string.Equals(action, "helper_research", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new
                    {
                        name = "helper_research",
                        description = "Perform deep research on a topic using web search and knowledge base.",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new
                            {
                                topic = new { type = "string", description = "The research topic" },
                                depth = new { type = "integer", description = "Research depth (1-3)" }
                            },
                            required = new[] { "topic" }
                        }
                    };
                    continue;
                }

                if (string.Equals(action, "helper_generate_project", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new
                    {
                        name = "helper_generate_project",
                        description = "Generate a full .NET/WPF project from a prompt.",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new
                            {
                                prompt = new { type = "string", description = "Detailed project requirements" }
                            },
                            required = new[] { "prompt" }
                        }
                    };
                }
            }
        }
    }
}

