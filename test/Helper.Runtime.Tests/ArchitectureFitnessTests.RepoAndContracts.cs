namespace Helper.Runtime.Tests;

public partial class ArchitectureFitnessTests
{
    private static readonly string[] BlockedBrandTokens =
    {
        "Gem" + "ini",
        "Gem" + "eni",
        "Gen" + "esis"
    };

    [Fact]
    public void SourceTree_DoesNotContainCommentOnlyTombstoneCsFiles()
    {
        var sourceRoot = ResolveWorkspaceFile("src");
        var commentOnlyFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !File.ReadAllLines(file)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Any(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(commentOnlyFiles);
    }

    [Fact]
    public void WorkspaceRoot_And_SourceTree_StayFreeOfAccidentalFolders_And_BuildArtifacts()
    {
        var workspaceRoot = ResolveWorkspaceFile();
        var rootDirectories = Directory.GetDirectories(workspaceRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        var accidentalRootDirectories = rootDirectories
            .Where(name => name!.StartsWith("New folder", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(name, "PROJECTS", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var sourceRoot = ResolveWorkspaceFile("src");
        var sourceArtifacts = Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return string.Equals(name, ".playwright", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        Assert.Empty(accidentalRootDirectories);
        Assert.Empty(sourceArtifacts);
    }

    [Fact]
    public void ProjectReferenceGraph_DoesNotContainCycles()
    {
        var workspaceRoot = ResolveWorkspaceFile();
        var projectFiles = new[]
            {
                Path.Combine(workspaceRoot, "src"),
                Path.Combine(workspaceRoot, "test"),
                Path.Combine(workspaceRoot, "sandbox")
            }
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}temp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(projectFiles);
        Assert.Empty(FindProjectReferenceCycles(projectFiles));
    }

    [Fact]
    public void PublicContractSurfaces_Expose_HelperOnly_Routes_And_Entrypoints()
    {
        var files = new[]
        {
            ResolveWorkspaceFile("src", "Helper.Api", "Program.cs"),
            ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "EndpointRegistrationExtensions.Evolution.Generation.cs"),
            ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "EndpointRegistrationExtensions.Evolution.Research.cs"),
            ResolveWorkspaceFile("src", "Helper.Api", "Hosting", "OpenApiDocumentFactory.cs"),
            ResolveWorkspaceFile("doc", "openapi_contract_snapshot.json"),
            ResolveWorkspaceFile("scripts", "run_golden_generation_test.ps1"),
            ResolveWorkspaceFile("scripts", "run_golden_generation_test_final.ps1"),
            ResolveWorkspaceFile("doc", "operator", "README.md")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("/api/gen" + "esis/generate", text, StringComparison.Ordinal);
            Assert.DoesNotContain("/api/gen" + "esis/research", text, StringComparison.Ordinal);
            Assert.DoesNotContain("/hubs/gen" + "esis", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("invoke_gen" + "esis_cli.ps1", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ActiveArchitecturePlanFilenames_Are_Free_Of_Legacy_Namespace_Tokens()
    {
        var architectureRoot = ResolveWorkspaceFile("doc", "architecture");
        var legacyNamedFiles = Directory.GetFiles(architectureRoot, "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => ContainsBlockedToken(name))
            .ToArray();

        Assert.Empty(legacyNamedFiles);
    }

    [Fact]
    public void OperatorRunbooks_And_GoldenScripts_Use_Helper_First_Entrypoints()
    {
        var runbookFiles = new[]
        {
            ResolveWorkspaceFile("doc", "operator", "README.md"),
            ResolveWorkspaceFile("doc", "certification", "reference", "runbook_template_rollback.md"),
            ResolveWorkspaceFile("doc", "certification", "reference", "runbook_golden_promotion.md"),
            ResolveWorkspaceFile("doc", "certification", "reference", "certification_protocol_golden_template.md")
        };

        foreach (var file in runbookFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("invoke_gen" + "esis_cli.ps1", text, StringComparison.Ordinal);
            Assert.Contains("invoke_helper_cli.ps1", text, StringComparison.Ordinal);
        }

        var goldenScripts = new[]
        {
            ResolveWorkspaceFile("scripts", "run_golden_generation_test.ps1"),
            ResolveWorkspaceFile("scripts", "run_golden_generation_test_final.ps1")
        };

        foreach (var file in goldenScripts)
        {
            var text = File.ReadAllText(file);
            Assert.Contains("/api/helper/generate", text, StringComparison.Ordinal);
            Assert.DoesNotContain("/api/gen" + "esis/generate", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OperatorVisible_Runtime_StringLiterals_Stay_HelperOnly()
    {
        var files = new[]
        {
            ResolveWorkspaceFile("src", "Helper.Runtime.Cli", "HelperCliCommandDispatcher.cs"),
            ResolveWorkspaceFile("src", "Helper.Runtime", "MetacognitiveAgent.cs"),
            ResolveWorkspaceFile("src", "Helper.Runtime", "WindowsSandboxProvider.cs"),
            ResolveWorkspaceFile("src", "Helper.Runtime", "HelperOrchestrator.cs"),
            ResolveWorkspaceFile("src", "Helper.Runtime", "Core", "Contracts", "ConversationContracts.cs"),
            ResolveWorkspaceFile("src", "Helper.Runtime", "PersonalityManager.cs"),
            ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ChatTurnImmediateService.cs")
        };

        foreach (var file in files)
        {
            var stringLiterals = System.Text.RegularExpressions.Regex.Matches(
                    File.ReadAllText(file),
                    "\"(?:\\\\.|[^\"])*\"")
                .Select(static match => match.Value)
                .ToArray();

            Assert.DoesNotContain(stringLiterals, literal => ContainsBlockedToken(literal));
        }
    }

    [Fact]
    public void PublicWorkingTree_Files_And_Contents_Are_Free_Of_Disallowed_Brand_Tokens()
    {
        var workspaceRoot = ResolveWorkspaceFile();
        var roots = new[]
        {
            ResolveWorkspaceFile(".github"),
            ResolveWorkspaceFile("components"),
            ResolveWorkspaceFile("contexts"),
            ResolveWorkspaceFile("src"),
            ResolveWorkspaceFile("test"),
            ResolveWorkspaceFile("hooks"),
            ResolveWorkspaceFile("scripts"),
            ResolveWorkspaceFile("services"),
            ResolveWorkspaceFile("doc"),
            ResolveWorkspaceFile("eval")
        };
        var files = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}TestResults{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                IsPublicRepoTextFile(path))
            .Concat(Directory.GetFiles(workspaceRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(IsPublicRepoTextFile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(workspaceRoot, file);
            Assert.False(ContainsBlockedToken(relativePath), $"Blocked token found in file path: {relativePath}");
            Assert.False(ContainsBlockedToken(File.ReadAllText(file)), $"Blocked token found in file content: {file}");
        }
    }

    [Fact]
    public void ConversationInterfaceFiles_Remain_InterfaceOnly_And_Use_Explicit_Type_Names()
    {
        var conversationRoot = ResolveWorkspaceFile("src", "Helper.Api", "Conversation");
        var interfaceFiles = Directory.GetFiles(conversationRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                return !string.IsNullOrWhiteSpace(name) &&
                       name!.Length > 1 &&
                       name[0] == 'I' &&
                       char.IsUpper(name[1]);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(interfaceFiles);
        Assert.True(File.Exists(ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ChatTurnCritic.cs")));
        Assert.True(File.Exists(ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ChatTurnPlanner.cs")));
        Assert.True(File.Exists(ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ClaimExtractionService.cs")));
        Assert.True(File.Exists(ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ClaimSourceMatcher.cs")));
        Assert.True(File.Exists(ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ConversationState.cs")));
        Assert.True(File.Exists(ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "MemoryPolicyService.cs")));
        Assert.True(File.Exists(ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "UserProfileService.cs")));

        Assert.All(interfaceFiles, path =>
        {
            var text = File.ReadAllText(path);
            Assert.Contains("interface", text, StringComparison.Ordinal);
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(class|record|struct)\b"), $"{Path.GetFileName(path)} should stay interface-only.");
        });
    }

    private static bool ContainsBlockedToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return BlockedBrandTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPublicRepoTextFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName.StartsWith(".env.local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            return fileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals(".ignore", StringComparison.OrdinalIgnoreCase);
        }

        return extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".config", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".css", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".sh", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".targets", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".yml", StringComparison.OrdinalIgnoreCase);
    }
}
