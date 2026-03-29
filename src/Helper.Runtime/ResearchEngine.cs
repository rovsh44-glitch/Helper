using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class ResearchEngine : IResearchEngine
    {
        private static readonly HashSet<string> SupportedDirectoryScanExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf",
            ".epub",
            ".docx",
            ".djvu",
            ".txt",
            ".jpg",
            ".png",
            ".md",
            ".markdown",
            ".html",
            ".htm",
            ".fb2",
            ".chm"
        };

        private readonly IResearchService _researcher;
        private readonly ILibrarianAgent _librarian;
        private readonly IGoalManager _goals;
        private readonly IStrategicPlanner _strategy;
        private readonly ICriticService _critic;
        private readonly AILink _ai;

        public ResearchEngine(
            IResearchService researcher, ILibrarianAgent librarian, 
            IGoalManager goals, IStrategicPlanner strategy, 
            ICriticService critic, AILink ai)
        {
            _researcher = researcher;
            _librarian = librarian;
            _goals = goals;
            _strategy = strategy;
            _critic = critic;
            _ai = ai;
        }

        public async Task<GenerationResult> HandleResearchModeAsync(GenerationRequest request, IntentAnalysis analysis, Action<string>? onProgress, CancellationToken ct)
        {
            bool isRussian = System.Text.RegularExpressions.Regex.IsMatch(request.Prompt, @"[а-яА-Я]");
            var batchContext = new StringBuilder();

            if (!string.IsNullOrEmpty(analysis.TargetPath))
            {
                if (File.Exists(analysis.TargetPath))
                {
                    onProgress?.Invoke(isRussian ? $"📖 Индексация документа: {Path.GetFileName(analysis.TargetPath)}..." : $"📖 Indexing document: {Path.GetFileName(analysis.TargetPath)}...");
                    var text = await _librarian.IndexFileAsync(analysis.TargetPath!, null, ct);
                    batchContext.AppendLine(text.Replace("[img-0]", "").Replace("![Image]", ""));
                }
                else if (Directory.Exists(analysis.TargetPath))
                {
                    onProgress?.Invoke(isRussian ? $"📂 Индексация директории: {analysis.TargetPath}..." : $"📂 Indexing directory: {analysis.TargetPath}...");

                    // Mirror the main indexing pipeline so nested author folders and FB2 docs are not skipped.
                    var files = Directory.EnumerateFiles(analysis.TargetPath!, "*.*", SearchOption.AllDirectories)
                        .Where(f => SupportedDirectoryScanExtensions.Contains(Path.GetExtension(f)));
                    
                    foreach (var file in files)
                    {
                        onProgress?.Invoke(isRussian ? $"📄 Обработка: {Path.GetFileName(file)}..." : $"📄 Processing: {Path.GetFileName(file)}...");
                        var text = await _librarian.IndexFileAsync(file, null, ct);
                        var cleanText = text.Replace("[img-0]", "").Replace("![Image]", "").Trim();
                        var summary = cleanText.Length > 1000 ? cleanText.Substring(0, 1000) + "..." : cleanText;
                        batchContext.AppendLine($"File: {Path.GetFileName(file)}\nAnalysis: {summary}\n---");
                        await Task.Delay(3000, ct);
                    }
                }
            }
            
            var fullBatchContext = batchContext.ToString();
            if (fullBatchContext.Length > 5000) fullBatchContext = fullBatchContext.Substring(0, 5000) + "... [Truncated]";
            
            onProgress?.Invoke(isRussian ? "🧠 Планирование стратегии (Метапознание)..." : "🧠 Thinking about the plan (Metacognition)...");
            var activeGoals = await _goals.GetActiveGoalsAsync(ct);
            var goalsContext = string.Join("\n", activeGoals.Select(g => $"{g.Title}: {g.Description}"));
            
            var strategicPrompt = $"CURRENT GOALS:\n{goalsContext}\n\nTASK: {request.Prompt}";
            var strategicPlan = await _strategy.PlanStrategyAsync(strategicPrompt, fullBatchContext, ct);
            
            onProgress?.Invoke($"[STRATEGY_JSON]{JsonSerializer.Serialize(strategicPlan)}");
            onProgress?.Invoke(isRussian ? $"🎯 Выбранная стратегия: {strategicPlan.SelectedStrategyId}." : $"🎯 Chosen strategy: {strategicPlan.SelectedStrategyId}.");

            if (strategicPlan.RequiresMoreInfo)
            {
                throw new Exception(isRussian ? "Планирование приостановлено." : "Strategic planning suspended.");
            }

            var researchPrompt = $@"OBJECTIVE: {request.Prompt} | TASK: Write detailed facts-based research report.";
            
            try 
            {
                var research = await ConductResearchAsync(researchPrompt, 1, onProgress, ct);
                var evidence = !string.IsNullOrEmpty(research.RawEvidence) ? research.RawEvidence : fullBatchContext;
                var critique = await _critic.CritiqueAsync(evidence, research.FullReport, "Strategic Analysis", ct);
                var finalReport = critique.IsApproved ? research.FullReport : (critique.CorrectedContent ?? research.FullReport);
                
                var reportPath = Path.Combine(request.OutputPath, "RESEARCH_REPORT.md");
                Directory.CreateDirectory(request.OutputPath);
                await File.WriteAllTextAsync(reportPath, finalReport, ct);

                return new GenerationResult(true, new List<GeneratedFile> { new GeneratedFile("RESEARCH_REPORT.md", finalReport, "markdown") }, request.OutputPath, new List<BuildError>(), TimeSpan.Zero, 0, true);
            }
            catch
            {
                _ai.SwitchModel("qwen2.5-coder:14b");
                var research = await ConductResearchAsync(researchPrompt, 1, onProgress, ct);
                return new GenerationResult(true, new List<GeneratedFile> { new GeneratedFile("RESEARCH_REPORT.md", research.FullReport, "markdown") }, request.OutputPath, new List<BuildError>(), TimeSpan.Zero, 0, true);
            }
        }

        public Task<ResearchResult> ConductResearchAsync(string topic, int depth = 1, Action<string>? onProgress = null, CancellationToken ct = default)
            => _researcher.ResearchAsync(topic, depth, onProgress, ct);
    }
}

