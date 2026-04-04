namespace Helper.Runtime.Tests;

internal sealed class TempDirectoryScope : IDisposable
{
    public TempDirectoryScope(string prefix = "helper_compile_path_")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        TryDeleteDirectory(Path);
    }

    public static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // keep temp scope for diagnostics if cleanup fails
        }
    }
}

internal sealed class EnvScope : IDisposable
{
    private readonly IReadOnlyDictionary<string, string?> _snapshot;

    public EnvScope(IReadOnlyDictionary<string, string?> variables)
    {
        _snapshot = variables.ToDictionary(
            pair => pair.Key,
            pair => Environment.GetEnvironmentVariable(pair.Key),
            StringComparer.Ordinal);

        foreach (var pair in variables)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    public void Dispose()
    {
        foreach (var pair in _snapshot)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
