namespace Helper.Runtime.Tests;

public sealed class ConversationProjectContextUiArchitectureTests
{
    [Fact]
    public void Settings_ProjectContext_Surface_Uses_ProjectScoped_Continuity_Collections()
    {
        var scopeHelper = TestWorkspaceRoot.ReadAllText("services", "projectContextContinuityScope.ts");
        var hook = TestWorkspaceRoot.ReadAllText("hooks", "useSettingsViewState.ts");
        var view = TestWorkspaceRoot.ReadAllText("components", "views", "SettingsView.tsx");
        var panel = TestWorkspaceRoot.ReadAllText("components", "settings", "SettingsProjectContextPanel.tsx");

        Assert.Contains("filterProjectScopedBackgroundTasks", scopeHelper, StringComparison.Ordinal);
        Assert.Contains("filterProjectScopedProactiveTopics", scopeHelper, StringComparison.Ordinal);
        Assert.Contains("if (!normalizedProjectId)", scopeHelper, StringComparison.Ordinal);
        Assert.Contains("return [];", scopeHelper, StringComparison.Ordinal);

        Assert.Contains("filterProjectScopedBackgroundTasks(backgroundTasks, projectId)", hook, StringComparison.Ordinal);
        Assert.Contains("filterProjectScopedProactiveTopics(proactiveTopics, projectId)", hook, StringComparison.Ordinal);
        Assert.Contains("applyConversationSnapshot(await getConversationSnapshot(conversationId));", hook, StringComparison.Ordinal);
        Assert.Contains("backgroundTasks: projectScopedBackgroundTasks", hook, StringComparison.Ordinal);
        Assert.Contains("proactiveTopics: projectScopedProactiveTopics", hook, StringComparison.Ordinal);

        Assert.Contains("backgroundTasks={state.projectScopedBackgroundTasks}", view, StringComparison.Ordinal);
        Assert.Contains("proactiveTopics={state.projectScopedProactiveTopics}", view, StringComparison.Ordinal);
        Assert.Contains("No active project.", panel, StringComparison.Ordinal);
    }
}
