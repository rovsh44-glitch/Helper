namespace Helper.Api.Hosting;

public sealed partial class ConversationMetricsService
{
    public ConversationMetricsSnapshot GetSnapshot()
    {
        var turns = Volatile.Read(ref _turns);
        if (turns == 0)
        {
            return new ConversationMetricsSnapshot(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                new ConversationStyleMetricsSnapshot(0, 0, 0, 0, 0, 0, 0, Array.Empty<string>()),
                BuildReasoningSnapshot(0),
                Array.Empty<string>());
        }

        var avgFirst = (double)Volatile.Read(ref _firstTokenLatencySum) / turns;
        var avgFull = (double)Volatile.Read(ref _fullLatencySum) / turns;
        var toolCalls = (int)Volatile.Read(ref _toolCalls);
        var factualTurns = Volatile.Read(ref _factualTurns);
        var factualWithCitations = Volatile.Read(ref _factualWithCitations);
        var totalClaims = Volatile.Read(ref _totalClaims);
        var verifiedClaims = Volatile.Read(ref _verifiedClaims);
        var citationCoverage = totalClaims > 0
            ? (double)verifiedClaims / totalClaims
            : (factualTurns == 0 ? 0 : (double)factualWithCitations / factualTurns);
        var avgConfidence = (double)Volatile.Read(ref _confidenceMilliSum) / (turns * 1000);
        var successRate = (double)Volatile.Read(ref _successTurns) / turns;
        var modelSamples = Volatile.Read(ref _modelTtftSamples);
        var transportSamples = Volatile.Read(ref _transportTtftSamples);
        var endToEndSamples = Volatile.Read(ref _endToEndTtftSamples);
        var avgModelTtft = modelSamples > 0 ? (double)Volatile.Read(ref _modelTtftSum) / modelSamples : 0;
        var avgTransportTtft = transportSamples > 0 ? (double)Volatile.Read(ref _transportTtftSum) / transportSamples : 0;
        var avgEndToEndTtft = endToEndSamples > 0 ? (double)Volatile.Read(ref _endToEndTtftSum) / endToEndSamples : 0;
        var budgetExceededRate = (double)Volatile.Read(ref _budgetExceededTurns) / turns;
        var fastModeTurns = (int)Volatile.Read(ref _fastModeTurns);
        var balancedModeTurns = (int)Volatile.Read(ref _balancedModeTurns);
        var deepModeTurns = (int)Volatile.Read(ref _deepModeTurns);
        var unknownModeTurns = (int)Volatile.Read(ref _unknownModeTurns);
        var researchRoutedTurns = (int)Volatile.Read(ref _researchRoutedTurns);
        var researchClarificationFallbackTurns = (int)Volatile.Read(ref _researchClarificationFallbackTurns);
        var style = BuildStyleSnapshot();
        var reasoning = BuildReasoningSnapshot(turns);

        var alerts = new List<string>();
        if (avgFirst > 1200) alerts.Add("First-token latency exceeded SLO (>1200ms average).");
        if (avgFull > 2000) alerts.Add("Full-response latency exceeded SLO (>2000ms average).");
        if (modelSamples > 0 && avgModelTtft > 1200) alerts.Add("Model TTFT exceeded SLO (>1200ms average).");
        if (endToEndSamples > 0 && avgEndToEndTtft > 2000) alerts.Add("End-to-end TTFT exceeded SLO (>2000ms average).");
        if (budgetExceededRate > 0.20) alerts.Add("Budget exceeded rate is above 20%; tune fast/balanced/deep policy.");
        if (factualTurns > 0 && citationCoverage < 0.70) alerts.Add("Citation coverage for factual prompts is below 70%.");
        if (successRate < 0.85) alerts.Add("Conversation success rate is below 85%.");
        if (researchRoutedTurns > 0 && (double)researchClarificationFallbackTurns / researchRoutedTurns > 0.15)
        {
            alerts.Add("Research clarification fallback rate is above 15%; review research intent routing.");
        }

        alerts.AddRange(style.Alerts);
        alerts.AddRange(reasoning.Alerts);

        return new ConversationMetricsSnapshot(
            (int)turns,
            avgFirst,
            avgFull,
            toolCalls,
            citationCoverage,
            (int)verifiedClaims,
            (int)totalClaims,
            avgConfidence,
            successRate,
            avgModelTtft,
            avgTransportTtft,
            avgEndToEndTtft,
            budgetExceededRate,
            fastModeTurns,
            balancedModeTurns,
            deepModeTurns,
            unknownModeTurns,
            researchRoutedTurns,
            researchClarificationFallbackTurns,
            style,
            reasoning,
            alerts);
    }

