using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class ApiRuntimePathResolverTests
{
    [Fact]
    public void ResolveUnderRoot_UsesAbsolutePrimaryPath_WithoutRebasing()
    {
        var resolved = ApiRuntimePathResolver.ResolveUnderRoot(
            primary: @"D:\helper-data\LOG",
            secondary: @"library",
            rootPath: @"C:\helper-root",
            fallbackRelative: @"temp\logs");

        Assert.Equal(Path.GetFullPath(@"D:\helper-data\LOG"), resolved);
    }

    [Fact]
    public void ResolveUnderRoot_UsesSecondaryPath_WhenPrimaryIsMissing()
    {
        var resolved = ApiRuntimePathResolver.ResolveUnderRoot(
            primary: null,
            secondary: @"library",
            rootPath: @"C:\helper-root",
            fallbackRelative: @"temp\logs");

        Assert.Equal(Path.GetFullPath(@"C:\helper-root\library"), resolved);
    }

    [Fact]
    public void ResolveUnderRoot_UsesFallbackRelativePath_WhenNoOverridesExist()
    {
        var resolved = ApiRuntimePathResolver.ResolveUnderRoot(
            primary: null,
            secondary: null,
            rootPath: @"C:\helper-root",
            fallbackRelative: @"temp\logs");

        Assert.Equal(Path.GetFullPath(@"C:\helper-root\temp\logs"), resolved);
    }
}

