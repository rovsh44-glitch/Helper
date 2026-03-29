using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Helper.Runtime.Infrastructure
{
    public class KnowledgeRequestService
    {
        private readonly string _path;
        private List<string> _missingTopics = new();

        public KnowledgeRequestService()
        {
            _path = HelperWorkspacePathResolver.ResolveDataFilePath("knowledge_requests.json");
        }

        public async Task RequestTopicAsync(string topic)
        {
            if (!_missingTopics.Contains(topic))
            {
                _missingTopics.Add(topic);
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(_missingTopics, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        public string GetStatusMessage() => $"I have {_missingTopics.Count} pending knowledge requests. Please provide documentation in the configured HELPER_LIBRARY_ROOT.";
    }
}

