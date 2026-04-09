namespace Helper.Runtime.Tests;

public sealed class ConversationPreferenceEndpointArchitectureTests
{
    [Fact]
    public void ConversationPreferences_ParsePath_Uses_Shared_Payload_Reader()
    {
        var parsePath = TestWorkspaceRoot.ReadAllText("src", "Helper.Api", "Hosting", "EndpointRegistrationExtensions.Conversation.PreferenceParsing.cs");
        var payloadReader = TestWorkspaceRoot.ReadAllText("src", "Helper.Api", "Hosting", "ConversationPreferencePayloadReader.cs");

        Assert.Contains("ConversationPreferencePayloadReader.ReadAsync", parsePath, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Deserialize<ConversationPreferenceDto>", parsePath, StringComparison.Ordinal);
        Assert.Contains("JsonSerializerDefaults.Web", payloadReader, StringComparison.Ordinal);
    }
}
