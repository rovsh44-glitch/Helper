using System.Text;
using System.Text.Json;
using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Helper.Runtime.Tests;

public sealed class ConversationPreferencesEndpointRouteTests
{
    [Fact]
    public async Task PreferencesEndpoint_Binds_CamelCase_And_Preserves_Omitted_Clearable_ProjectId()
    {
        using var harness = CreateHarness();
        var state = harness.Store.GetOrCreate("conv-preferences-preserve");
        state.SearchLocalityHint = "berlin";
        state.ProjectContext = new ProjectContextState(
            "helper-public",
            "Helper Public",
            "Keep public contract honest.",
            MemoryEnabled: true,
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);

        var result = await harness.InvokeAsync(state.Id, """
            {
              "projectLabel": "Helper Public v2",
              "projectInstructions": "Keep public contract honest.",
              "backgroundResearchEnabled": false
            }
            """);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(state.ProjectContext);
        Assert.Equal("helper-public", state.ProjectContext?.ProjectId);
        Assert.Equal("Helper Public v2", state.ProjectContext?.Label);
        Assert.Equal("Keep public contract honest.", state.ProjectContext?.Instructions);
        Assert.False(state.BackgroundResearchEnabled);
        Assert.True(ReadBooleanProperty(result.Body, "success"));
    }

    [Fact]
    public async Task PreferencesEndpoint_Clears_ProjectContext_And_SearchLocality_When_Null_Is_Explicit()
    {
        using var harness = CreateHarness();
        var state = harness.Store.GetOrCreate("conv-preferences-clear");
        state.SearchLocalityHint = "berlin";
        state.ProjectContext = new ProjectContextState(
            "helper-public",
            "Helper Public",
            "Keep public contract honest.",
            MemoryEnabled: true,
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);

        var result = await harness.InvokeAsync(state.Id, """
            {
              "projectId": null,
              "searchLocalityHint": null
            }
            """);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Null(state.SearchLocalityHint);
        Assert.Null(state.ProjectContext);
        Assert.True(ReadBooleanProperty(result.Body, "success"));
    }

    [Fact]
    public async Task PreferencesEndpoint_Returns_BadRequest_For_TopLevel_Null_Payload()
    {
        using var harness = CreateHarness();
        var state = harness.Store.GetOrCreate("conv-preferences-invalid");
        state.SearchLocalityHint = "berlin";

        var result = await harness.InvokeAsync(state.Id, "null");

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("Invalid preferences payload.", result.Body, StringComparison.Ordinal);
        Assert.Equal("berlin", state.SearchLocalityHint);
    }

    private static PreferenceEndpointHarness CreateHarness()
    {
        var store = new InMemoryConversationStore(
            new MemoryPolicyService(),
            new ConversationSummarizer(),
            persistenceEngine: null,
            writeBehindQueue: null);

        return new PreferenceEndpointHarness(
            store,
            new UserProfileService(),
            new MemoryPolicyService(),
            new AlwaysEnabledFeatureFlags());
    }

    private sealed class PreferenceEndpointHarness : IDisposable
    {
        private readonly IUserProfileService _userProfile;
        private readonly IMemoryPolicyService _memoryPolicy;
        private readonly IFeatureFlags _flags;
        private readonly ServiceProvider _serviceProvider;

        public PreferenceEndpointHarness(
            InMemoryConversationStore store,
            IUserProfileService userProfile,
            IMemoryPolicyService memoryPolicy,
            IFeatureFlags flags)
        {
            Store = store;
            _userProfile = userProfile;
            _memoryPolicy = memoryPolicy;
            _flags = flags;
            _serviceProvider = new ServiceCollection()
                .AddLogging()
                .AddOptions()
                .BuildServiceProvider();
        }

        public InMemoryConversationStore Store { get; }

        public async Task<(int StatusCode, string Body)> InvokeAsync(string conversationId, string requestBody)
        {
            var context = new DefaultHttpContext
            {
                RequestServices = _serviceProvider
            };
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = $"/api/chat/{conversationId}/preferences";
            context.Request.ContentType = "application/json";
            context.Request.RouteValues["conversationId"] = conversationId;
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
            context.Response.Body = new MemoryStream();

            var result = await ConversationPreferenceEndpointHandler.HandleAsync(
                conversationId,
                context.Request,
                Store,
                _userProfile,
                _memoryPolicy,
                _flags,
                CancellationToken.None);

            await result.ExecuteAsync(context);

            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
            return (context.Response.StatusCode, await reader.ReadToEndAsync());
        }

        public void Dispose()
        {
            Store.Dispose();
            _serviceProvider.Dispose();
        }
    }

    private static bool ReadBooleanProperty(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(propertyName).GetBoolean();
    }

    private sealed class AlwaysEnabledFeatureFlags : IFeatureFlags
    {
        public bool AttachmentsEnabled => true;
        public bool RegenerateEnabled => true;
        public bool BranchingEnabled => true;
        public bool BranchMergeEnabled => true;
        public bool ConversationRepairEnabled => true;
        public bool EnhancedGroundingEnabled => true;
        public bool IntentV2Enabled => true;
        public bool GroundingV2Enabled => true;
        public bool StreamingV2Enabled => true;
        public bool AuthV2Enabled => true;
        public bool MemoryV2Enabled => true;
    }
}
