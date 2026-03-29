using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Swarm.Core;

namespace Helper.Runtime.Swarm.Agents
{
    public class ArbanAgent
    {
        private readonly AILink _ai;
        private readonly ICodeSanitizer _sanitizer;
        private readonly IReflectionService _reflection;
        private readonly IMethodSignatureValidator _signatureValidator;
        private readonly RoslynLiveCorrector _validator = new();
        private readonly TimeSpan _singleCallTimeout;

        public ArbanAgent(
            AILink ai,
            ICodeSanitizer sanitizer,
            IReflectionService reflection,
            IMethodSignatureValidator signatureValidator)
        { 
            _ai = ai; 
            _sanitizer = sanitizer;
            _reflection = reflection;
            _signatureValidator = signatureValidator;
            _singleCallTimeout = ReadTimeoutFromEnvironment();
        }

        public async Task<ArbanResult> ImplementMethodAsync(ArbanMethodTask task, string className, CancellationToken ct = default)
        {
            var signatureValidation = _signatureValidator.Validate(task.Signature);
            if (!signatureValidation.IsValid || string.IsNullOrWhiteSpace(signatureValidation.NormalizedSignature))
            {
                return new ArbanResult(task.Name, BuildFallbackBody(task.Name), false, 0, signatureValidation.Errors.ToList());
            }

            // --- COGNITIVE LAYER: SEARCH FOR LESSONS ---
            var lessons = await _reflection.SearchLessonsAsync($"{className} {task.Name} {task.Purpose}", 2, ct);
            var lessonContext = lessons.Any() 
                ? "\nLEARNED LESSONS FROM PAST FAILURES:\n" + string.Join("\n", lessons.Select(l => $"- {l.ErrorPattern}: {l.Solution}"))
                : "";

            var prompt = $@"
            ROLE: Micro-Method Implementation Unit (Arban).
            YASA LAW: Output ONLY raw C# method body code. 
            NO explanations. NO markdown blocks. NO signatures.
            CLASS: {className}
            METHOD: {task.Signature}
            PURPOSE: {task.Purpose}
            HINTS: {task.ContextHints}
            {lessonContext}";

            var errors = new List<string>();
            var signature = signatureValidation.NormalizedSignature;
            var model = _ai.GetBestModel("fast");
            var maxAttempts = 3;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var currentPrompt = prompt;
                if (errors.Count > 0)
                {
                    currentPrompt += $"\n\nPREVIOUS ERRORS:\n- {string.Join("\n- ", errors.Take(3))}\nFix errors and return only valid method body.";
                    model = _ai.GetBestModel("coder");
                }

                var (timedOut, body) = await AskWithTimeoutAsync(currentPrompt, model, ct);
                if (timedOut)
                {
                    errors.Add($"Model call timed out after {_singleCallTimeout.TotalSeconds:0}s.");
                    continue;
                }

                var cleanBody = StripShell(body);
                if (cleanBody.Contains("Sure!", StringComparison.OrdinalIgnoreCase) ||
                    cleanBody.Contains("Here is", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("Model returned prose instead of code body.");
                    continue;
                }

                var wrappedCode = $"namespace ArbanValidation {{ public class Dummy {{ {signature} {{ {cleanBody} }} }} }}";
                var validation = _validator.ValidateSyntax(wrappedCode);
                if (validation.Valid)
                {
                    return new ArbanResult(task.Name, cleanBody, true, attempt, errors);
                }

                errors.AddRange(validation.Errors);
            }

            errors.Add("Method generation retries exhausted. Fallback method body injected.");
            return new ArbanResult(task.Name, BuildFallbackBody(task.Name), false, maxAttempts, errors);
        }

        private async Task<(bool TimedOut, string Response)> AskWithTimeoutAsync(string prompt, string model, CancellationToken ct)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var askTask = _ai.AskAsync(prompt, linkedCts.Token, model);
            var timeoutTask = Task.Delay(_singleCallTimeout, ct);
            var completed = await Task.WhenAny(askTask, timeoutTask);
            if (completed == askTask)
            {
                return (false, await askTask);
            }

            linkedCts.Cancel();
            return (true, string.Empty);
        }

        private string StripShell(string response)
        {
            var code = response.Trim();
            
            // 1. Remove Markdown
            if (code.Contains("```csharp")) code = code.Split("```csharp")[1].Split("```")[0].Trim();
            else if (code.Contains("```")) code = code.Split("```")[1].Split("```")[0].Trim();

            // 2. Remove Namespace and Class shells if present
            // Logic: Find the first '{' and the last '}'. If there are multiple levels, 
            // the LLM likely returned a full class. We want to skip the outermost level if it defines a class.
            
            if (code.Contains("class ") || code.Contains("namespace "))
            {
                var firstBrace = code.IndexOf('{');
                var lastBrace = code.LastIndexOf('}');
                
                if (firstBrace != -1 && lastBrace > firstBrace)
                {
                    // Extract content INSIDE the class
                    var inner = code.Substring(firstBrace + 1, lastBrace - firstBrace - 1).Trim();
                    
                    // Check if there is ANOTHER level (method level inside class content)
                    // If the LLM returned "public void Method() { code }", we might want just 'code'.
                    // But for Arban, we usually want the method body.
                    
                    var innerFirst = inner.IndexOf('{');
                    var innerLast = inner.LastIndexOf('}');
                    if (innerFirst != -1 && innerLast > innerFirst)
                    {
                        return inner.Substring(innerFirst + 1, innerLast - innerFirst - 1).Trim();
                    }
                    return inner;
                }
            }

            return code;
        }

        private static string BuildFallbackBody(string methodName)
        {
            return GenerationFallbackPolicy.BuildMethodSynthesisFallback(methodName);
        }

        private static TimeSpan ReadTimeoutFromEnvironment()
        {
            var raw = Environment.GetEnvironmentVariable("HELPER_METHOD_GEN_TIMEOUT_SEC");
            if (!int.TryParse(raw, out var seconds))
            {
                return TimeSpan.FromSeconds(45);
            }

            return TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 180));
        }
    }
}

