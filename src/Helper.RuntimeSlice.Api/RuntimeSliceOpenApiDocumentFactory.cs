namespace Helper.RuntimeSlice.Api;

internal static class RuntimeSliceOpenApiDocumentFactory
{
    public static object Create()
    {
        return new
        {
            openapi = "3.0.1",
            info = new
            {
                title = "Helper Runtime Review Slice API",
                version = "v1",
                description = "Public-safe read-only runtime review API for HELPER."
            },
            servers = new[]
            {
                new { url = "http://localhost:5076" }
            },
            paths = new Dictionary<string, object>
            {
                ["/api/about"] = new { get = new { summary = "Runtime slice metadata" } },
                ["/api/health"] = new { get = new { summary = "Health status" } },
                ["/api/readiness"] = new { get = new { summary = "Readiness snapshot" } },
                ["/api/openapi.json"] = new { get = new { summary = "OpenAPI contract document" } },
                ["/api/runtime/logs"] = new { get = new { summary = "Sanitized runtime log review snapshot" } },
                ["/api/evolution/status"] = new { get = new { summary = "Evolution status snapshot" } },
                ["/api/evolution/library"] = new { get = new { summary = "Library indexing queue snapshot" } },
                ["/api/telemetry/routes"] = new { get = new { summary = "Route telemetry snapshot" } }
            }
        };
    }
}
