using System.Linq;
using System.Text;
using Helper.Api.Backend.ControlPlane;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Api.Hosting;

public interface IPrometheusMetricsFormatter
{
    string Format(
        MetricsSnapshot requestMetrics,
        ConversationMetricsSnapshot conversationMetrics,
        WebResearchTelemetrySnapshot webResearchMetrics,
        HumanLikeConversationDashboardSnapshot humanLikeConversationMetrics,
        ConversationStageMetricsSnapshot conversationStageMetrics,
        ToolAuditSnapshot toolMetrics,
        ConversationFeedbackSnapshot feedbackMetrics,
        ChatResilienceSnapshot resilienceMetrics,
        IntentTelemetrySnapshot intentMetrics,
        Helper.Api.Conversation.PostTurnAuditSnapshot postTurnAuditMetrics,
        GenerationMetricsSnapshot generationMetrics,
        BackendControlPlaneSnapshot controlPlaneMetrics);
}

public sealed class PrometheusMetricsFormatter : IPrometheusMetricsFormatter
{
    public string Format(
        MetricsSnapshot requestMetrics,
        ConversationMetricsSnapshot conversationMetrics,
        WebResearchTelemetrySnapshot webResearchMetrics,
        HumanLikeConversationDashboardSnapshot humanLikeConversationMetrics,
        ToolAuditSnapshot toolMetrics,
        ConversationFeedbackSnapshot feedbackMetrics,
        ChatResilienceSnapshot resilienceMetrics,
        IntentTelemetrySnapshot intentMetrics,
        Helper.Api.Conversation.PostTurnAuditSnapshot postTurnAuditMetrics,
        GenerationMetricsSnapshot generationMetrics,
        BackendControlPlaneSnapshot controlPlaneMetrics)
    {
        return Format(
            requestMetrics,
            conversationMetrics,
            webResearchMetrics,
            humanLikeConversationMetrics,
            new ConversationStageMetricsSnapshot(Array.Empty<ConversationStageBucketSnapshot>(), Array.Empty<string>()),
            toolMetrics,
            feedbackMetrics,
            resilienceMetrics,
            intentMetrics,
            postTurnAuditMetrics,
            generationMetrics,
            controlPlaneMetrics);
    }

