using Avalonia.Platform.Storage;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.ViewModels.Dialogs;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class CaptionExportStorageTests
{
    [Test]
    public async Task LocalExport_PreservesExistingUnixPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix permissions are not available on Windows.");
            return;
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            $"beutl-caption-permissions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string destinationPath = Path.Combine(directory, "subtitles.srt");
        try
        {
            await File.WriteAllTextAsync(destinationPath, "old captions");
            UnixFileMode mode = UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupWrite
                | UnixFileMode.OtherRead;
            File.SetUnixFileMode(destinationPath, mode);
            var destination = new Mock<IStorageFile>(MockBehavior.Strict);
            destination.SetupGet(file => file.Path).Returns(new Uri(destinationPath));

            await CaptionExportStorage.WriteAsync(
                destination.Object,
                "new captions"u8.ToArray(),
                CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(await File.ReadAllTextAsync(destinationPath), Is.EqualTo("new captions"));
                Assert.That(File.GetUnixFileMode(destinationPath), Is.EqualTo(mode));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task NonLocalExport_IsRefusedWithoutTouchingTheProvider()
    {
        // The desktop save pickers never hand out such a file.
        var destination = new Mock<IStorageFile>(MockBehavior.Strict);
        destination.SetupGet(file => file.Path).Returns(new Uri("content://test/subtitles.srt"));

        await Assert.ThrowsAsync<NotSupportedException>(() => CaptionExportStorage.WriteAsync(
            destination.Object,
            "new captions"u8.ToArray(),
            CancellationToken.None));
    }
}
