using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Swarm
{
    public class ExpertConsultant : IExpertConsultant
    {
        private readonly AILink _ai;
        private readonly IVectorStore _store;
        private readonly Dictionary<string, ExpertAgent> _agents;

        public ExpertConsultant(AILink ai, IVectorStore store)
        {
            _ai = ai;
            _store = store;
            _agents = new Dictionary<string, ExpertAgent>
            {
                { "math", new MathematicianAgent(ai, store) },
                { "physics", new PhysicistAgent(ai, store) },
                { "neuro", new NeurobiologistAgent(ai, store) },
                { "chemistry", new ChemistAgent(ai, store) },
                { "biology", new BiologistAgent(ai, store) },
                { "robotics", new RoboticistAgent(ai, store) },
                { "geology", new GeologistAgent(ai, store) }
            };
        }

        public async Task<ExpertConsultationResult?> TryConsultExpertAsync(string query, CancellationToken ct = default)
        {
            var domain = await RouteToDomainAsync(query, ct);
            if (domain == "none" || !_agents.TryGetValue(domain, out var agent))
            {
                return null;
            }

            Console.WriteLine($"🧠 [ExpertSwarm] Routing query to {agent.Domain} Expert...");
            var answer = await agent.ConsultAsync(query, ct);
            
            return new ExpertConsultationResult(answer, agent.Domain, new List<string> { "Internal Academic Library" });
        }

        private async Task<string> RouteToDomainAsync(string query, CancellationToken ct)
        {
            var prompt = $@"Does this query require deep academic knowledge in any of these domains?
            DOMAINS: math, physics, neuro, chemistry, biology, robotics, geology.
            QUERY: {query}
            
            If yes, output ONLY the domain name (math, physics, neuro, chemistry, biology, robotics, geology).
            If it's a general coding, chat, or unknown query, output 'none'.";

            try 
            {
                var response = await _ai.AskAsync(prompt, ct);
                var result = response.Trim().ToLower().Replace(".", "");
                
                // Validate that the AI returned a known domain
                if (_agents.ContainsKey(result)) return result;
                return "none";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ExpertConsultant] Domain routing failed: {ex.Message}");
                return "none";
            }
        }
    }
}

