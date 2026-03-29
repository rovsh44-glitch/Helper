using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class PersonalityManager : IPersonalityManager
    {
        private readonly AILink _ai;
        private readonly Dictionary<string, PersonalityProfile> _profiles = new();

        public PersonalityManager(AILink ai) => _ai = ai;

        public Task<PersonalityProfile> GetProfileAsync(string id, CancellationToken ct = default)
        {
            if (_profiles.TryGetValue(id, out var profile)) return Task.FromResult(profile);
            var defaultProfile = new PersonalityProfile(id, "Helper", "You are an autonomous self-evolving AI.");
            _profiles[id] = defaultProfile;
            return Task.FromResult(defaultProfile);
        }

        public async Task<DriftAudit> AuditResponseAsync(string response, string personaId, CancellationToken ct = default)
        {
            var profile = await GetProfileAsync(personaId, ct);
            if (profile.BaselineEmbedding == null) return new DriftAudit(1.0, false, personaId, DateTime.UtcNow);

            var currentEmbedding = await _ai.EmbedAsync(response, ct);
            var similarity = CosineSimilarity(profile.BaselineEmbedding, currentEmbedding);
            return new DriftAudit(similarity, similarity < 0.8, personaId, DateTime.UtcNow);
        }

        public async Task SetBaselineAsync(string personaId, string referenceResponse, CancellationToken ct = default)
        {
            var embedding = await _ai.EmbedAsync(referenceResponse, ct);
            var profile = await GetProfileAsync(personaId, ct);
            _profiles[personaId] = profile with { BaselineEmbedding = embedding };
        }

        private double CosineSimilarity(float[] v1, float[] v2)
        {
            double dot = 0.0, mag1 = 0.0, mag2 = 0.0;
            for (int i = 0; i < v1.Length; i++)
            {
                dot += v1[i] * v2[i];
                mag1 += v1[i] * v1[i];
                mag2 += v2[i] * v2[i];
            }
            return dot / (Math.Sqrt(mag1) * Math.Sqrt(mag2));
        }
    }
}