    public string Format(
        MetricsSnapshot requestMetrics,
        ConversationMetricsSnapshot conversationMetrics,
        WebResearchTelemetrySnapshot webResearchMetrics,
        HumanLikeConversationDashboardSnapshot humanLikeConversationMetrics,
        ConversationStageMetricsSnapshot conversationStageMetrics,
        ToolAuditSnapshot toolMetrics,
        ConversationFeedbackSnapshot feedbackMetrics,
        ChatResilienceSnapshot resilienceMetrics,
        IntentTelemetrySnapshot intentMetrics,
        Helper.Api.Conversation.PostTurnAuditSnapshot postTurnAuditMetrics,
        GenerationMetricsSnapshot generationMetrics,
        BackendControlPlaneSnapshot controlPlaneMetrics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# HELP helper_requests_total Total API requests.");
        sb.AppendLine("# TYPE helper_requests_total gauge");
        sb.AppendLine($"helper_requests_total {requestMetrics.TotalRequests}");

        sb.AppendLine("# HELP helper_request_errors_total Total API errors.");
        sb.AppendLine("# TYPE helper_request_errors_total gauge");
        sb.AppendLine($"helper_request_errors_total {requestMetrics.TotalErrors}");

        sb.AppendLine("# HELP helper_request_canceled_total Total canceled API requests.");
        sb.AppendLine("# TYPE helper_request_canceled_total gauge");
        sb.AppendLine($"helper_request_canceled_total {requestMetrics.TotalCanceled}");

        sb.AppendLine("# HELP helper_request_timeout_total Total timeout-class API requests.");
        sb.AppendLine("# TYPE helper_request_timeout_total gauge");
        sb.AppendLine($"helper_request_timeout_total {requestMetrics.TotalTimeouts}");

        sb.AppendLine("# HELP helper_request_5xx_total Total 5xx API responses.");
        sb.AppendLine("# TYPE helper_request_5xx_total gauge");
        sb.AppendLine($"helper_request_5xx_total {requestMetrics.Total5xx}");

        var chatEndpoints = requestMetrics.Endpoints
            .Where(endpoint => endpoint.Path.StartsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var chatTimeoutTotal = chatEndpoints.Sum(endpoint => endpoint.TimeoutCount);
        var chatCanceledTotal = chatEndpoints.Sum(endpoint => endpoint.CanceledCount);
        var chat5xxTotal = chatEndpoints.Sum(endpoint => endpoint.ServerErrorCount);

        sb.AppendLine("# HELP helper_chat_timeout_total Total timeout-class outcomes for chat endpoints.");
        sb.AppendLine("# TYPE helper_chat_timeout_total gauge");
        sb.AppendLine($"helper_chat_timeout_total {chatTimeoutTotal}");

        sb.AppendLine("# HELP helper_chat_canceled_total Total canceled requests for chat endpoints.");
        sb.AppendLine("# TYPE helper_chat_canceled_total gauge");
        sb.AppendLine($"helper_chat_canceled_total {chatCanceledTotal}");

        sb.AppendLine("# HELP helper_chat_5xx_total Total 5xx responses for chat endpoints.");
        sb.AppendLine("# TYPE helper_chat_5xx_total gauge");
        sb.AppendLine($"helper_chat_5xx_total {chat5xxTotal}");

        sb.AppendLine("# HELP helper_conversation_ttft_ms Average first-token latency.");
        sb.AppendLine("# TYPE helper_conversation_ttft_ms gauge");
        sb.AppendLine($"helper_conversation_ttft_ms {conversationMetrics.AvgFirstTokenLatencyMs:F2}");

        sb.AppendLine("# HELP helper_conversation_full_latency_ms Average full response latency.");
        sb.AppendLine("# TYPE helper_conversation_full_latency_ms gauge");
        sb.AppendLine($"helper_conversation_full_latency_ms {conversationMetrics.AvgFullResponseLatencyMs:F2}");

        sb.AppendLine("# HELP helper_model_ttft_ms Average model time-to-first-token.");
        sb.AppendLine("# TYPE helper_model_ttft_ms gauge");
        sb.AppendLine($"helper_model_ttft_ms {conversationMetrics.AvgModelTtftMs:F2}");

        sb.AppendLine("# HELP helper_transport_ttft_ms Average transport time-to-first-token.");
        sb.AppendLine("# TYPE helper_transport_ttft_ms gauge");
        sb.AppendLine($"helper_transport_ttft_ms {conversationMetrics.AvgTransportTtftMs:F2}");

        sb.AppendLine("# HELP helper_end_to_end_ttft_ms Average end-to-end time-to-first-token.");
        sb.AppendLine("# TYPE helper_end_to_end_ttft_ms gauge");
        sb.AppendLine($"helper_end_to_end_ttft_ms {conversationMetrics.AvgEndToEndTtftMs:F2}");

        sb.AppendLine("# HELP helper_budget_exceeded_rate Ratio of turns where budget limits were hit.");
        sb.AppendLine("# TYPE helper_budget_exceeded_rate gauge");
        sb.AppendLine($"helper_budget_exceeded_rate {conversationMetrics.BudgetExceededRate:F4}");

        sb.AppendLine("# HELP helper_conversation_stage_avg_ms Average stage latency.");
        sb.AppendLine("# TYPE helper_conversation_stage_avg_ms gauge");
        foreach (var stage in conversationStageMetrics.Stages)
        {
            var label = EscapeLabel(stage.Stage);
            sb.AppendLine($"helper_conversation_stage_avg_ms{{stage=\"{label}\"}} {stage.AvgLatencyMs:F2}");
        }

        sb.AppendLine("# HELP helper_conversation_stage_p95_ms Approximate p95 stage latency.");
        sb.AppendLine("# TYPE helper_conversation_stage_p95_ms gauge");
        foreach (var stage in conversationStageMetrics.Stages)
        {
            var label = EscapeLabel(stage.Stage);
            sb.AppendLine($"helper_conversation_stage_p95_ms{{stage=\"{label}\"}} {stage.P95LatencyMs:F2}");
        }

        sb.AppendLine("# HELP helper_execution_mode_fast_total Number of turns executed in fast mode.");
        sb.AppendLine("# TYPE helper_execution_mode_fast_total gauge");
        sb.AppendLine($"helper_execution_mode_fast_total {conversationMetrics.FastModeTurns}");

        sb.AppendLine("# HELP helper_execution_mode_balanced_total Number of turns executed in balanced mode.");
        sb.AppendLine("# TYPE helper_execution_mode_balanced_total gauge");
        sb.AppendLine($"helper_execution_mode_balanced_total {conversationMetrics.BalancedModeTurns}");

        sb.AppendLine("# HELP helper_execution_mode_deep_total Number of turns executed in deep mode.");
        sb.AppendLine("# TYPE helper_execution_mode_deep_total gauge");
        sb.AppendLine($"helper_execution_mode_deep_total {conversationMetrics.DeepModeTurns}");

        sb.AppendLine("# HELP helper_citation_coverage Ratio of factual turns with citations.");
        sb.AppendLine("# TYPE helper_citation_coverage gauge");
        sb.AppendLine($"helper_citation_coverage {conversationMetrics.CitationCoverage:F4}");

        sb.AppendLine("# HELP helper_verified_claims_total Verified factual claims count.");
        sb.AppendLine("# TYPE helper_verified_claims_total gauge");
        sb.AppendLine($"helper_verified_claims_total {conversationMetrics.VerifiedClaims}");

        sb.AppendLine("# HELP helper_total_claims_total Total factual claims count.");
        sb.AppendLine("# TYPE helper_total_claims_total gauge");
        sb.AppendLine($"helper_total_claims_total {conversationMetrics.TotalClaims}");

        sb.AppendLine("# HELP helper_tool_success_ratio Ratio of successful tool calls.");
        sb.AppendLine("# TYPE helper_tool_success_ratio gauge");
        sb.AppendLine($"helper_tool_success_ratio {toolMetrics.SuccessRatio:F4}");

        sb.AppendLine("# HELP helper_tool_calls_total Total tool calls by source.");
        sb.AppendLine("# TYPE helper_tool_calls_total gauge");
        foreach (var source in toolMetrics.Sources)
        {
            var label = EscapeLabel(source.Source);
            sb.AppendLine($"helper_tool_calls_total{{source=\"{label}\"}} {source.TotalCalls}");
        }

        sb.AppendLine("# HELP helper_tool_failures_total Failed tool calls by source.");
        sb.AppendLine("# TYPE helper_tool_failures_total gauge");
        foreach (var source in toolMetrics.Sources)
        {
            var label = EscapeLabel(source.Source);
            sb.AppendLine($"helper_tool_failures_total{{source=\"{label}\"}} {source.FailedCalls}");
        }

        sb.AppendLine("# HELP helper_tool_success_ratio_by_source Ratio of successful tool calls by source.");
        sb.AppendLine("# TYPE helper_tool_success_ratio_by_source gauge");
        foreach (var source in toolMetrics.Sources)
        {
            var label = EscapeLabel(source.Source);
            sb.AppendLine($"helper_tool_success_ratio_by_source{{source=\"{label}\"}} {source.SuccessRatio:F4}");
        }

        sb.AppendLine("# HELP helper_user_helpfulness_average Average user helpfulness score (1-5).");
        sb.AppendLine("# TYPE helper_user_helpfulness_average gauge");
        sb.AppendLine($"helper_user_helpfulness_average {feedbackMetrics.AverageRating:F2}");

        sb.AppendLine("# HELP helper_chat_retries_total Total retry attempts in chat resilience policy.");
        sb.AppendLine("# TYPE helper_chat_retries_total gauge");
        sb.AppendLine($"helper_chat_retries_total {resilienceMetrics.TotalRetries}");

        sb.AppendLine("# HELP helper_chat_circuit_open_total Total circuit-open events.");
        sb.AppendLine("# TYPE helper_chat_circuit_open_total gauge");
        sb.AppendLine($"helper_chat_circuit_open_total {resilienceMetrics.TotalCircuitOpenEvents}");

        sb.AppendLine("# HELP helper_chat_fallback_total Total fail-open fallback events.");
        sb.AppendLine("# TYPE helper_chat_fallback_total gauge");
        sb.AppendLine($"helper_chat_fallback_total {resilienceMetrics.TotalFallbacks}");

        sb.AppendLine("# HELP helper_intent_low_confidence_rate Share of low-confidence intent classifications.");
        sb.AppendLine("# TYPE helper_intent_low_confidence_rate gauge");
        sb.AppendLine($"helper_intent_low_confidence_rate {intentMetrics.LowConfidenceRate:F4}");

        sb.AppendLine("# HELP helper_intent_avg_confidence Average confidence of intent classifier.");
        sb.AppendLine("# TYPE helper_intent_avg_confidence gauge");
        sb.AppendLine($"helper_intent_avg_confidence {intentMetrics.AvgConfidence:F4}");

        var intentUnknownTotal = GetIntentCount(intentMetrics, "unknown");
        sb.AppendLine("# HELP helper_intent_unknown_total Total intent classifications routed to unknown.");
        sb.AppendLine("# TYPE helper_intent_unknown_total gauge");
        sb.AppendLine($"helper_intent_unknown_total {intentUnknownTotal}");

        sb.AppendLine("# HELP helper_research_routed_total Total turns routed to research intent.");
        sb.AppendLine("# TYPE helper_research_routed_total gauge");
        sb.AppendLine($"helper_research_routed_total {conversationMetrics.ResearchRoutedTurns}");

        sb.AppendLine("# HELP helper_research_clarification_fallback_total Total research turns that degraded to clarification-required output.");
        sb.AppendLine("# TYPE helper_research_clarification_fallback_total gauge");
        sb.AppendLine($"helper_research_clarification_fallback_total {conversationMetrics.ResearchClarificationFallbackTurns}");

        sb.AppendLine("# HELP helper_web_research_turns_total Total tracked turns with active web-research surface.");
        sb.AppendLine("# TYPE helper_web_research_turns_total gauge");
        sb.AppendLine($"helper_web_research_turns_total {webResearchMetrics.Turns}");

        sb.AppendLine("# HELP helper_web_research_live_turns_total Total tracked turns that executed live web search.");
        sb.AppendLine("# TYPE helper_web_research_live_turns_total gauge");
        sb.AppendLine($"helper_web_research_live_turns_total {webResearchMetrics.LiveWebTurns}");

        sb.AppendLine("# HELP helper_web_research_cached_turns_total Total tracked turns that reused cached web evidence.");
        sb.AppendLine("# TYPE helper_web_research_cached_turns_total gauge");
        sb.AppendLine($"helper_web_research_cached_turns_total {webResearchMetrics.CachedWebTurns}");

        sb.AppendLine("# HELP helper_web_research_avg_queries_per_turn Average executed web query count per tracked turn.");
        sb.AppendLine("# TYPE helper_web_research_avg_queries_per_turn gauge");
        sb.AppendLine($"helper_web_research_avg_queries_per_turn {webResearchMetrics.AvgQueriesPerTurn:F2}");

        sb.AppendLine("# HELP helper_web_research_avg_fetched_pages_per_turn Average fetched-page count per tracked turn.");
        sb.AppendLine("# TYPE helper_web_research_avg_fetched_pages_per_turn gauge");
        sb.AppendLine($"helper_web_research_avg_fetched_pages_per_turn {webResearchMetrics.AvgFetchedPagesPerTurn:F2}");

        sb.AppendLine("# HELP helper_web_research_avg_passages_per_turn Average extracted passage count per tracked turn.");
        sb.AppendLine("# TYPE helper_web_research_avg_passages_per_turn gauge");
        sb.AppendLine($"helper_web_research_avg_passages_per_turn {webResearchMetrics.AvgPassagesPerTurn:F2}");

        sb.AppendLine("# HELP helper_web_research_blocked_fetch_total Total blocked web fetch attempts surfaced in tracked turns.");
        sb.AppendLine("# TYPE helper_web_research_blocked_fetch_total gauge");
        sb.AppendLine($"helper_web_research_blocked_fetch_total {webResearchMetrics.TotalBlockedFetches}");

        sb.AppendLine("# HELP helper_web_research_blocked_fetch_rate Average blocked fetch count per tracked turn.");
        sb.AppendLine("# TYPE helper_web_research_blocked_fetch_rate gauge");
        sb.AppendLine($"helper_web_research_blocked_fetch_rate {webResearchMetrics.BlockedFetchRate:F4}");

        sb.AppendLine("# HELP helper_web_research_stale_disclosure_total Total tracked turns that disclosed stale web evidence.");
        sb.AppendLine("# TYPE helper_web_research_stale_disclosure_total gauge");
        sb.AppendLine($"helper_web_research_stale_disclosure_total {webResearchMetrics.StaleDisclosureTurns}");

        sb.AppendLine("# HELP helper_web_research_stale_disclosure_rate Share of tracked turns that disclosed stale web evidence.");
        sb.AppendLine("# TYPE helper_web_research_stale_disclosure_rate gauge");
        sb.AppendLine($"helper_web_research_stale_disclosure_rate {webResearchMetrics.StaleDisclosureRate:F4}");

        sb.AppendLine("# HELP helper_response_repeated_phrase_rate Share of turns that reuse a previously seen leading phrase.");
        sb.AppendLine("# TYPE helper_response_repeated_phrase_rate gauge");
        sb.AppendLine($"helper_response_repeated_phrase_rate {conversationMetrics.Style.RepeatedPhraseRate:F4}");

        sb.AppendLine("# HELP helper_mixed_language_turn_rate Share of turns with mixed or mismatched language.");
        sb.AppendLine("# TYPE helper_mixed_language_turn_rate gauge");
        sb.AppendLine($"helper_mixed_language_turn_rate {conversationMetrics.Style.MixedLanguageTurnRate:F4}");

        sb.AppendLine("# HELP helper_generic_clarification_rate Share of turns that used a generic clarification prompt.");
        sb.AppendLine("# TYPE helper_generic_clarification_rate gauge");
        sb.AppendLine($"helper_generic_clarification_rate {conversationMetrics.Style.GenericClarificationRate:F4}");

        sb.AppendLine("# HELP helper_generic_next_step_rate Share of turns that used a generic next-step CTA.");
        sb.AppendLine("# TYPE helper_generic_next_step_rate gauge");
        sb.AppendLine($"helper_generic_next_step_rate {conversationMetrics.Style.GenericNextStepRate:F4}");

        sb.AppendLine("# HELP helper_memory_ack_template_rate Share of remember turns that used a canned acknowledgement template.");
        sb.AppendLine("# TYPE helper_memory_ack_template_rate gauge");
        sb.AppendLine($"helper_memory_ack_template_rate {conversationMetrics.Style.MemoryAckTemplateRate:F4}");

        sb.AppendLine("# HELP helper_source_reuse_dominance Dominance of the most reused source set across sourced turns.");
        sb.AppendLine("# TYPE helper_source_reuse_dominance gauge");
        sb.AppendLine($"helper_source_reuse_dominance {conversationMetrics.Style.SourceReuseDominance:F4}");

        sb.AppendLine("# HELP helper_clarification_helpfulness_rate Share of clarification turns that converted into a useful follow-up or positive rating.");
        sb.AppendLine("# TYPE helper_clarification_helpfulness_rate gauge");
        sb.AppendLine($"helper_clarification_helpfulness_rate {humanLikeConversationMetrics.Summary.ClarificationHelpfulnessRate:F4}");

        sb.AppendLine("# HELP helper_repair_success_rate Share of repair attempts that completed successfully.");
        sb.AppendLine("# TYPE helper_repair_success_rate gauge");
        sb.AppendLine($"helper_repair_success_rate {humanLikeConversationMetrics.Summary.RepairSuccessRate:F4}");

        sb.AppendLine("# HELP helper_style_feedback_average_rating Average user style feedback rating for assistant turns.");
        sb.AppendLine("# TYPE helper_style_feedback_average_rating gauge");
        sb.AppendLine($"helper_style_feedback_average_rating {humanLikeConversationMetrics.Summary.StyleFeedbackAverageRating:F2}");

        sb.AppendLine("# HELP helper_style_low_rating_rate Share of feedback votes rated 1-2.");
        sb.AppendLine("# TYPE helper_style_low_rating_rate gauge");
        sb.AppendLine($"helper_style_low_rating_rate {humanLikeConversationMetrics.Summary.StyleLowRatingRate:F4}");

        sb.AppendLine("# HELP helper_reasoning_turns_total Total turns that used a reasoning-aware verifier or branch path.");
        sb.AppendLine("# TYPE helper_reasoning_turns_total gauge");
        sb.AppendLine($"helper_reasoning_turns_total {conversationMetrics.Reasoning.Turns}");

        sb.AppendLine("# HELP helper_reasoning_branching_turns_total Total turns that used branch-and-verify.");
        sb.AppendLine("# TYPE helper_reasoning_branching_turns_total gauge");
        sb.AppendLine($"helper_reasoning_branching_turns_total {conversationMetrics.Reasoning.BranchingTurns}");

        sb.AppendLine("# HELP helper_reasoning_branching_rate Share of all turns that used branch-and-verify.");
        sb.AppendLine("# TYPE helper_reasoning_branching_rate gauge");
        sb.AppendLine($"helper_reasoning_branching_rate {conversationMetrics.Reasoning.BranchingRate:F4}");

        sb.AppendLine("# HELP helper_reasoning_avg_branches_explored Average branches explored on reasoning turns.");
        sb.AppendLine("# TYPE helper_reasoning_avg_branches_explored gauge");
        sb.AppendLine($"helper_reasoning_avg_branches_explored {conversationMetrics.Reasoning.AvgBranchesExplored:F2}");

        sb.AppendLine("# HELP helper_reasoning_avg_candidates_rejected Average rejected candidates on reasoning turns.");
        sb.AppendLine("# TYPE helper_reasoning_avg_candidates_rejected gauge");
        sb.AppendLine($"helper_reasoning_avg_candidates_rejected {conversationMetrics.Reasoning.AvgCandidatesRejected:F2}");

        sb.AppendLine("# HELP helper_reasoning_local_verification_checks_total Total local verification checks across reasoning turns.");
        sb.AppendLine("# TYPE helper_reasoning_local_verification_checks_total gauge");
        sb.AppendLine($"helper_reasoning_local_verification_checks_total {conversationMetrics.Reasoning.LocalVerificationChecks}");

        sb.AppendLine("# HELP helper_reasoning_local_verification_passes_total Total passed local verification checks across reasoning turns.");
        sb.AppendLine("# TYPE helper_reasoning_local_verification_passes_total gauge");
        sb.AppendLine($"helper_reasoning_local_verification_passes_total {conversationMetrics.Reasoning.LocalVerificationPasses}");

        sb.AppendLine("# HELP helper_reasoning_local_verification_rejects_total Total rejected local verification checks across reasoning turns.");
        sb.AppendLine("# TYPE helper_reasoning_local_verification_rejects_total gauge");
        sb.AppendLine($"helper_reasoning_local_verification_rejects_total {conversationMetrics.Reasoning.LocalVerificationRejects}");

        sb.AppendLine("# HELP helper_reasoning_local_verification_pass_rate Average pass rate of local verification checks.");
        sb.AppendLine("# TYPE helper_reasoning_local_verification_pass_rate gauge");
        sb.AppendLine($"helper_reasoning_local_verification_pass_rate {conversationMetrics.Reasoning.LocalVerificationPassRate:F4}");

        sb.AppendLine("# HELP helper_reasoning_avg_model_calls_used Average model calls used on reasoning turns.");
        sb.AppendLine("# TYPE helper_reasoning_avg_model_calls_used gauge");
        sb.AppendLine($"helper_reasoning_avg_model_calls_used {conversationMetrics.Reasoning.AvgModelCallsUsed:F2}");

        sb.AppendLine("# HELP helper_reasoning_avg_retrieval_chunks_used Average retrieval chunks used on reasoning turns.");
        sb.AppendLine("# TYPE helper_reasoning_avg_retrieval_chunks_used gauge");
        sb.AppendLine($"helper_reasoning_avg_retrieval_chunks_used {conversationMetrics.Reasoning.AvgRetrievalChunksUsed:F2}");

        sb.AppendLine("# HELP helper_reasoning_avg_procedural_lessons_used Average procedural lessons used on reasoning turns.");
        sb.AppendLine("# TYPE helper_reasoning_avg_procedural_lessons_used gauge");
        sb.AppendLine($"helper_reasoning_avg_procedural_lessons_used {conversationMetrics.Reasoning.AvgProceduralLessonsUsed:F2}");

        sb.AppendLine("# HELP helper_reasoning_avg_approximate_token_cost Average approximate token cost on reasoning turns.");
        sb.AppendLine("# TYPE helper_reasoning_avg_approximate_token_cost gauge");
        sb.AppendLine($"helper_reasoning_avg_approximate_token_cost {conversationMetrics.Reasoning.AvgApproximateTokenCost:F2}");

        sb.AppendLine("# HELP helper_post_turn_audit_pending Pending async post-turn audit items.");
        sb.AppendLine("# TYPE helper_post_turn_audit_pending gauge");
        sb.AppendLine($"helper_post_turn_audit_pending {postTurnAuditMetrics.Pending}");

        sb.AppendLine("# HELP helper_post_turn_audit_failed Total failed async post-turn audits.");
        sb.AppendLine("# TYPE helper_post_turn_audit_failed gauge");
        sb.AppendLine($"helper_post_turn_audit_failed {postTurnAuditMetrics.Failed}");

        sb.AppendLine("# HELP helper_post_turn_audit_dead_lettered Total dead-lettered async post-turn audits.");
        sb.AppendLine("# TYPE helper_post_turn_audit_dead_lettered gauge");
        sb.AppendLine($"helper_post_turn_audit_dead_lettered {postTurnAuditMetrics.DeadLettered}");

        if (controlPlaneMetrics.Readiness.TimeToListeningMs.HasValue)
        {
            sb.AppendLine("# HELP helper_startup_listening_ms Time from process start to listening.");
            sb.AppendLine("# TYPE helper_startup_listening_ms gauge");
            sb.AppendLine($"helper_startup_listening_ms {controlPlaneMetrics.Readiness.TimeToListeningMs.Value}");
        }

        if (controlPlaneMetrics.Readiness.TimeToReadyMs.HasValue)
        {
            sb.AppendLine("# HELP helper_startup_ready_ms Time from process start to minimal ready.");
            sb.AppendLine("# TYPE helper_startup_ready_ms gauge");
            sb.AppendLine($"helper_startup_ready_ms {controlPlaneMetrics.Readiness.TimeToReadyMs.Value}");
        }

        if (controlPlaneMetrics.Readiness.TimeToWarmReadyMs.HasValue)
        {
            sb.AppendLine("# HELP helper_startup_warm_ready_ms Time from process start to warm ready.");
            sb.AppendLine("# TYPE helper_startup_warm_ready_ms gauge");
            sb.AppendLine($"helper_startup_warm_ready_ms {controlPlaneMetrics.Readiness.TimeToWarmReadyMs.Value}");
        }

        sb.AppendLine("# HELP helper_control_plane_alerts_total Total active control-plane alerts.");
        sb.AppendLine("# TYPE helper_control_plane_alerts_total gauge");
        sb.AppendLine($"helper_control_plane_alerts_total {controlPlaneMetrics.Alerts.Count}");

        sb.AppendLine("# HELP helper_model_pool_inflight Current in-flight model calls by execution pool.");
        sb.AppendLine("# TYPE helper_model_pool_inflight gauge");
        foreach (var pool in controlPlaneMetrics.ModelGateway.Pools)
        {
            var label = EscapeLabel(pool.Pool);
            sb.AppendLine($"helper_model_pool_inflight{{pool=\"{label}\"}} {pool.InFlight}");
        }

        sb.AppendLine("# HELP helper_model_pool_calls_total Total model calls by execution pool.");
        sb.AppendLine("# TYPE helper_model_pool_calls_total gauge");
        foreach (var pool in controlPlaneMetrics.ModelGateway.Pools)
        {
            var label = EscapeLabel(pool.Pool);
            sb.AppendLine($"helper_model_pool_calls_total{{pool=\"{label}\"}} {pool.TotalCalls}");
        }

        sb.AppendLine("# HELP helper_model_pool_failures_total Failed model calls by execution pool.");
        sb.AppendLine("# TYPE helper_model_pool_failures_total gauge");
        foreach (var pool in controlPlaneMetrics.ModelGateway.Pools)
        {
            var label = EscapeLabel(pool.Pool);
            sb.AppendLine($"helper_model_pool_failures_total{{pool=\"{label}\"}} {pool.FailedCalls}");
        }

        sb.AppendLine("# HELP helper_model_pool_timeouts_total Timed out model calls by execution pool.");
        sb.AppendLine("# TYPE helper_model_pool_timeouts_total gauge");
        foreach (var pool in controlPlaneMetrics.ModelGateway.Pools)
        {
            var label = EscapeLabel(pool.Pool);
            sb.AppendLine($"helper_model_pool_timeouts_total{{pool=\"{label}\"}} {pool.TimeoutCalls}");
        }

        sb.AppendLine("# HELP helper_model_pool_avg_latency_ms Average model latency by execution pool.");
        sb.AppendLine("# TYPE helper_model_pool_avg_latency_ms gauge");
        foreach (var pool in controlPlaneMetrics.ModelGateway.Pools)
        {
            var label = EscapeLabel(pool.Pool);
            sb.AppendLine($"helper_model_pool_avg_latency_ms{{pool=\"{label}\"}} {pool.AvgLatencyMs:F2}");
        }

        sb.AppendLine("# HELP helper_persistence_queue_pending Pending dirty conversation notifications.");
        sb.AppendLine("# TYPE helper_persistence_queue_pending gauge");
        sb.AppendLine($"helper_persistence_queue_pending {controlPlaneMetrics.PersistenceQueue.Pending}");

        sb.AppendLine("# HELP helper_persistence_queue_dropped Total dropped dirty conversation notifications.");
        sb.AppendLine("# TYPE helper_persistence_queue_dropped gauge");
        sb.AppendLine($"helper_persistence_queue_dropped {controlPlaneMetrics.PersistenceQueue.Dropped}");

        sb.AppendLine("# HELP helper_persistence_pending_dirty_conversations Pending dirty conversations awaiting flush.");
        sb.AppendLine("# TYPE helper_persistence_pending_dirty_conversations gauge");
        sb.AppendLine($"helper_persistence_pending_dirty_conversations {controlPlaneMetrics.Persistence.PendingDirtyConversations}");

        sb.AppendLine("# HELP generation_runs_total Total generation runs.");
        sb.AppendLine("# TYPE generation_runs_total gauge");
        sb.AppendLine($"generation_runs_total {generationMetrics.GenerationRunsTotal}");

        sb.AppendLine("# HELP generation_validation_fail_total Total blocked runs due to validation failures.");
        sb.AppendLine("# TYPE generation_validation_fail_total gauge");
        sb.AppendLine($"generation_validation_fail_total {generationMetrics.GenerationValidationFailTotal}");

        sb.AppendLine("# HELP generation_compile_fail_total Total runs failed in compile gate.");
        sb.AppendLine("# TYPE generation_compile_fail_total gauge");
        sb.AppendLine($"generation_compile_fail_total {generationMetrics.GenerationCompileFailTotal}");

        sb.AppendLine("# HELP generation_promoted_total Total successful promotions from validated zone.");
        sb.AppendLine("# TYPE generation_promoted_total gauge");
        sb.AppendLine($"generation_promoted_total {generationMetrics.GenerationPromotedTotal}");

        sb.AppendLine("# HELP generation_golden_template_hit_total Total generation requests routed to a golden template.");
        sb.AppendLine("# TYPE generation_golden_template_hit_total gauge");
        sb.AppendLine($"generation_golden_template_hit_total {generationMetrics.GenerationGoldenTemplateHitTotal}");

        sb.AppendLine("# HELP generation_golden_template_miss_total Total generation requests that did not match a golden template.");
        sb.AppendLine("# TYPE generation_golden_template_miss_total gauge");
        sb.AppendLine($"generation_golden_template_miss_total {generationMetrics.GenerationGoldenTemplateMissTotal}");

        sb.AppendLine("# HELP generation_timeout_routing_total Total generation timeouts during routing stage.");
        sb.AppendLine("# TYPE generation_timeout_routing_total gauge");
        sb.AppendLine($"generation_timeout_routing_total {generationMetrics.GenerationTimeoutRoutingTotal}");

        sb.AppendLine("# HELP generation_timeout_forge_total Total generation timeouts during forge stage.");
        sb.AppendLine("# TYPE generation_timeout_forge_total gauge");
        sb.AppendLine($"generation_timeout_forge_total {generationMetrics.GenerationTimeoutForgeTotal}");

        sb.AppendLine("# HELP generation_timeout_synthesis_total Total generation timeouts during synthesis stage.");
        sb.AppendLine("# TYPE generation_timeout_synthesis_total gauge");
        sb.AppendLine($"generation_timeout_synthesis_total {generationMetrics.GenerationTimeoutSynthesisTotal}");

        sb.AppendLine("# HELP generation_timeout_autofix_total Total generation timeouts during autofix stage.");
        sb.AppendLine("# TYPE generation_timeout_autofix_total gauge");
        sb.AppendLine($"generation_timeout_autofix_total {generationMetrics.GenerationTimeoutAutofixTotal}");

        sb.AppendLine("# HELP generation_timeout_unknown_total Total generation timeouts with unknown stage.");
        sb.AppendLine("# TYPE generation_timeout_unknown_total gauge");
        sb.AppendLine($"generation_timeout_unknown_total {generationMetrics.GenerationTimeoutUnknownTotal}");

        sb.AppendLine("# HELP generation_autofix_attempts_total Total generation autofix attempts.");
        sb.AppendLine("# TYPE generation_autofix_attempts_total gauge");
        sb.AppendLine($"generation_autofix_attempts_total {generationMetrics.GenerationAutofixAttemptsTotal}");

        sb.AppendLine("# HELP generation_autofix_success_total Total successful generation autofix attempts.");
        sb.AppendLine("# TYPE generation_autofix_success_total gauge");
        sb.AppendLine($"generation_autofix_success_total {generationMetrics.GenerationAutofixSuccessTotal}");

        sb.AppendLine("# HELP generation_autofix_fail_total Total failed generation autofix attempts.");
        sb.AppendLine("# TYPE generation_autofix_fail_total gauge");
        sb.AppendLine($"generation_autofix_fail_total {generationMetrics.GenerationAutofixFailTotal}");

        sb.AppendLine("# HELP generation_template_promotion_attempt_total Total template runtime promotion attempts.");
        sb.AppendLine("# TYPE generation_template_promotion_attempt_total gauge");
        sb.AppendLine($"generation_template_promotion_attempt_total {generationMetrics.TemplatePromotionAttemptTotal}");

        sb.AppendLine("# HELP generation_template_promotion_success_total Total successful template runtime promotions.");
        sb.AppendLine("# TYPE generation_template_promotion_success_total gauge");
        sb.AppendLine($"generation_template_promotion_success_total {generationMetrics.TemplatePromotionSuccessTotal}");

        sb.AppendLine("# HELP generation_template_promotion_fail_total Total failed template runtime promotions.");
        sb.AppendLine("# TYPE generation_template_promotion_fail_total gauge");
        sb.AppendLine($"generation_template_promotion_fail_total {generationMetrics.TemplatePromotionFailTotal}");

        sb.AppendLine("# HELP generation_template_promotion_format_fix_applied_total Total formatting drifts auto-fixed during promotion.");
        sb.AppendLine("# TYPE generation_template_promotion_format_fix_applied_total gauge");
        sb.AppendLine($"generation_template_promotion_format_fix_applied_total {generationMetrics.TemplatePromotionFormatFixAppliedTotal}");

        sb.AppendLine("# HELP generation_template_promotion_format_still_failing_total Total formatting drifts that remained failing after auto-fix.");
        sb.AppendLine("# TYPE generation_template_promotion_format_still_failing_total gauge");
        sb.AppendLine($"generation_template_promotion_format_still_failing_total {generationMetrics.TemplatePromotionFormatStillFailingTotal}");

        sb.AppendLine("# HELP generation_template_promotion_fail_reason_total Failed template promotions grouped by reason.");
        sb.AppendLine("# TYPE generation_template_promotion_fail_reason_total gauge");
        foreach (var reason in generationMetrics.TemplatePromotionFailReasonTotals.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var label = EscapeLabel(reason.Key);
            sb.AppendLine($"generation_template_promotion_fail_reason_total{{reason=\"{label}\"}} {reason.Value}");
        }

        return sb.ToString();
    }

    private static string EscapeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static int GetIntentCount(IntentTelemetrySnapshot intentMetrics, string intentName)
    {
        return intentMetrics.Intents
            .Where(bucket => string.Equals(bucket.Name, intentName, StringComparison.OrdinalIgnoreCase))
            .Select(bucket => bucket.Count)
            .DefaultIfEmpty(0)
            .Sum();
    }
}

