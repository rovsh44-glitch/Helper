using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Swarm
{
    public abstract class ExpertAgent
    {
        protected readonly AILink _ai;
        protected readonly IVectorStore _store;
        protected readonly string _collection;

        public abstract string Domain { get; }
        public abstract string SystemPrompt { get; }

        protected ExpertAgent(AILink ai, IVectorStore store, string domain)
        {
            _ai = ai;
            _store = store;
            _collection = $"knowledge_{domain.ToLower()}";
        }

        public async Task<string> ConsultAsync(string query, CancellationToken ct = default)
        {
            // 1. Retrieve domain-specific knowledge
            var embedding = await _ai.EmbedAsync(query, ct);
            var knowledge = await _store.SearchAsync(embedding, _collection, limit: 5, ct);
            
            var contextParts = new List<string>();
            foreach (var k in knowledge)
            {
                var title = k.Metadata.GetValueOrDefault("title", "Unknown Source");
                contextParts.Add($"[Source: {title}]\n{k.Content}");
            }
            var context = string.Join("\n\n", contextParts);

            // 2. Formulate expert response
            var finalPrompt = $@"{SystemPrompt}
            
            REFERENCE KNOWLEDGE FROM YOUR LIBRARY:
            {context}
            
            USER QUERY: {query}
            
            INSTRUCTION: Use the provided scientific references to answer the query with high precision. If information is missing, state it clearly.";

            return await _ai.AskAsync(finalPrompt, ct, _ai.GetBestModel("reasoning"));
        }
    }

    public class MathematicianAgent : ExpertAgent
    {
        public MathematicianAgent(AILink ai, IVectorStore store) : base(ai, store, "math") { }
        public override string Domain => "Mathematics";
        public override string SystemPrompt => "You are a Senior Mathematician specializing in Analysis and Linear Algebra. Use rigorous proofs and clear notation.";
    }

    public class PhysicistAgent : ExpertAgent
    {
        public PhysicistAgent(AILink ai, IVectorStore store) : base(ai, store, "physics") { }
        public override string Domain => "Physics";
        public override string SystemPrompt => "You are a Theoretical Physicist. Explain phenomena using first principles and fundamental laws (Landau-Lifshitz level).";
    }

    public class NeurobiologistAgent : ExpertAgent
    {
        public NeurobiologistAgent(AILink ai, IVectorStore store) : base(ai, store, "neuro") { }
        public override string Domain => "Neurobiology";
        public override string SystemPrompt => "You are an Expert Neurobiologist. Focus on synaptic mechanisms, neural circuits, and the biological basis of mind (Kandel level).";
    }

    public class ChemistAgent : ExpertAgent
    {
        public ChemistAgent(AILink ai, IVectorStore store) : base(ai, store, "chemistry") { }
        public override string Domain => "Chemistry";
        public override string SystemPrompt => "You are a Research Chemist. Focus on molecular structure, reaction mechanisms, and thermodynamics.";
    }

    public class BiologistAgent : ExpertAgent
    {
        public BiologistAgent(AILink ai, IVectorStore store) : base(ai, store, "biology") { }
        public override string Domain => "Biology";
        public override string SystemPrompt => "You are an Expert Biologist. Focus on evolutionary biology, genetics, and cellular mechanisms.";
    }

    public class RoboticistAgent : ExpertAgent
    {
        public RoboticistAgent(AILink ai, IVectorStore store) : base(ai, store, "robotics") { }
        public override string Domain => "Robotics";
        public override string SystemPrompt => "You are a Senior Robotics Engineer. Focus on kinematics, control systems, and artificial intelligence integration.";
    }

    public class GeologistAgent : ExpertAgent
    {
        public GeologistAgent(AILink ai, IVectorStore store) : base(ai, store, "geology") { }
        public override string Domain => "Geology";
        public override string SystemPrompt => "You are an Expert Geologist and Mineralogist. Focus on tectonics, mineral structures, and geological time scales.";
    }
}

