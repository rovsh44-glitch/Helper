using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class ReflectionService : IReflectionService
    {
        private readonly AILink _ai;
        private readonly IVectorStore _store;
        private readonly Func<string, CancellationToken, Task<float[]>> _embedAsync;
        private const string CollectionName = "engineering_lessons";

        public ReflectionService(AILink ai, IVectorStore store, Func<string, CancellationToken, Task<float[]>>? embedAsync = null)
        {
            _ai = ai;
            _store = store;
            _embedAsync = embedAsync ?? ai.EmbedAsync;
        }

        public async Task<EngineeringLesson?> ConductPostMortemAsync(string originalPrompt, List<BuildError> errors, string finalCode, CancellationToken ct = default)
        {
            if (errors == null || errors.Count == 0) return null;

            var errorSummary = string.Join("\n", errors.Select(e => $"[{e.Code}] {e.Message}"));
            
            var prompt = $@"
            ACT AS A SENIOR SYSTEM ANALYST.
            OBJECTIVE: Analyze a recent engineering failure and extract a permanent lesson.
            ORIGINAL REQUEST: {originalPrompt}
            BUILD ERRORS ENCOUNTERED:
            {errorSummary}
            FINAL WORKING CODE:
            {(finalCode.Length > 3000 ? finalCode.Substring(0, 3000) : finalCode)}
            
            TASK: Identify the core technical reason for the initial failure and the specific fix.
            OUTPUT ONLY JSON:
            {{
              ""ErrorPattern"": ""Common pattern/description of the bug"",
              ""Context"": ""Language/Framework context"",
              ""Solution"": ""Specific code-level instruction"",
              ""Principle"": ""General engineering principle""
            }}";

            try
            {
                var lessonData = await _ai.AskJsonAsync<EngineeringLesson>(prompt, ct);
                return lessonData with { CreatedAt = DateTime.UtcNow };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ReflectionService] Post-mortem generation failed: {ex.Message}");
                return null;
            }
        }

        public async Task<EngineeringLesson?> ConductSuccessReviewAsync(string originalPrompt, string finalCode, CancellationToken ct = default)
        {
            var prompt = $@"
            ACT AS A SENIOR ARCHITECT.
            OBJECTIVE: Analyze a successful implementation and extract a high-quality pattern.
            ORIGINAL REQUEST: {originalPrompt}
            IMPLEMENTED CODE:
            {(finalCode.Length > 3000 ? finalCode.Substring(0, 3000) : finalCode)}
            
            TASK: Identify why this implementation is considered a success and what reusable pattern was used.
            OUTPUT ONLY JSON:
            {{
              ""ErrorPattern"": ""Success Pattern: [Brief Title]"",
              ""Context"": ""Architecture/Framework context"",
              ""Solution"": ""Description of the implementation strategy that worked"",
              ""Principle"": ""Architectural principle demonstrated here""
            }}";

            try
            {
                var lessonData = await _ai.AskJsonAsync<EngineeringLesson>(prompt, ct);
                return lessonData with { CreatedAt = DateTime.UtcNow };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ReflectionService] Success review generation failed: {ex.Message}");
                return null;
            }
        }

        public async Task IngestLessonAsync(EngineeringLesson lesson, CancellationToken ct = default)
        {
            var errorPattern = LimitPayload(lesson.ErrorPattern, 220);
            var context = LimitPayload(lesson.Context, 320);
            var solution = LimitPayload(lesson.Solution, 420);
            var principle = LimitPayload(lesson.Principle, 260);
            var content = $"[LESSON] {errorPattern}\nContext: {context}\nSolution: {solution}\nPrinciple: {principle}";
            var embedding = await _embedAsync(content, ct);
            
            var chunk = new KnowledgeChunk(
                Id: Guid.NewGuid().ToString(),
                Content: content,
                Embedding: embedding,
                Metadata: new Dictionary<string, string>
                {
                    { "type", "engineering_lesson" },
                    { "error_pattern", errorPattern },
                    { "context", context },
                    { "solution", solution },
                    { "principle", principle },
                    { "created_at", lesson.CreatedAt.ToString("O") }
                },
                Collection: CollectionName
            );

            await _store.UpsertAsync(chunk, ct);
        }

        public async Task<List<EngineeringLesson>> SearchLessonsAsync(string context, int limit = 3, CancellationToken ct = default)
        {
            var embedding = await _embedAsync(context, ct);
            var results = await _store.SearchAsync(embedding, CollectionName, limit, ct);

            var lessons = new List<EngineeringLesson>();
            foreach (var res in results)
            {
                lessons.Add(RestoreLesson(res));
            }
            return lessons;
        }

        private static EngineeringLesson RestoreLesson(KnowledgeChunk chunk)
        {
            var metadata = chunk.Metadata ?? new Dictionary<string, string>();
            var context = metadata.GetValueOrDefault("context", "General");
            var errorPattern = metadata.GetValueOrDefault("error_pattern", string.Empty);
            var solution = metadata.GetValueOrDefault("solution", string.Empty);
            var principle = metadata.GetValueOrDefault("principle", string.Empty);

            if (string.IsNullOrWhiteSpace(errorPattern) ||
                string.IsNullOrWhiteSpace(solution) ||
                string.IsNullOrWhiteSpace(principle))
            {
                ParseLegacyContent(chunk.Content, ref errorPattern, ref context, ref solution, ref principle);
            }

            var dateStr = metadata.GetValueOrDefault("created_at", DateTime.UtcNow.ToString("O"));
            if (dateStr.Contains("stringValue"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(dateStr, "\"stringValue\": \"([^\"]+)\"");
                if (match.Success)
                {
                    dateStr = match.Groups[1].Value;
                }
            }

            return new EngineeringLesson(
                string.IsNullOrWhiteSpace(errorPattern) ? chunk.Content : errorPattern,
                string.IsNullOrWhiteSpace(context) ? "General" : context,
                solution,
                principle,
                DateTime.Parse(dateStr));
        }

        private static void ParseLegacyContent(
            string content,
            ref string errorPattern,
            ref string context,
            ref string solution,
            ref string principle)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[LESSON]", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(errorPattern))
                {
                    errorPattern = line.Replace("[LESSON]", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                }
                else if (line.StartsWith("Context:", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(context))
                {
                    context = line["Context:".Length..].Trim();
                }
                else if (line.StartsWith("Solution:", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(solution))
                {
                    solution = line["Solution:".Length..].Trim();
                }
                else if (line.StartsWith("Principle:", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(principle))
                {
                    principle = line["Principle:".Length..].Trim();
                }
            }
        }

        private static string LimitPayload(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().Replace("\r", " ").Replace("\n", " ");
            return normalized.Length <= maxLength
                ? normalized
                : normalized[..maxLength].TrimEnd() + "...";
        }
    }
}

