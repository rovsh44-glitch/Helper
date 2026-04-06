using System.Text;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public sealed record ConversationPromptAssembly(
    string Prompt,
    IReadOnlyList<string> UsedLayers,
    int HistoryMessageCount,
    int ProceduralLessonCount,
    int RetrievalChunkCount);

public interface IConversationContextAssembler
{
    Task<ConversationPromptAssembly> AssembleAsync(ChatTurnContext context, CancellationToken ct);
}

public sealed class ConversationContextAssembler : IConversationContextAssembler
{
    private readonly IReflectionService _reflectionService;
    private readonly IRetrievalContextAssembler _retrievalAssembler;
    private readonly IReasoningAwareRetrievalPolicy _retrievalPolicy;
    private readonly ISharedUnderstandingService _sharedUnderstandingService;
    private readonly IProjectInstructionPolicy _projectInstructionPolicy;
    private readonly IProjectMemoryBoundaryPolicy _projectMemoryBoundaryPolicy;

    public ConversationContextAssembler(
        IReflectionService reflectionService,
        IRetrievalContextAssembler retrievalAssembler,
        IReasoningAwareRetrievalPolicy retrievalPolicy,
        ISharedUnderstandingService? sharedUnderstandingService = null,
        IProjectInstructionPolicy? projectInstructionPolicy = null,
        IProjectMemoryBoundaryPolicy? projectMemoryBoundaryPolicy = null)
    {
        _reflectionService = reflectionService;
        _retrievalAssembler = retrievalAssembler;
        _retrievalPolicy = retrievalPolicy;
        _sharedUnderstandingService = sharedUnderstandingService ?? new SharedUnderstandingService();
        _projectInstructionPolicy = projectInstructionPolicy ?? new ProjectInstructionPolicy();
        _projectMemoryBoundaryPolicy = projectMemoryBoundaryPolicy ?? new ProjectMemoryBoundaryPolicy();
    }

    public async Task<ConversationPromptAssembly> AssembleAsync(ChatTurnContext context, CancellationToken ct)
    {
        var selection = MemoryLayerSelection.Resolve(context);
        var contextBlocks = new List<string>();
        var usedLayers = new List<string>();

        var nonSystemMessages = context.History
            .Where(message => !string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var recentMessages = nonSystemMessages.Count > 0
            ? nonSystemMessages.TakeLast(selection.HistoryMessageBudget).ToList()
            : context.History.TakeLast(selection.HistoryMessageBudget).ToList();
        if (recentMessages.Count > 0)
        {
            usedLayers.Add("recent_history");
        }

        AppendSystemContextBlocks(context.History, selection, contextBlocks, usedLayers);
        AppendSharedUnderstandingBlock(context, selection, contextBlocks, usedLayers);
        AppendProjectContextBlock(context, contextBlocks, usedLayers);
        var proceduralLessons = await TryLoadLessonsAsync(context, selection, contextBlocks, usedLayers, ct).ConfigureAwait(false);
        var retrievalChunks = await TryLoadRetrievalAsync(context, selection, contextBlocks, usedLayers, ct).ConfigureAwait(false);

        var prompt = ChatPromptFormatter.BuildConversationPrompt(recentMessages, contextBlocks);
        return new ConversationPromptAssembly(
            prompt,
            usedLayers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            recentMessages.Count,
            proceduralLessons,
            retrievalChunks);
    }

    private void AppendProjectContextBlock(
        ChatTurnContext context,
        List<string> contextBlocks,
        List<string> usedLayers)
    {
        var block = _projectInstructionPolicy.BuildContextBlock(context.Conversation.ProjectContext, context);
        if (string.IsNullOrWhiteSpace(block))
        {
            return;
        }

        contextBlocks.Add(block);
        usedLayers.Add("project_memory");
    }

    private static void AppendSystemContextBlocks(
        IReadOnlyList<ChatMessageDto> history,
        MemoryLayerSelection selection,
        List<string> contextBlocks,
        List<string> usedLayers)
    {
        foreach (var message in history.Where(message => string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)))
        {
            if (MemoryLayerSelection.IsBranchSummaryMessage(message) &&
                selection.Layers.Contains("branch_summary", StringComparer.OrdinalIgnoreCase))
            {
                contextBlocks.Add(message.Content);
                usedLayers.Add("branch_summary");
                continue;
            }

            if (MemoryLayerSelection.IsRollingSummaryMessage(message) &&
                selection.Layers.Contains("rolling_summary", StringComparer.OrdinalIgnoreCase))
            {
                contextBlocks.Add(message.Content);
                usedLayers.Add("rolling_summary");
                continue;
            }

            if (MemoryLayerSelection.IsConversationProfileMessage(message) &&
                selection.Layers.Contains("conversation_profile", StringComparer.OrdinalIgnoreCase))
            {
                contextBlocks.Add(message.Content);
                usedLayers.Add("conversation_profile");
            }
        }
    }

