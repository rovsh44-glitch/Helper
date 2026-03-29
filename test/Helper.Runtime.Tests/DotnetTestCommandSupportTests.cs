using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class DotnetTestCommandSupportTests
{
    [Fact]
    public void NormalizeFilter_RebuildsMalformedFullyQualifiedNameSequence()
    {
        const string malformed =
            "FullyQualifiedNameConversationRuntimeTestsFullyQualifiedNameSimpleResearcherTests FullyQualifiedNameOperatorResponseComposerTests";

        var normalized = DotnetTestCommandSupport.NormalizeFilter(malformed, Array.Empty<string>());

        Assert.Equal(
            "FullyQualifiedName~ConversationRuntimeTests|FullyQualifiedName~SimpleResearcherTests|FullyQualifiedName~OperatorResponseComposerTests",
            normalized);
    }

    [Fact]
    public void TryParseShellCommand_ParsesDotnetTestAndPreservesNonFilterArguments()
    {
        const string command =
            "dotnet test test\\Helper.Runtime.Tests\\Helper.Runtime.Tests.csproj --no-build -m:1 --filter \"ConversationRuntimeTests SimpleResearcherTests\"";

        var success = DotnetTestCommandSupport.TryParseShellCommand(command, out var invocation, out var error);

        Assert.True(success, error);
        Assert.Equal("test\\Helper.Runtime.Tests\\Helper.Runtime.Tests.csproj", invocation.Target);
        Assert.Equal(new[] { "--no-build", "-m:1" }, invocation.BaseArguments);
        Assert.Equal(
            "FullyQualifiedName~ConversationRuntimeTests|FullyQualifiedName~SimpleResearcherTests",
            invocation.FilterExpression);
        Assert.False(invocation.ApplyDefaultArgumentsWhenEmpty);
    }

    [Fact]
    public void TryCreateInvocationFromToolArguments_BuildsFilterFromClassNames()
    {
        var arguments = new Dictionary<string, object>
        {
            ["target"] = "test\\Helper.Runtime.Tests\\Helper.Runtime.Tests.csproj",
            ["classNames"] = new[] { "ConversationRuntimeTests", "SimpleResearcherTests", "OperatorResponseComposerTests" },
            ["baseArguments"] = new[] { "--no-build", "-m:1" },
            ["showProcessMonitor"] = true
        };

        var success = DotnetTestCommandSupport.TryCreateInvocationFromToolArguments(arguments, out var invocation, out var error);

        Assert.True(success, error);
        Assert.Equal(
            "FullyQualifiedName~ConversationRuntimeTests|FullyQualifiedName~SimpleResearcherTests|FullyQualifiedName~OperatorResponseComposerTests",
            invocation.FilterExpression);
        Assert.Equal(new[] { "--no-build", "-m:1" }, invocation.BaseArguments);
        Assert.True(invocation.ShowProcessMonitor);
    }
}

