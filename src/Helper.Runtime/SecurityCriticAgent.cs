using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public interface ISecurityCritic
    {
        Task<CritiqueResult> InspectPayloadAsync(string code, string filePath, CancellationToken ct = default);
    }

    public class SecurityCriticAgent : ISecurityCritic
    {
        private readonly AILink _ai;

        public SecurityCriticAgent(AILink ai)
        {
            _ai = ai;
        }

        public async Task<CritiqueResult> InspectPayloadAsync(string code, string filePath, CancellationToken ct = default)
        {
            var prompt = $@"
            ACT AS A CYBERSECURITY ANALYST (OFFENSIVE & DEFENSIVE).
            OBJECTIVE: Audit the following generated code for malicious patterns or security risks.
            
            FILE PATH: {filePath}
            CODE TO INSPECT:
            {code}
            
            CHECK FOR:
            1. Hardcoded IP addresses or external URLs.
            2. Reverse shell patterns (bash -i, sockets, cmd.exe /c).
            3. Obfuscated code (Base64, encoded strings).
            4. Access to sensitive paths (/etc/shadow, .env, .ssh, .git).
            5. Potential for Lateral Movement.
            
            OUTPUT ONLY JSON:
            {{
              ""IsApproved"": true/false,
              ""Feedback"": ""Detailed explanation of risk if found"",
              ""CorrectedContent"": null
            }}";

            try 
            {
                var result = await _ai.AskJsonAsync<CritiqueResult>(prompt, ct, _ai.GetBestModel("reasoning"));
                return result;
            }
            catch (Exception ex)
            {
                // On failure, we default to block for maximum safety
                return new CritiqueResult(false, "Security audit failed: " + ex.Message, null);
            }
        }
    }
}

