namespace Beutl.UnitTests.TestInfrastructure;

// A unique path under the temp directory, deleted with its contents on Dispose if it exists. The
// directory is not created, so a test can tell whether the code under test created it.
public sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
