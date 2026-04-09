using System.Text.Json;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class ClientContractParityTests
{
    [Fact]
    public void GeneratedClient_Stays_Aligned_With_Public_Continuity_Routes()
    {
        var openApiJson = JsonSerializer.Serialize(OpenApiDocumentFactory.Create());
        var client = TestWorkspaceRoot.ReadAllText("services", "generatedApiClient.ts");

        Assert.Equal(
            openApiJson.Contains("/api/chat/{conversationId}/background/{taskId}/cancel", StringComparison.Ordinal),
            client.Contains("/background/${encodeURIComponent(taskId)}/cancel", StringComparison.Ordinal));
        Assert.Equal(
            openApiJson.Contains("/api/chat/{conversationId}/topics/{topicId}", StringComparison.Ordinal),
            client.Contains("/topics/${encodeURIComponent(topicId)}", StringComparison.Ordinal));

        Assert.DoesNotContain("/voice/session", client, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/chat/{conversationId}/voice/session", openApiJson, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedClient_And_ApiContracts_Expose_The_Same_Advanced_Settings_Surface()
    {
        var contracts = TestWorkspaceRoot.ReadAllText("src", "Helper.Api", "Hosting", "ApiContracts.cs");
        var client = TestWorkspaceRoot.ReadAllText("services", "generatedApiClient.ts");

        AssertAdvancedField("DecisionAssertiveness", "decisionAssertiveness?: string;", contracts, client);
        AssertAdvancedField("ClarificationTolerance", "clarificationTolerance?: string;", contracts, client);
        AssertAdvancedField("CitationPreference", "citationPreference?: string;", contracts, client);
        AssertAdvancedField("RepairStyle", "repairStyle?: string;", contracts, client);
        AssertAdvancedField("ReasoningStyle", "reasoningStyle?: string;", contracts, client);
        AssertAdvancedField("ReasoningEffort", "reasoningEffort?: string;", contracts, client);
        AssertAdvancedField("SearchLocalityHint", "searchLocalityHint?: string | null;", contracts, client);
        AssertAdvancedField("ProjectId", "projectId?: string | null;", contracts, client);
        AssertAdvancedField("ProjectLabel", "projectLabel?: string | null;", contracts, client);
        AssertAdvancedField("ProjectInstructions", "projectInstructions?: string | null;", contracts, client);
        AssertAdvancedField("ProjectMemoryEnabled", "projectMemoryEnabled?: boolean;", contracts, client);
        AssertAdvancedField("BackgroundResearchEnabled", "backgroundResearchEnabled?: boolean;", contracts, client);
        AssertAdvancedField("ProactiveUpdatesEnabled", "proactiveUpdatesEnabled?: boolean;", contracts, client);
        Assert.DoesNotContain("PersonaBundleId", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("personaBundleId", client, StringComparison.Ordinal);
    }

    private static void AssertAdvancedField(string contractsToken, string clientToken, string contracts, string client)
    {
        Assert.Equal(
            contracts.Contains(contractsToken, StringComparison.Ordinal),
            client.Contains(clientToken, StringComparison.Ordinal));
    }
}