    private ConversationStyleMetricsSnapshot BuildStyleSnapshot()
    {
        var styleTurns = Volatile.Read(ref _styleTurns);
        if (styleTurns <= 0)
        {
            return new ConversationStyleMetricsSnapshot(0, 0, 0, 0, 0, 0, 0, Array.Empty<string>());
        }

        var repeatedPhraseRate = (double)Volatile.Read(ref _styleRepeatedPhraseTurns) / styleTurns;
        var mixedLanguageRate = (double)Volatile.Read(ref _styleMixedLanguageTurns) / styleTurns;
        var genericClarificationRate = (double)Volatile.Read(ref _styleGenericClarificationTurns) / styleTurns;
        var genericNextStepRate = (double)Volatile.Read(ref _styleGenericNextStepTurns) / styleTurns;
        var memoryAckTemplateRate = (double)Volatile.Read(ref _styleMemoryAckTemplateTurns) / styleTurns;
        var sourceTurns = Volatile.Read(ref _styleSourceTurns);
        var sourceReuseDominance = sourceTurns > 0 && _sourceFingerprintCounts.Count > 0
            ? (double)_sourceFingerprintCounts.Values.Max() / sourceTurns
            : 0;

        return new ConversationStyleMetricsSnapshot(
            Turns: (int)styleTurns,
            RepeatedPhraseRate: repeatedPhraseRate,
            MixedLanguageTurnRate: mixedLanguageRate,
            GenericClarificationRate: genericClarificationRate,
            GenericNextStepRate: genericNextStepRate,
            MemoryAckTemplateRate: memoryAckTemplateRate,
            SourceReuseDominance: sourceReuseDominance,
            Alerts: BuildStyleAlerts(
                repeatedPhraseRate,
                mixedLanguageRate,
                genericClarificationRate,
                genericNextStepRate,
                memoryAckTemplateRate,
                sourceReuseDominance));
    }

    private ReasoningEfficiencyMetricsSnapshot BuildReasoningSnapshot(long totalTurns)
    {
        var reasoningTurns = Volatile.Read(ref _reasoningTurns);
        if (reasoningTurns <= 0)
        {
            return new ReasoningEfficiencyMetricsSnapshot(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                Array.Empty<string>());
        }

        var branchingTurns = Volatile.Read(ref _reasoningBranchingTurns);
        var localVerificationChecks = Volatile.Read(ref _reasoningLocalVerificationChecksSum);
        var localVerificationPasses = Volatile.Read(ref _reasoningLocalVerificationPassesSum);
        var localVerificationRejects = Volatile.Read(ref _reasoningLocalVerificationRejectsSum);
        var branchingRate = totalTurns > 0 ? (double)branchingTurns / totalTurns : 0;
        var localVerificationPassRate = localVerificationChecks > 0
            ? (double)localVerificationPasses / localVerificationChecks
            : 0;

        return new ReasoningEfficiencyMetricsSnapshot(
            Turns: (int)reasoningTurns,
            BranchingTurns: (int)branchingTurns,
            BranchingRate: branchingRate,
            AvgBranchesExplored: (double)Volatile.Read(ref _reasoningBranchesExploredSum) / reasoningTurns,
            AvgCandidatesRejected: (double)Volatile.Read(ref _reasoningCandidatesRejectedSum) / reasoningTurns,
            LocalVerificationChecks: (int)localVerificationChecks,
            LocalVerificationPasses: (int)localVerificationPasses,
            LocalVerificationRejects: (int)localVerificationRejects,
            LocalVerificationPassRate: localVerificationPassRate,
            AvgModelCallsUsed: (double)Volatile.Read(ref _reasoningModelCallsUsedSum) / reasoningTurns,
            AvgRetrievalChunksUsed: (double)Volatile.Read(ref _reasoningRetrievalChunksUsedSum) / reasoningTurns,
            AvgProceduralLessonsUsed: (double)Volatile.Read(ref _reasoningProceduralLessonsUsedSum) / reasoningTurns,
            AvgApproximateTokenCost: (double)Volatile.Read(ref _reasoningApproximateTokenCostSum) / reasoningTurns,
            Alerts: BuildReasoningAlerts(reasoningTurns, localVerificationChecks, localVerificationPassRate));
    }

    private static IReadOnlyList<string> BuildReasoningAlerts(long reasoningTurns, long localVerificationChecks, double localVerificationPassRate)
    {
        var alerts = new List<string>();
        if (reasoningTurns > 0 && localVerificationChecks > 0 && localVerificationPassRate < 0.40)
        {
            alerts.Add("Reasoning local verification pass rate is below 40%; review branch admission and verifier fit.");
        }

        return alerts;
    }

    private static IReadOnlyList<string> BuildStyleAlerts(
        double repeatedPhraseRate,
        double mixedLanguageRate,
        double genericClarificationRate,
        double genericNextStepRate,
        double memoryAckTemplateRate,
        double sourceReuseDominance)
    {
        var alerts = new List<string>();
        if (repeatedPhraseRate > 0.18)
        {
            alerts.Add("Repeated lead-phrase rate is above 18%; conversational framing is becoming template-heavy.");
        }

        if (mixedLanguageRate > 0.0)
        {
            alerts.Add("Mixed-language turn rate is above 0%; language-lock regressions detected.");
        }

        if (genericClarificationRate > 0.15)
        {
            alerts.Add("Generic clarification rate is above 15%; clarification UX is drifting toward robotic prompts.");
        }

        if (genericNextStepRate > 0.20)
        {
            alerts.Add("Generic next-step rate is above 20%; responses are overusing canned CTA endings.");
        }

        if (memoryAckTemplateRate > 0.20)
        {
            alerts.Add("Memory acknowledgement template rate is above 20%; remember-flow variation is too low.");
        }

        if (sourceReuseDominance > 0.60)
        {
            alerts.Add("Source reuse dominance is above 60%; retrieval is over-concentrated on the same source set.");
        }

        return alerts;
    }
}
