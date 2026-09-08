using Avalonia.Platform.Storage;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class SharedFilePickerOptionsTests
{
    [Test]
    public void StorageFileOwnership_ReleasesEveryPickerResultAndIsIdempotent()
    {
        var first = new Mock<IStorageFile>(MockBehavior.Strict);
        var second = new Mock<IStorageFile>(MockBehavior.Strict);
        first.Setup(file => file.Dispose()).Throws(new IOException("release failed"));
        second.Setup(file => file.Dispose());
        IDisposable ownership = SharedFilePickerOptions.OwnStorageFiles(
            [first.Object, second.Object]);

        Assert.DoesNotThrow(ownership.Dispose);
        Assert.DoesNotThrow(ownership.Dispose);

        first.Verify(file => file.Dispose(), Times.Once);
        second.Verify(file => file.Dispose(), Times.Once);
    }
}
