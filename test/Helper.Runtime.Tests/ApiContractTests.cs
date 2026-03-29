using Helper.Api.Hosting;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Helper.Runtime.Tests;

public class ApiContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void OpenApiDocument_ContainsCorePaths()
    {
        var doc = OpenApiDocumentFactory.Create();
        var json = JsonSerializer.Serialize(doc);

        Assert.Contains("/api/chat", json);
        Assert.Contains("/api/chat/{conversationId}/resume", json);
        Assert.Contains("/api/chat/{conversationId}/turns/{turnId}/regenerate", json);
        Assert.Contains("/api/chat/{conversationId}/branches", json);
        Assert.Contains("/api/chat/{conversationId}/branches/compare", json);
        Assert.Contains("/api/chat/{conversationId}/branches/merge", json);
        Assert.Contains("/api/chat/{conversationId}/repair", json);
        Assert.Contains("/api/chat/{conversationId}/feedback", json);
        Assert.Contains("/api/auth/session", json);
        Assert.Contains("/api/auth/keys", json);
        Assert.Contains("/api/auth/keys/rotate", json);
        Assert.Contains("/api/auth/keys/{keyId}/revoke", json);
        Assert.Contains("/api/helper/generate", json);
        Assert.Contains("/api/helper/research", json);
        Assert.Contains("/api/metrics", json);
        Assert.Contains("/api/metrics/web-research", json);
        Assert.Contains("/api/metrics/human-like-conversation", json);
        Assert.Contains("/api/control-plane", json);
        Assert.Contains("/api/metrics/tool-audit-consistency", json);
        Assert.Contains("/api/metrics/prometheus", json);
        Assert.Contains("/api/metrics/parity-gate", json);
        Assert.Contains("/api/metrics/parity-window-gate", json);
        Assert.Contains("/api/metrics/parity-benchmark", json);
        Assert.Contains("/api/templates", json);
        Assert.Contains("/api/templates/promotion-profile", json);
        Assert.Contains("/api/templates/{templateId}/versions", json);
        Assert.Contains("/api/templates/{templateId}/activate/{version}", json);
        Assert.Contains("/api/templates/{templateId}/rollback", json);
        Assert.Contains("/api/templates/{templateId}/certify/{version}", json);
        Assert.Contains("/api/templates/certification-gate", json);
        Assert.Contains("X-API-KEY", json);
        Assert.Contains("bearer", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void OpenApiDocument_Exposes_HelperOnly_Routes()
    {
        var doc = OpenApiDocumentFactory.Create();
        var json = JsonSerializer.Serialize(doc);

        Assert.Contains("/api/helper/generate", json);
        Assert.Contains("/api/helper/research", json);
        Assert.DoesNotContain("/api/gen" + "esis/generate", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/gen" + "esis/research", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"deprecated\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void ExtractApiKey_ReadsHeaderAndQuery()
    {
        var headerContext = new DefaultHttpContext();
        headerContext.Request.Headers["X-API-KEY"] = "header-key";
        Assert.Equal("header-key", ApiProgramHelpers.ExtractApiKey(headerContext));

        var queryContext = new DefaultHttpContext();
        queryContext.Request.QueryString = new QueryString("?access_token=query-key");
        Assert.Equal("query-key", ApiProgramHelpers.ExtractApiKey(queryContext));
    }
}

