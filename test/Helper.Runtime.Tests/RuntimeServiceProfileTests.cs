using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Helper.Runtime.Tests;

public sealed class RuntimeServiceProfileTests
{
    [Fact]
    public async Task AddHelperApplicationServices_UsesDisabledExecutor_ByDefault()
    {
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            [RuntimeServiceProfileSupport.PrototypeRuntimeServicesEnvName] = "false"
        });
        using var temp = new TempDirectoryScope("helper-runtime-profile-");
        using var provider = BuildServices(temp.Path).BuildServiceProvider();

        var executor = provider.GetRequiredService<ICodeExecutor>();
        var result = await executor.ExecuteAsync("print('hello')", "python");

        Assert.IsType<DisabledCodeExecutor>(executor);
        Assert.False(result.Success);
        Assert.Contains("disabled in the production runtime profile", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddHelperApplicationServices_AllowsPrototypeExecutor_WhenExplicitlyEnabled()
    {
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            [RuntimeServiceProfileSupport.PrototypeRuntimeServicesEnvName] = "true"
        });
        using var temp = new TempDirectoryScope("helper-runtime-profile-");
        using var provider = BuildServices(temp.Path).BuildServiceProvider();

        var executor = provider.GetRequiredService<ICodeExecutor>();
        var result = await executor.ExecuteAsync("print('hello')", "python");

        Assert.IsType<PythonSandbox>(executor);
        Assert.False(result.Success);
        Assert.Contains("not implemented", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static IServiceCollection BuildServices(string root)
    {
        var services = new ServiceCollection();
        Directory.CreateDirectory(root);
        var runtimeConfig = new ApiRuntimeConfig(
            root,
            Path.Combine(root, "data"),
            Path.Combine(root, "projects"),
            Path.Combine(root, "library"),
            Path.Combine(root, "logs"),
            Path.Combine(root, "templates"),
            "primary-key");

        services.AddSingleton(runtimeConfig);
        services.AddHelperApplicationServices(runtimeConfig);
        return services;
    }
}
