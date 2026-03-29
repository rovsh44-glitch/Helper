using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Swarm.Agents;
using Helper.Runtime.Swarm.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Helper.Runtime.Swarm
{
    public class SwarmOrchestrator
    {
        private readonly SwarmArchitect _architect;
        private readonly SwarmContractor _contractor;
        private readonly SwarmSpecialist _specialist;
        private readonly AILink _ai;
        private readonly ICodeSanitizer _sanitizer;
        private readonly IBuildExecutor _executor;
        private readonly ISwarmNodeManager _nodeManager;
        private readonly IMcpProxyService _mcpProxy;
        private readonly ISecurityCritic _security;
        private readonly IBlueprintJsonSchemaValidator _schemaValidator;
        private readonly IBlueprintContractValidator _blueprintContractValidator;

        public SwarmOrchestrator(
            AILink ai,
            ICodeSanitizer sanitizer,
            IBuildExecutor executor,
            ISwarmNodeManager nodeManager,
            IMcpProxyService mcpProxy,
            ISecurityCritic security,
            IBlueprintJsonSchemaValidator schemaValidator,
            IBlueprintContractValidator blueprintContractValidator)
        {
            _ai = ai;
            _sanitizer = sanitizer;
            _executor = executor;
            _nodeManager = nodeManager;
            _mcpProxy = mcpProxy;
            _security = security;
            _schemaValidator = schemaValidator;
            _blueprintContractValidator = blueprintContractValidator;
            _architect = new SwarmArchitect(ai, _schemaValidator, _blueprintContractValidator);
            _contractor = new SwarmContractor(ai);
            _specialist = new SwarmSpecialist(ai);
        }

        public async Task<string> SwarmForgeAsync(string userRequest, string outputDir, CancellationToken ct = default)
        {
            Console.WriteLine("🏗️ [Architect] Designing Blueprint...");
            var blueprint = await _architect.DesignSystemAsync(userRequest, ct);
            
            var projectDir = Path.Combine(outputDir, blueprint.ProjectName + "_" + Guid.NewGuid().ToString("N")[..4]);
            Directory.CreateDirectory(projectDir);

            Console.WriteLine("📜 [Contractor] Establishing Interfaces...");
            var artifacts = await _contractor.EstablishContractsAsync(blueprint, ct);
            
            // SECURITY AUDIT: Interfaces
            foreach (var art in artifacts)
            {
                var audit = await _security.InspectPayloadAsync(art.Content, art.Path, ct);
                if (!audit.IsApproved) throw new UnauthorizedAccessException($"[Security] ⛔ Artifact {art.Path} REJECTED: {audit.Feedback}");
                SaveArtifact(projectDir, art);
            }

            Console.WriteLine("🐝 [Swarm] Distributed Task Allocation...");
            var pendingFiles = blueprint.Files.Where(f => f.Role != FileRole.Model && f.Role != FileRole.Infrastructure).ToList();
            
            var results = new ConcurrentQueue<SwarmArtifact>();
            var tasks = pendingFiles.Select(async file => 
            {
                var bestNode = await _nodeManager.SelectBestNodeAsync(TaskComplexity.Standard, ct);
                
                if (bestNode != null && !bestNode.IsLocal)
                {
                    var remoteResult = await DelegateTaskToRemoteNodeAsync(bestNode, file, blueprint, artifacts, ct);
                    if (remoteResult != null)
                    {
                        results.Enqueue(remoteResult);
                        return;
                    }
                }

                var art = await _specialist.ImplementAsync(file, blueprint, artifacts, ct);
                results.Enqueue(art);
            });

            await Task.WhenAll(tasks);

            // SECURITY AUDIT: Implementation
            foreach (var art in results)
            {
                var audit = await _security.InspectPayloadAsync(art.Content, art.Path, ct);
                if (!audit.IsApproved)
                {
                    Console.WriteLine($"   ⚠️ [Security] REJECTED {art.Path}. Attempting sanitized re-generation...");
                    // In a full implementation, we would retry or use the 'CorrectedContent' from the audit
                    throw new UnauthorizedAccessException($"[Security] ⛔ {art.Path} failed security audit: {audit.Feedback}");
                }
                SaveArtifact(projectDir, art, artifacts);
            }

            Console.WriteLine("🔌 [Integrator] Finalizing Project...");
            await CreateCsprojAsync(projectDir, blueprint);
            await CreateAppXamlAsync(projectDir, blueprint);

            Console.WriteLine("⚖️ [QA] Verifying Build...");
            var buildErrors = await _executor.ExecuteBuildAsync(projectDir, ct);
            if (buildErrors.Count == 0) return projectDir;

            Console.WriteLine("🚑 [Healer] Build failed. Attempting Auto-Healing...");
            var validator = new MultiLanguageValidator(_executor);
            var healer = new AutoHealer(_ai, validator);
            await healer.HealAsync(projectDir, buildErrors, msg => Console.WriteLine($"   -> {msg}"), ct);
            
            return projectDir;
        }

        private async Task<SwarmArtifact?> DelegateTaskToRemoteNodeAsync(SwarmNode node, SwarmFileDefinition file, SwarmBlueprint blueprint, List<SwarmArtifact> artifacts, CancellationToken ct)
        {
            try
            {
                var args = new Dictionary<string, object>
                {
                    { "task", file },
                    { "blueprint", blueprint },
                    { "context", artifacts }
                };

                var result = await _mcpProxy.CallExternalToolAsync(node.BaseUrl, "--mcp", "helper_swarm_implement", args, ct);
                if (result.Success)
                {
                    return JsonSerializer.Deserialize<SwarmArtifact>(result.Output, JsonDefaults.Options);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ [Swarm] Delegation failed for {file.Path}: {ex.Message}");
            }
            return null;
        }

        private void SaveArtifact(string baseDir, SwarmArtifact artifact, List<SwarmArtifact>? sharedContext = null)
        {
            var fullPath = Path.Combine(baseDir, artifact.Path);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, artifact.Content);
        }

        private async Task CreateCsprojAsync(string projectDir, SwarmBlueprint blueprint)
        {
            var name = blueprint.ProjectName;
            var content = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""CommunityToolkit.Mvvm"" Version=""8.2.2"" />
  </ItemGroup>
</Project>";
            await File.WriteAllTextAsync(Path.Combine(projectDir, $"{name}.csproj"), content);
        }

        private async Task CreateAppXamlAsync(string projectDir, SwarmBlueprint blueprint)
        {
            var ns = blueprint.RootNamespace;
            var xaml = $@"<Application x:Class=""{ns}.App"" xmlns=""http://schemas.microsoft.com/winfx/2003/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2003/xaml"" StartupUri=""MainWindow.xaml""></Application>";
            var cs = $@"namespace {ns} {{ public partial class App : System.Windows.Application {{ }} }}";
            await File.WriteAllTextAsync(Path.Combine(projectDir, "App.xaml"), xaml);
            await File.WriteAllTextAsync(Path.Combine(projectDir, "App.xaml.cs"), cs);
        }
    }
}

