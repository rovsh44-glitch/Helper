using System.Text;
using Helper.Api.Hosting;
using Microsoft.AspNetCore.Http;

namespace Helper.Runtime.Tests;

public sealed class ConversationPreferencePayloadReaderTests
{
    [Fact]
    public async Task ReadAsync_Binds_CamelCase_RequestBody_Into_ConversationPreferenceDto()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""
            {
              "searchLocalityHint": "berlin",
              "projectId": "helper-public",
              "projectInstructions": "Keep public contract honest.",
              "backgroundResearchEnabled": false
            }
            """));

        var update = await ConversationPreferencePayloadReader.ReadAsync(context.Request, CancellationToken.None);

        Assert.Equal("berlin", update.Preferences.SearchLocalityHint);
        Assert.Equal("helper-public", update.Preferences.ProjectId);
        Assert.Equal("Keep public contract honest.", update.Preferences.ProjectInstructions);
        Assert.False(update.Preferences.BackgroundResearchEnabled);
        Assert.Contains("searchLocalityHint", update.PresentFields);
        Assert.Contains("projectId", update.PresentFields);
    }

    [Fact]
    public async Task ReadAsync_Preserves_Explicit_Nulls_For_Clearable_Fields()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""
            {
              "searchLocalityHint": null,
              "projectId": null
            }
            """));

        var update = await ConversationPreferencePayloadReader.ReadAsync(context.Request, CancellationToken.None);

        Assert.Null(update.Preferences.SearchLocalityHint);
        Assert.Null(update.Preferences.ProjectId);
        Assert.Contains("searchLocalityHint", update.PresentFields);
        Assert.Contains("projectId", update.PresentFields);
    }
}