    private void AppendSharedUnderstandingBlock(
        ChatTurnContext context,
        MemoryLayerSelection selection,
        List<string> contextBlocks,
        List<string> usedLayers)
    {
        if (!selection.Layers.Contains("shared_understanding", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var block = _sharedUnderstandingService.BuildContextBlock(context.Conversation, context);
        if (string.IsNullOrWhiteSpace(block))
        {
            return;
        }

        contextBlocks.Add(block);
        usedLayers.Add("shared_understanding");
    }

    private async Task<int> TryLoadLessonsAsync(
        ChatTurnContext context,
        MemoryLayerSelection selection,
        List<string> contextBlocks,
        List<string> usedLayers,
        CancellationToken ct)
    {
        if (!selection.Layers.Contains("procedural_lessons", StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        try
        {
            var lessons = await _reflectionService
                .SearchLessonsAsync(context.Request.Message, selection.ProceduralLessonBudget, ct)
                .ConfigureAwait(false);
            if (lessons.Count == 0)
            {
                return 0;
            }

            var block = new StringBuilder();
            block.AppendLine("Procedural lessons:");
            foreach (var lesson in lessons)
            {
                block.Append("- Pattern: ").Append(lesson.ErrorPattern).AppendLine();
                block.Append("  Solution: ").Append(lesson.Solution).AppendLine();
                block.Append("  Principle: ").Append(lesson.Principle).AppendLine();
            }

            contextBlocks.Add(block.ToString().TrimEnd());
            usedLayers.Add("procedural_lessons");
            return lessons.Count;
        }
        catch
        {
            context.UncertaintyFlags.Add("procedural_lessons_unavailable");
            return 0;
        }
    }

    private async Task<int> TryLoadRetrievalAsync(
        ChatTurnContext context,
        MemoryLayerSelection selection,
        List<string> contextBlocks,
        List<string> usedLayers,
        CancellationToken ct)
    {
        if (!selection.Layers.Contains("structured_retrieval", StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        try
        {
            var retrievalPlan = _retrievalPolicy.Resolve(context, selection);
            context.SelectedRetrievalPurpose = FormatRetrievalPurpose(retrievalPlan.Options.Purpose);
            context.RetrievalTrace.Clear();
            context.RetrievalTrace.AddRange(retrievalPlan.Trace);
            var chunks = await LoadRetrievalChunksAsync(context.Request.Message, retrievalPlan.EffectiveLimit, retrievalPlan, ct).ConfigureAwait(false);
            chunks = await TryImproveRetrievalQualityAsync(context.Request.Message, chunks, retrievalPlan, context, ct).ConfigureAwait(false);

            if (chunks.Count == 0)
            {
                context.RetrievalTrace.Add("chunks:0");
                return 0;
            }

            context.RetrievalTrace.Add($"chunks:{chunks.Count}");
            context.RetrievalTrace.Add($"collections:{string.Join(",", chunks.Select(chunk => chunk.Metadata.GetValueOrDefault("collection", chunk.Collection)).Distinct(StringComparer.OrdinalIgnoreCase))}");

            var block = new StringBuilder();
            block.AppendLine("Retrieved context:");
            foreach (var chunk in chunks)
            {
                var preview = chunk.Content ?? string.Empty;
                preview = preview.Replace("\r", " ").Replace("\n", " ").Trim();
                if (preview.Length > 220)
                {
                    preview = preview[..220].TrimEnd() + "...";
                }

                var collection = chunk.Metadata.GetValueOrDefault("collection", chunk.Collection);
                block.Append("- [").Append(collection).Append("] ").AppendLine(preview);
            }

            contextBlocks.Add(block.ToString().TrimEnd());
            usedLayers.Add("structured_retrieval");
            return chunks.Count;
        }
        catch
        {
            context.UncertaintyFlags.Add("structured_retrieval_unavailable");
            context.RetrievalTrace.Add("status:unavailable");
            return 0;
        }
    }

    private Task<IReadOnlyList<KnowledgeChunk>> LoadRetrievalChunksAsync(
        string query,
        int limit,
        ReasoningAwareRetrievalPlan retrievalPlan,
        CancellationToken ct)
    {
        return _retrievalAssembler.AssembleAsync(
            query,
            limit: limit,
            expandContext: retrievalPlan.ExpandContext,
            ct: ct,
            options: retrievalPlan.Options);
    }

    private async Task<IReadOnlyList<KnowledgeChunk>> TryImproveRetrievalQualityAsync(
        string query,
        IReadOnlyList<KnowledgeChunk> chunks,
        ReasoningAwareRetrievalPlan retrievalPlan,
        ChatTurnContext context,
        CancellationToken ct)
    {
        try
        {
            var fitSummary = RetrievalChunkTopicalFitInspector.Summarize(chunks, retrievalPlan.TopicalFitFloor);
            var diversitySummary = RetrievalSourceDiversityInspector.Summarize(chunks, retrievalPlan.SourceReuseDominanceThreshold);
            context.RetrievalTrace.AddRange(fitSummary.Trace);
            context.RetrievalTrace.AddRange(diversitySummary.Trace);

            var selectedChunks = chunks;
            var selectedFitSummary = fitSummary;
            var selectedDiversitySummary = diversitySummary;

            if (!retrievalPlan.AllowDeepRetrievalFallback ||
                retrievalPlan.DeepRetrievalLimit <= retrievalPlan.EffectiveLimit ||
                (!fitSummary.NeedsDeeperRetrieval && !diversitySummary.NeedsBroaderRetrieval))
            {
                if (ShouldDiscardLowFitChunks(fitSummary, retrievalPlan))
                {
                    context.RetrievalTrace.Add("retrieval_quality:discarded_low_fit");
                    return Array.Empty<KnowledgeChunk>();
                }

                return chunks;
            }

            context.RetrievalTrace.Add("deep_retrieval:retry");
            var deeperChunks = await LoadRetrievalChunksAsync(query, retrievalPlan.DeepRetrievalLimit, retrievalPlan, ct).ConfigureAwait(false);
            var selectedDeeperChunks = deeperChunks.Take(retrievalPlan.EffectiveLimit).ToList();
            var deeperSummary = RetrievalChunkTopicalFitInspector.Summarize(selectedDeeperChunks, retrievalPlan.TopicalFitFloor);
            var deeperDiversitySummary = RetrievalSourceDiversityInspector.Summarize(selectedDeeperChunks, retrievalPlan.SourceReuseDominanceThreshold);
            context.RetrievalTrace.AddRange(deeperSummary.Trace.Select(static trace => $"deep_{trace}"));
            context.RetrievalTrace.AddRange(deeperDiversitySummary.Trace.Select(static trace => $"deep_{trace}"));

            var fitImproved = deeperSummary.IsMeaningfullyBetterThan(fitSummary);
            var diversityImproved = deeperDiversitySummary.IsMeaningfullyBetterThan(diversitySummary);

            if (selectedDeeperChunks.Count == 0 || (!fitImproved && !diversityImproved))
            {
                context.RetrievalTrace.Add("deep_retrieval:rejected");
            }
            else
            {
                context.RetrievalTrace.Add("deep_retrieval:accepted");
                context.RetrievalTrace.AddRange(deeperSummary.Trace.Select(static trace => $"selected_{trace}"));
                context.RetrievalTrace.AddRange(deeperDiversitySummary.Trace.Select(static trace => $"selected_{trace}"));
                selectedChunks = selectedDeeperChunks;
                selectedFitSummary = deeperSummary;
                selectedDiversitySummary = deeperDiversitySummary;
            }

            if (ShouldDiscardLowFitChunks(selectedFitSummary, retrievalPlan))
            {
                context.RetrievalTrace.Add("retrieval_quality:discarded_low_fit");
                return Array.Empty<KnowledgeChunk>();
            }

            if (selectedChunks == chunks && selectedDiversitySummary != diversitySummary)
            {
                context.RetrievalTrace.AddRange(selectedDiversitySummary.Trace.Select(static trace => $"selected_{trace}"));
            }

            return selectedChunks;
        }
        catch
        {
            context.RetrievalTrace.Add("retrieval_quality_analysis:unavailable");
            return chunks;
        }
    }

    private static bool ShouldDiscardLowFitChunks(
        RetrievalChunkTopicalFitSummary summary,
        ReasoningAwareRetrievalPlan retrievalPlan)
    {
        return summary.HasAssessments &&
               string.Equals(summary.AggregateLabel, "low", StringComparison.OrdinalIgnoreCase) &&
               summary.AverageScore < retrievalPlan.TopicalFitFloor;
    }

    private static string FormatRetrievalPurpose(RetrievalPurpose purpose)
    {
        return purpose switch
        {
            RetrievalPurpose.FactualLookup => "factual_lookup",
            RetrievalPurpose.ReasoningSupport => "reasoning_support",
            _ => "standard"
        };
    }
}

