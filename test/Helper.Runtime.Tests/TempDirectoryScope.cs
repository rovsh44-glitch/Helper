namespace Helper.Runtime.Tests;

internal sealed class TempDirectoryScope : IDisposable
{
    public TempDirectoryScope(string prefix = "helper-tests")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
