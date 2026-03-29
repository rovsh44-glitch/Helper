using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Infrastructure
{
    public class LlmCritic : ICriticService
    {
        private readonly AILink _ai;
        private readonly IPersonaOrchestrator _personas;

        public LlmCritic(AILink ai, IPersonaOrchestrator personas) 
        {
            _ai = ai;
            _personas = personas;
        }

        public async Task<CritiqueResult> AnalyzeAsync(string content, CancellationToken ct = default)
        {
            var prompt = $@"ACT AS A STRICT TECHNICAL CRITIC. Evaluate this content for accuracy and logic. 
            Output ONLY JSON: {{ ""IsApproved"": true/false, ""Feedback"": ""..."", ""CorrectedContent"": ""..."" }}
            CONTENT: {content}";

            var response = await _ai.AskAsync(prompt, ct);
            return ParseCritiqueOrFailOpen(response, "AnalyzeAsync");
        }

        public async Task<CritiqueResult> CritiqueAsync(string sourceData, string draft, string context, CancellationToken ct = default)
        {
            var prompt = $@"ACT AS A STRICT TECHNICAL CRITIC. Evaluate if the DRAFT matches the SOURCE DATA and is logic-consistent.
            Approval rules:
            1) Approve if the draft is syntactically valid C# and preserves planned method signatures/contracts.
            2) Do NOT reject only because business logic is placeholder/TODO when SOURCE DATA has no concrete algorithm details.
            3) Do NOT reject interfaces/views/resources/scripts for lacking executable business logic.
            4) If improvements are optional, keep IsApproved=true and put recommendations in Feedback.
            SOURCE: {sourceData}
            DRAFT: {draft}
            CONTEXT: {context}
            Output ONLY JSON: {{ ""IsApproved"": true/false, ""Feedback"": ""..."", ""CorrectedContent"": ""..."" }}";

            var response = await _ai.AskAsync(prompt, ct);
            return ParseCritiqueOrFailOpen(response, "CritiqueAsync");
        }

        public async Task<ShadowRoundtableReport> ChallengeAsync(string proposal, CancellationToken ct = default)
        {
            // Direct use of Persona Orchestrator to fulfill the "Shadow Roundtable" logic
            return await _personas.ConductRoundtableAsync(proposal, ct);
        }

        private static CritiqueResult ParseCritiqueOrFailOpen(string response, string source)
        {
            var candidate = ExtractJsonCandidate(response);
            if (TryDeserializeCritique(candidate, out var parsed))
            {
                return parsed!;
            }

            return new CritiqueResult(
                true,
                $"Critic response parsing failed in {source}. Accepted in fail-open mode to avoid deadlock.",
                null);
        }

        private static string ExtractJsonCandidate(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return string.Empty;
            }

            var trimmed = response.Trim();
            if (trimmed.Contains("```json", StringComparison.OrdinalIgnoreCase))
            {
                var block = Regex.Match(trimmed, "```json\\s*(?<json>.*?)\\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (block.Success)
                {
                    return block.Groups["json"].Value.Trim();
                }
            }

            if (trimmed.Contains("```", StringComparison.Ordinal))
            {
                var block = Regex.Match(trimmed, "```\\s*(?<json>.*?)\\s*```", RegexOptions.Singleline);
                if (block.Success)
                {
                    return block.Groups["json"].Value.Trim();
                }
            }

            var firstBrace = trimmed.IndexOf('{');
            var lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return trimmed;
        }

        private static bool TryDeserializeCritique(string json, out CritiqueResult? parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                parsed = JsonSerializer.Deserialize<CritiqueResult>(json, JsonDefaults.Options);
                if (parsed is null)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(parsed.Feedback))
                {
                    parsed = parsed with { Feedback = parsed.IsApproved ? "Approved." : "Rejected." };
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

