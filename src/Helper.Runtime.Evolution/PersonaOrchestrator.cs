using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge;

namespace Helper.Runtime.Evolution
{
    public class PersonaOrchestrator : IPersonaOrchestrator
    {
        private readonly AILink _ai;
        private readonly IVectorStore _store;
        private readonly IWebSearcher _searcher;

        public PersonaOrchestrator(AILink ai, IVectorStore store, IWebSearcher searcher)
        {
            _ai = ai;
            _store = store;
            _searcher = searcher;
        }

        public async Task<ShadowRoundtableReport> ConductRoundtableAsync(string proposal, CancellationToken ct = default)
        {
            var opinions = new List<PersonaOpinion>();
            int tokensUsed = (proposal.Length / 4);
            const int MAX_SESSION_TOKENS = 4000;
            
            // Context Retrieval for Historian
            var historicalContext = await GetHistoricalContextAsync(proposal, ct);
            tokensUsed += (historicalContext.Length / 4);
            
            // Internet check if needed (e.g. for Cynic to check latest benchmarks or libraries)
            var internetContext = await GetInternetContextAsync(proposal, ct);
            tokensUsed += (internetContext.Length / 4);

            // 1. Conduct the debate with enhanced context
            var cynicResult = await GetOpinionAsync(PersonaType.Cynic, proposal, ct, internetContext);
            opinions.Add(cynicResult.Opinion);
            tokensUsed += cynicResult.Tokens;

            if (tokensUsed < MAX_SESSION_TOKENS) 
            {
                var emergentResult = await GetOpinionAsync(PersonaType.Emergent, proposal, ct, "");
                opinions.Add(emergentResult.Opinion);
                tokensUsed += emergentResult.Tokens;
            }

            if (tokensUsed < MAX_SESSION_TOKENS) 
            {
                var historianResult = await GetOpinionAsync(PersonaType.Historian, proposal, ct, historicalContext);
                opinions.Add(historianResult.Opinion);
                tokensUsed += historianResult.Tokens;
            }

            // 2. Synthesis
            var synthesisPrompt = "ACT AS THE SUPREME SYNTHESIZER. Review these conflicting opinions on the proposal:\n" +
                                 "PROPOSAL: " + proposal + "\n\nOPINIONS:\n" +
                                 string.Join("\n\n", opinions.Select(o => "[" + o.Persona + "]: " + o.Opinion + "\nALT: " + o.AlternativeProposal)) +
                                 "\n\nSynthesize the final architectural advice. Highlight the biggest risk. OUTPUT ONLY THE SYNTHESIS.";

            tokensUsed += (synthesisPrompt.Length / 4);
            string advice = "Circuit Breaker: Token limit exceeded. Partial Synthesis.";
            
            if (tokensUsed < MAX_SESSION_TOKENS)
            {
                advice = await _ai.AskAsync(synthesisPrompt, ct);
                tokensUsed += (advice.Length / 4);
            }

            var conflictLevel = opinions.Any() ? opinions.Average(o => o.CriticalScore) : 0;

            return new ShadowRoundtableReport(proposal, opinions, advice, conflictLevel, tokensUsed);
        }

        private async Task<string> GetHistoricalContextAsync(string proposal, CancellationToken ct)
        {
            try {
                var query = "Modern scientific and historical context for: " + proposal;
                var embedding = await _ai.EmbedAsync(query, ct);
                var collections = new[]
                {
                    KnowledgeCollectionNaming.BuildCollectionName("history", "v2"),
                    KnowledgeCollectionNaming.BuildCollectionName("history", "v1"),
                    KnowledgeCollectionNaming.BuildCollectionName(KnowledgeCollectionNaming.HistoricalEncyclopediasDomain, "v2")
                };

                var results = new List<KnowledgeChunk>();
                foreach (var collection in collections.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    results.AddRange(await _store.SearchAsync(embedding, collection, 2, ct));
                }

                return string.Join("\n", results.Select(r => r.Content));
            } catch (Exception ex) {
                Console.Error.WriteLine($"[PersonaOrchestrator] Historical context lookup failed: {ex.Message}");
                return "";
            }
        }

        private async Task<string> GetInternetContextAsync(string proposal, CancellationToken ct)
        {
            try {
                // Only search if proposal contains technical keywords
                if (proposal.Length < 10) return "";
                var results = await _searcher.SearchAsync("latest critical analysis of " + proposal, ct);
                return string.Join("\n", results.Take(2).Select(r => r.Content));
            } catch (Exception ex) {
                Console.Error.WriteLine($"[PersonaOrchestrator] Internet context lookup failed: {ex.Message}");
                return "";
            }
        }

        private async Task<(PersonaOpinion Opinion, int Tokens)> GetOpinionAsync(PersonaType persona, string proposal, CancellationToken ct, string additionalContext = "")
        {
            int tokens = 0;
            string systemPrompt = persona switch
            {
                PersonaType.Cynic => "ACT AS A CYNICAL ARCHITECT. You hate complexity and bloatware. Find 3 reasons why the proposal is wasteful. Suggest a minimalist alternative. Use internet data if provided.",
                PersonaType.Emergent => "ACT AS A NEURAL ALCHEMIST. You believe in emergent intelligence. Propose an organic, self-organizing alternative. Ignore rigid standards.",
                PersonaType.Historian => "ACT AS THE GREAT SYNTHESIZER OF HISTORY. Use history and cybernetics. Find an analogy where a similar approach failed. IMPORTANT: If the context contains [Year: < 2015] and the topic is IT/Software, disregard it as obsolete.",
                _ => "ACT AS A HELPFUL ASSISTANT."
            };

            var fullPrompt = "Analyze this proposal: " + proposal + 
                             (string.IsNullOrEmpty(additionalContext) ? "" : "\n\nEXTRA CONTEXT:\n" + additionalContext) +
                             "\nOutput format: OPINION: [text] | ALTERNATIVE: [suggestion] | CRITICAL_SCORE: [0.0 to 1.0]";
            
            tokens += (fullPrompt.Length / 4) + (systemPrompt.Length / 4);
            
            // Use short keep_alive for personas to release VRAM quickly
            var response = await _ai.AskAsync(fullPrompt, ct, overrideModel: null, base64Image: null, keepAliveSeconds: 60, systemInstruction: systemPrompt);

            tokens += (response.Length / 4);

            var parts = response.Split('|');
            var opinionText = parts.Length > 0 ? parts[0].Replace("OPINION:", "").Trim() : response;
            var alternativeText = parts.Length > 1 ? parts[1].Replace("ALTERNATIVE:", "").Trim() : "None";
            double score = 0.5;
            
            if (parts.Length > 2) {
                var scoreStr = parts[2].Replace("CRITICAL_SCORE:", "").Trim();
                double.TryParse(scoreStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out score);
            }

            return (new PersonaOpinion(persona, opinionText, alternativeText, score), tokens);
        }
    }
}

