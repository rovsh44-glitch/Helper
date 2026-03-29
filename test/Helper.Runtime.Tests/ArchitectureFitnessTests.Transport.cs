namespace Helper.Runtime.Tests;

public partial class ArchitectureFitnessTests
{
    [Fact]
    public void InteractivePath_UsesModelGatewayAndWriteBehindBoundaries()
    {
        var executorPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ChatTurnExecutor.cs");
        var storePath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "InMemoryConversationStore.cs");
        var executorText = File.ReadAllText(executorPath);
        var storeText = File.ReadAllText(storePath);

        Assert.Contains("IModelGateway", executorText, StringComparison.Ordinal);
        Assert.Contains("IConversationWriteBehindQueue", storeText, StringComparison.Ordinal);
    }

    [Fact]
    public void TransportLayer_DoesNotConstructTurnExecutionComponentsDirectly()
    {
        var transportPath = ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "EndpointRegistrationExtensions.Conversation.cs");
        var transportText = File.ReadAllText(transportPath);

        Assert.DoesNotContain("new ChatTurnExecutor", transportText, StringComparison.Ordinal);
        Assert.DoesNotContain("new ChatTurnPlanner", transportText, StringComparison.Ordinal);
        Assert.DoesNotContain("new TurnOrchestrationEngine", transportText, StringComparison.Ordinal);
        Assert.DoesNotContain("AILink", transportText, StringComparison.Ordinal);
        Assert.DoesNotContain("IPostTurnAuditQueue", transportText, StringComparison.Ordinal);
        Assert.DoesNotContain("EnqueuePostTurnAudit", transportText, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientFacingContracts_DoNotExposePersistentSecrets()
    {
        var contractsPath = ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "ApiContracts.cs");
        var contracts = File.ReadAllText(contractsPath);

        Assert.DoesNotContain("SessionSigningKey", contracts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HELPER_API_KEY", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("SigningKey", contracts, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConversationRuntime_DoesNotKeepDuplicateLegacyControlPlane()
    {
        var orchestratorPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ChatOrchestrator.cs");
        var orchestrator = File.ReadAllText(orchestratorPath);
        var legacyPath = ResolveWorkspaceFile("src", "Helper.Api", "Backend", "Application", "ChatCommandOrchestrator.cs");

        Assert.DoesNotContain("LegacyChatOrchestrator", orchestrator, StringComparison.Ordinal);
        Assert.False(File.Exists(legacyPath), "Legacy ChatCommandOrchestrator tombstone should be removed from src.");
    }

    [Fact]
    public void ChatTurnExecutor_RemainsThinCoordinator()
    {
        var executorPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ChatTurnExecutor.cs");
        var executor = File.ReadAllText(executorPath);
        var lineCount = File.ReadAllLines(executorPath).Length;

        Assert.True(lineCount <= 140, $"ChatTurnExecutor should stay thin, actual lines: {lineCount}.");
        Assert.Contains("ChatTurnImmediateService", executor, StringComparison.Ordinal);
        Assert.Contains("ChatTurnAnswerService", executor, StringComparison.Ordinal);
        Assert.DoesNotContain("TryApplyDeterministicMemoryCapture", executor, StringComparison.Ordinal);
        Assert.DoesNotContain("StreamThroughGatewayAsync", executor, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversationTransport_IsSplitIntoFocusedPartialFiles()
    {
        var conversationRoot = ResolveWorkspaceFile("src", "Helper.Api", "Hosting");
        var partials = Directory.GetFiles(conversationRoot, "EndpointRegistrationExtensions.Conversation*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rootPath = ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "EndpointRegistrationExtensions.Conversation.cs");
        var rootLines = File.ReadAllLines(rootPath).Length;
        var oversized = Directory.GetFiles(conversationRoot, "EndpointRegistrationExtensions.Conversation*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => new { Path = path, Lines = File.ReadAllLines(path).Length })
            .Where(x => x.Lines > 400)
            .ToArray();

        Assert.True(partials.Length >= 4, "Conversation transport should be split across multiple partial files.");
        Assert.True(rootLines <= 40, $"Conversation entrypoint should stay thin, actual lines: {rootLines}.");
        Assert.Empty(oversized);
    }

    [Fact]
    public void EvolutionAndGenerationTransport_IsSplitIntoBoundedFiles()
    {
        var hostingRoot = ResolveWorkspaceFile("src", "Helper.Api", "Hosting");
        var partials = Directory.GetFiles(hostingRoot, "EndpointRegistrationExtensions.Evolution*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rootPath = ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "EndpointRegistrationExtensions.EvolutionAndGeneration.cs");
        var rootLines = File.ReadAllLines(rootPath).Length;
        var oversized = Directory.GetFiles(hostingRoot, "EndpointRegistrationExtensions.Evolution*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => new { Path = path, Lines = File.ReadAllLines(path).Length })
            .Where(x => x.Lines > 320)
            .ToArray();
        var openApiPath = ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "OpenApiDocumentFactory.cs");
        var openApi = File.ReadAllText(openApiPath);
        var researchEndpointsPath = ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "EndpointRegistrationExtensions.Evolution.Research.cs");
        var researchEndpoints = File.ReadAllText(researchEndpointsPath);

        Assert.True(partials.Length >= 5, "Evolution/generation transport should be split across bounded files.");
        Assert.True(rootLines <= 40, $"Evolution/generation entrypoint should stay thin, actual lines: {rootLines}.");
        Assert.Empty(oversized);

        if (researchEndpoints.Contains("\"/api/research\"", StringComparison.Ordinal))
        {
            Assert.Contains("deprecated = true", openApi, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("[\"/api/research\"]", openApi, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EndpointRegistrationFiles_StayWithin_Configured_Budgets_And_Route_Families()
    {
        var hostingRoot = ResolveWorkspaceFile("src", "Helper.Api", "Hosting");
        var maxMappingsPerFile = ReadIntBudget("architecture", "endpointRegistration", "maxMappingsPerFile");
        var conversationAllowedPrefixes = ReadStringArrayBudget("architecture", "endpointRegistration", "conversationAllowedPrefixes");
        var conversationFiles = Directory.GetFiles(hostingRoot, "EndpointRegistrationExtensions.Conversation*.cs", SearchOption.TopDirectoryOnly);
        var evolutionFiles = Directory.GetFiles(hostingRoot, "EndpointRegistrationExtensions.Evolution*.cs", SearchOption.TopDirectoryOnly);

        Assert.All(conversationFiles, file =>
        {
            var routes = ExtractMappedRoutes(file);
            Assert.True(routes.Count <= maxMappingsPerFile, $"{Path.GetFileName(file)} exceeded mapping budget: {routes.Count} > {maxMappingsPerFile}.");
            Assert.All(routes, route =>
                Assert.True(conversationAllowedPrefixes.Any(prefix => route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
                    $"{Path.GetFileName(file)} contains route outside conversation prefixes: {route}"));
        });

        Assert.All(evolutionFiles, file =>
        {
            var routes = ExtractMappedRoutes(file);
            Assert.True(routes.Count <= maxMappingsPerFile, $"{Path.GetFileName(file)} exceeded mapping budget: {routes.Count} > {maxMappingsPerFile}.");
            if (routes.Count == 0)
            {
                return;
            }

            Assert.True(
                TryReadStringArrayBudget(out var allowedPrefixes, "architecture", "endpointRegistration", "evolutionAllowedPrefixesByFile", Path.GetFileName(file)),
                $"No allowed route family budget configured for {Path.GetFileName(file)}.");

            Assert.All(routes, route =>
                Assert.True(allowedPrefixes.Any(prefix => route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
                    $"{Path.GetFileName(file)} contains route outside configured families: {route}"));
        });
    }
}
