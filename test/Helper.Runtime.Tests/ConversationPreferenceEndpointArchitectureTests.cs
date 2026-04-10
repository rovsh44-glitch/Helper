namespace Helper.Runtime.Tests;

public sealed class ConversationPreferenceEndpointArchitectureTests
{
    [Fact]
    public void ConversationPreferences_ParsePath_Uses_Shared_Payload_Reader()
    {
        var routePath = TestWorkspaceRoot.ReadAllText("src", "Helper.Api", "Hosting", "EndpointRegistrationExtensions.Conversation.StateAndReplay.cs");
        var handlerPath = TestWorkspaceRoot.ReadAllText("src", "Helper.Api", "Hosting", "ConversationPreferenceEndpointHandler.cs");
        var payloadReader = TestWorkspaceRoot.ReadAllText("src", "Helper.Api", "Hosting", "ConversationPreferencePayloadReader.cs");

        Assert.Contains("ConversationPreferenceEndpointHandler.HandleAsync", routePath, StringComparison.Ordinal);
        Assert.Contains("ConversationPreferencePayloadReader.ReadAsync", handlerPath, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Deserialize<ConversationPreferenceDto>", handlerPath, StringComparison.Ordinal);
        Assert.Contains("JsonSerializerDefaults.Web", payloadReader, StringComparison.Ordinal);
    }
}
