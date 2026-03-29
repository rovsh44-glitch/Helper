namespace Helper.Api.Hosting;

internal static class ApiRuntimePathResolver
{
    public static string ResolveUnderRoot(string? primary, string? secondary, string rootPath, string fallbackRelative)
    {
        var raw = string.IsNullOrWhiteSpace(primary) ? secondary : primary;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = fallbackRelative;
        }

        if (!Path.IsPathRooted(raw))
        {
            raw = Path.Combine(rootPath, raw);
        }

        return Path.GetFullPath(raw);
    }
}

