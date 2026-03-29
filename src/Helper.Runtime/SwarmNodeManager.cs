using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class SwarmNodeManager : ISwarmNodeManager
    {
        private readonly ConcurrentDictionary<string, SwarmNode> _nodes = new();
        private readonly SystemScanner _scanner;
        private readonly HttpClient _httpClient;
        private readonly string _registryPath;

        public SwarmNodeManager(SystemScanner scanner)
        {
            _scanner = scanner;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _registryPath = HelperWorkspacePathResolver.ResolveDataFilePath("swarm_registry.json");
        }

        public async Task RegisterNodeAsync(SwarmNode node, CancellationToken ct = default)
        {
            _nodes[node.Id] = node with { LastSeen = DateTime.UtcNow };
            Console.WriteLine($"🐝 [Swarm] Node Active: {node.Id} ({node.Tier}) at {node.BaseUrl}");
            
            // Persist to registry for discovery by others
            await UpdateRegistryAsync(ct);
        }

        public async Task<List<SwarmNode>> GetActiveNodesAsync(CancellationToken ct = default)
        {
            // 1. Cleanup stale
            var stale = _nodes.Values.Where(n => (DateTime.UtcNow - n.LastSeen).TotalSeconds > 120).Select(n => n.Id).ToList();
            foreach (var id in stale) _nodes.TryRemove(id, out _);

            // 2. Ensure local node
            if (!_nodes.Values.Any(n => n.IsLocal))
            {
                var caps = await _scanner.ScanAsync(ct);
                var localId = Environment.MachineName + "_node";
                await RegisterNodeAsync(new SwarmNode(localId, "http://localhost:5000", caps.Tier, caps.VramGb, DateTime.UtcNow, true), ct);
            }

            // 3. Try discover from registry
            await DiscoverFromRegistryAsync(ct);

            return _nodes.Values.OrderByDescending(n => n.Tier).ToList();
        }

        public async Task<SwarmNode?> SelectBestNodeAsync(TaskComplexity complexity, CancellationToken ct = default)
        {
            var nodes = await GetActiveNodesAsync(ct);
            return complexity switch
            {
                TaskComplexity.Reasoning => nodes.OrderByDescending(n => n.Tier).FirstOrDefault(),
                TaskComplexity.Visual => nodes.OrderByDescending(n => n.Tier).FirstOrDefault(),
                _ => nodes.OrderBy(n => n.AvailableVramGb).FirstOrDefault()
            };
        }

        public async Task BroadcastPulseAsync(CancellationToken ct = default)
        {
            var nodes = _nodes.Values.ToList();
            foreach (var node in nodes)
            {
                if (node.IsLocal) 
                {
                    _nodes[node.Id] = node with { LastSeen = DateTime.UtcNow };
                    continue;
                }

                try
                {
                    var response = await _httpClient.GetAsync($"{node.BaseUrl}/api/health", ct);
                    if (response.IsSuccessStatusCode)
                    {
                        _nodes[node.Id] = node with { LastSeen = DateTime.UtcNow };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Swarm] Pulse failed for {node.Id}: {ex.Message}");
                    _nodes.TryRemove(node.Id, out _);
                }
            }
            await UpdateRegistryAsync(ct);
        }

        private async Task UpdateRegistryAsync(CancellationToken ct)
        {
            try
            {
                var localNode = _nodes.Values.FirstOrDefault(n => n.IsLocal);
                if (localNode == null) return;

                List<SwarmNode> registryNodes = new();
                if (File.Exists(_registryPath))
                {
                    var json = await File.ReadAllTextAsync(_registryPath, ct);
                    registryNodes = JsonSerializer.Deserialize<List<SwarmNode>>(json) ?? new();
                }

                // Remove old entry for this machine and add fresh one
                registryNodes.RemoveAll(n => n.Id == localNode.Id);
                registryNodes.Add(localNode);

                await File.WriteAllTextAsync(_registryPath, JsonSerializer.Serialize(registryNodes), ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Swarm] Failed to update registry: {ex.Message}");
            }
        }

        private async Task DiscoverFromRegistryAsync(CancellationToken ct)
        {
            if (!File.Exists(_registryPath)) return;
            try
            {
                var json = await File.ReadAllTextAsync(_registryPath, ct);
                var registryNodes = JsonSerializer.Deserialize<List<SwarmNode>>(json) ?? new();
                foreach (var node in registryNodes)
                {
                    if (!_nodes.ContainsKey(node.Id))
                    {
                        _nodes[node.Id] = node with { IsLocal = (node.Id == Environment.MachineName + "_node") };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Swarm] Failed to read registry: {ex.Message}");
            }
        }
    }
}

