namespace Helper.Api.Hosting;

public static class OpenApiDocumentFactory
{
    public static object Create()
    {
        return new
        {
            openapi = "3.0.1",
            info = new
            {
                title = "Helper API",
                version = "v1",
                description = "Contract snapshot for Helper backend."
            },
            servers = new[]
            {
                new { url = "http://localhost:5000" }
            },
            security = new object[]
            {
                new
                {
                    ApiKey = Array.Empty<string>()
                },
                new
                {
                    Bearer = Array.Empty<string>()
                }
            },
            components = new
            {
                securitySchemes = new
                {
                    ApiKey = new
                    {
                        type = "apiKey",
                        @in = "header",
                        name = "X-API-KEY"
                    },
                    Bearer = new
                    {
                        type = "http",
                        scheme = "bearer"
                    }
                }
            },
            paths = new Dictionary<string, object>
            {
                ["/api/health"] = new { get = new { summary = "Health status" } },
                ["/api/handshake"] = new { get = new { summary = "Auth handshake" } },
                ["/api/control-plane"] = new { get = new { summary = "Backend control-plane snapshot" } },
                ["/api/capabilities/catalog"] = new { get = new { summary = "Capability catalog snapshot for models, templates, tools, and extensions" } },
                ["/api/runtime/logs"] = new { get = new { summary = "Tail real runtime log sources for machine log review" } },
                ["/api/auth/session"] = new { post = new { summary = "Issue short-lived session token" } },
                ["/api/auth/keys"] = new { get = new { summary = "List machine keys metadata" } },
                ["/api/auth/keys/rotate"] = new { post = new { summary = "Rotate or issue machine key" } },
                ["/api/auth/keys/{keyId}/revoke"] = new { post = new { summary = "Revoke machine key by key id" } },
                ["/api/metrics"] = new { get = new { summary = "Runtime metrics snapshot" } },
                ["/api/metrics/web-research"] = new { get = new { summary = "Web-research telemetry snapshot" } },
                ["/api/metrics/human-like-conversation"] = new { get = new { summary = "Human-like conversation quality dashboard snapshot" } },
                ["/api/metrics/tool-audit-consistency"] = new { get = new { summary = "Compare conversation tool-call counters vs tool audit stream" } },
                ["/api/metrics/prometheus"] = new { get = new { summary = "Prometheus text metrics export" } },
                ["/api/metrics/parity-certification"] = new { post = new { summary = "Generate parity certification snapshot report" } },
                ["/api/metrics/parity-gate"] = new { post = new { summary = "Evaluate parity KPI gate against thresholds" } },
                ["/api/metrics/parity-window-gate"] = new { post = new { summary = "Evaluate rolling parity KPI window gate" } },
                ["/api/metrics/parity-benchmark"] = new { post = new { summary = "Run generation+diagnostics parity benchmark corpora" } },
                ["/api/metrics/closed-loop-predictability"] = new { post = new { summary = "Run closed-loop predictability protocol for top incident classes" } },
                ["/api/openapi.json"] = new { get = new { summary = "OpenAPI contract document" } },
                ["/api/settings/provider-profiles"] = new { get = new { summary = "List provider profiles and activation state" } },
                ["/api/settings/provider-profiles/active"] = new { get = new { summary = "Get active provider profile" } },
                ["/api/settings/provider-profiles/activate"] = new { post = new { summary = "Activate provider profile" } },
                ["/api/settings/provider-profiles/recommend"] = new { post = new { summary = "Recommend provider profile for workload" } },
                ["/api/settings/runtime-doctor/run"] = new { post = new { summary = "Run runtime provider diagnostics" } },
                ["/api/chat"] = new { post = new { summary = "Run chat turn", operationId = "chatCompleteTurn" } },
                ["/api/chat/stream"] = new { post = new { summary = "Run streaming chat turn", operationId = "chatStreamTurn" } },
                ["/api/chat/{conversationId}"] = new
                {
                    get = new { summary = "Get conversation" },
                    delete = new { summary = "Delete conversation" }
                },
                ["/api/chat/{conversationId}/resume"] = new
                {
                    post = new { summary = "Resume unfinished chat turn" }
                },
                ["/api/chat/{conversationId}/stream/resume"] = new
                {
                    post = new { summary = "Resume interrupted streaming turn from cursor offset" }
                },
                ["/api/chat/{conversationId}/turns/{turnId}/regenerate"] = new
                {
                    post = new { summary = "Regenerate assistant response for specific turn" }
                },
                ["/api/chat/{conversationId}/preferences"] = new
                {
                    post = new { summary = "Update conversation memory preferences" }
                },
                ["/api/chat/{conversationId}/background/{taskId}/cancel"] = new
                {
                    post = new { summary = "Cancel queued background follow-through task" }
                },
                ["/api/chat/{conversationId}/topics/{topicId}"] = new
                {
                    post = new { summary = "Enable or disable proactive topic subscription" }
                },
                ["/api/chat/{conversationId}/memory"] = new
                {
                    get = new { summary = "List active conversation memory items and memory policy" }
                },
                ["/api/chat/{conversationId}/memory/{memoryId}"] = new
                {
                    delete = new { summary = "Delete memory item from conversation memory" }
                },
                ["/api/chat/{conversationId}/branches"] = new
                {
                    post = new { summary = "Create branch from turn" }
                },
                ["/api/chat/{conversationId}/branches/{branchId}/activate"] = new
                {
                    post = new { summary = "Activate existing branch" }
                },
                ["/api/chat/{conversationId}/branches/compare"] = new
                {
                    get = new { summary = "Compare branch divergence and provenance" }
                },
                ["/api/chat/{conversationId}/branches/merge"] = new
                {
                    post = new { summary = "Merge one branch into another branch" }
                },
                ["/api/chat/{conversationId}/repair"] = new
                {
                    post = new { summary = "Repair conversation after misunderstanding" }
                },
                ["/api/chat/{conversationId}/feedback"] = new
                {
                    post = new { summary = "Submit user helpfulness feedback" }
                },
                ["/api/helper/generate"] = new { post = new { summary = "Generate project" } },
                ["/api/templates"] = new { get = new { summary = "List available templates" } },
                ["/api/templates/promotion-profile"] = new { get = new { summary = "Resolved template promotion feature profile" } },
                ["/api/templates/{templateId}/versions"] = new { get = new { summary = "List template versions and active status" } },
                ["/api/templates/{templateId}/activate/{version}"] = new { post = new { summary = "Activate specific template version" } },
                ["/api/templates/{templateId}/rollback"] = new { post = new { summary = "Rollback template to previous active version" } },
                ["/api/templates/{templateId}/certify/{version}"] = new { post = new { summary = "Certify template version against compile/artifact/smoke gates" } },
                ["/api/templates/certification-gate"] = new { post = new { summary = "Run certification gate for active template versions" } },
                ["/api/helper/research"] = new { post = new { summary = "Research mode" } },
                ["/api/rag/search"] = new { post = new { summary = "Vector search" } },
                ["/api/rag/ingest"] = new { post = new { summary = "Ingest content into vector store" } },
                ["/api/goals"] = new
                {
                    get = new { summary = "List goals" },
                    post = new { summary = "Add goal" }
                },
                ["/api/build"] = new { post = new { summary = "Build project" } },
                ["/api/fs/write"] = new { post = new { summary = "Write file via guarded API" } },
                ["/api/evolution/status"] = new { get = new { summary = "Evolution status" } },
                ["/api/evolution/library"] = new { get = new { summary = "Library queue snapshot" } },
                ["/api/evolution/start"] = new { post = new { summary = "Start evolution" } },
                ["/api/evolution/pause"] = new { post = new { summary = "Pause evolution" } },
                ["/api/evolution/stop"] = new { post = new { summary = "Stop evolution" } },
                ["/api/evolution/reset"] = new { post = new { summary = "Reset evolution queue" } },
                ["/api/indexing/start"] = new { post = new { summary = "Start indexing" } },
                ["/api/indexing/pause"] = new { post = new { summary = "Pause indexing" } },
                ["/api/indexing/reset"] = new { post = new { summary = "Reset indexing queue" } }
            }
        };
    }
}

