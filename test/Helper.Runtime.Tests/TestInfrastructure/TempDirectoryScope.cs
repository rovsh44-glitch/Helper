namespace Helper.Testing;

public sealed class TempDirectoryScope : IDisposable
{
    public TempDirectoryScope()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"helper-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
