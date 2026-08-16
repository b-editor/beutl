using Beutl.Services.AI;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiTemporaryFileStoreTests
{
    [Test]
    public void Create_UsesPrivateHomeDirectoryAndFilePermissions()
    {
        (string path, FileStream stream) = AiTemporaryFileStore.Create(
            "tests",
            "private",
            ".png");
        using (stream)
        {
            stream.WriteByte(1);
        }

        try
        {
            Assert.That(
                Path.GetFullPath(path),
                Does.StartWith(Path.GetFullPath(BeutlEnvironment.GetHomeDirectoryPath())));
            if (!OperatingSystem.IsWindows())
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(
                        File.GetUnixFileMode(path),
                        Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
                    Assert.That(
                        File.GetUnixFileMode(Path.GetDirectoryName(path)!),
                        Is.EqualTo(
                            UnixFileMode.UserRead
                            | UnixFileMode.UserWrite
                            | UnixFileMode.UserExecute));
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Cleanup_RemovesOnlyStaleFiles()
    {
        string directory = AiTemporaryFileStore.GetCategoryDirectory(
            $"cleanup-{Guid.NewGuid():N}");
        AiTemporaryFileStore.EnsurePrivateDirectory(directory);
        string stale = Path.Combine(directory, "stale.wav");
        string recent = Path.Combine(directory, "recent.wav");
        File.WriteAllBytes(stale, [1]);
        File.WriteAllBytes(recent, [2]);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(stale, (now - AiTemporaryFileStore.StaleAge - TimeSpan.FromMinutes(1)).UtcDateTime);

        AiTemporaryFileStore.CleanStaleFiles(directory, now);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(stale), Is.False);
            Assert.That(File.Exists(recent), Is.True);
        }
        Directory.Delete(directory, recursive: true);
    }
}
