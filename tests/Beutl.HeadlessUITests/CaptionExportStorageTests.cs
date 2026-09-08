using Avalonia.Platform.Storage;
using Beutl.Services.AI;
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
    public async Task NonLocalExport_PublishesThroughProviderStaging_WithNonSeekableStreams()
    {
        StorageTransactionMocks storage = CreateStorageTransaction();
        byte[]? stagedBytes = null;
        storage.StagedFile
            .Setup(file => file.OpenWriteAsync())
            .ReturnsAsync(() => new NonSeekableCommitStream(bytes => stagedBytes = bytes));

        await CaptionExportStorage.WriteAsync(
            storage.Destination.Object,
            "new captions"u8.ToArray(),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stagedBytes, Is.EqualTo("new captions"u8.ToArray()));
            storage.Destination.Verify(
                file => file.MoveAsync(storage.BackupFolder.Object),
                Times.Once);
            storage.StagedFile.Verify(
                file => file.MoveAsync(storage.Parent.Object),
                Times.Once);
            storage.BackupFile.Verify(file => file.DeleteAsync(), Times.Once);
        }
    }

    [Test]
    public void NonLocalExport_StagingWriteFailure_DoesNotMoveExistingDestination()
    {
        StorageTransactionMocks storage = CreateStorageTransaction();
        storage.StagedFile
            .Setup(file => file.OpenWriteAsync())
            .ReturnsAsync(() => new NonSeekableCommitStream(
                _ => { },
                new IOException("Injected staged write failure.")));

        Assert.ThrowsAsync<IOException>(async () =>
            await CaptionExportStorage.WriteAsync(
                storage.Destination.Object,
                "new captions"u8.ToArray(),
                CancellationToken.None));

        storage.Destination.Verify(
            file => file.MoveAsync(It.IsAny<IStorageFolder>()),
            Times.Never);
    }

    [Test]
    public void NonLocalExport_PublishFailure_RestoresExistingDestination()
    {
        StorageTransactionMocks storage = CreateStorageTransaction();
        storage.StagedFile
            .Setup(file => file.OpenWriteAsync())
            .ReturnsAsync(() => new NonSeekableCommitStream(_ => { }));
        storage.StagedFile
            .Setup(file => file.MoveAsync(storage.Parent.Object))
            .ThrowsAsync(new IOException("Injected staged publish failure."));

        Assert.ThrowsAsync<IOException>(async () =>
            await CaptionExportStorage.WriteAsync(
                storage.Destination.Object,
                "new captions"u8.ToArray(),
                CancellationToken.None));

        storage.BackupFile.Verify(
            file => file.MoveAsync(storage.Parent.Object),
            Times.Once);
        storage.BackupFile.Verify(file => file.DeleteAsync(), Times.Never);
    }

    private static StorageTransactionMocks CreateStorageTransaction()
    {
        var destination = new Mock<IStorageFile>(MockBehavior.Strict);
        var parent = new Mock<IStorageFolder>(MockBehavior.Strict);
        var stagingFolder = new Mock<IStorageFolder>(MockBehavior.Strict);
        var backupFolder = new Mock<IStorageFolder>(MockBehavior.Strict);
        var stagedFile = new Mock<IStorageFile>(MockBehavior.Strict);
        var backupFile = new Mock<IStorageFile>(MockBehavior.Strict);
        var committedFile = new Mock<IStorageFile>(MockBehavior.Strict);
        var restoredFile = new Mock<IStorageFile>(MockBehavior.Strict);

        destination.SetupGet(file => file.Path).Returns(new Uri("content://test/subtitles.srt"));
        destination.SetupGet(file => file.Name).Returns("subtitles.srt");
        destination.Setup(file => file.GetParentAsync()).ReturnsAsync(parent.Object);
        destination.Setup(file => file.MoveAsync(backupFolder.Object)).ReturnsAsync(backupFile.Object);

        parent.Setup(folder => folder.CreateFolderAsync(
                It.Is<string>(name => name.EndsWith("-stage", StringComparison.Ordinal))))
            .ReturnsAsync(stagingFolder.Object);
        parent.Setup(folder => folder.CreateFolderAsync(
                It.Is<string>(name => name.EndsWith("-backup", StringComparison.Ordinal))))
            .ReturnsAsync(backupFolder.Object);
        parent.Setup(folder => folder.GetFileAsync("subtitles.srt"))
            .ReturnsAsync((IStorageFile?)null);

        stagingFolder.Setup(folder => folder.CreateFileAsync("subtitles.srt"))
            .ReturnsAsync(stagedFile.Object);
        stagedFile.Setup(file => file.MoveAsync(parent.Object)).ReturnsAsync(committedFile.Object);
        backupFile.Setup(file => file.MoveAsync(parent.Object)).ReturnsAsync(restoredFile.Object);

        SetupCleanup(stagedFile);
        SetupCleanup(backupFile);
        SetupCleanup(stagingFolder);
        SetupCleanup(backupFolder);
        destination.Setup(file => file.Dispose());
        parent.Setup(folder => folder.Dispose());
        committedFile.Setup(file => file.Dispose());
        restoredFile.Setup(file => file.Dispose());

        return new StorageTransactionMocks(
            destination,
            parent,
            stagingFolder,
            backupFolder,
            stagedFile,
            backupFile);
    }

    private static void SetupCleanup<T>(Mock<T> item)
        where T : class, IStorageItem
    {
        item.Setup(value => value.DeleteAsync()).Returns(Task.CompletedTask);
        item.Setup(value => value.Dispose());
    }

    private sealed record StorageTransactionMocks(
        Mock<IStorageFile> Destination,
        Mock<IStorageFolder> Parent,
        Mock<IStorageFolder> StagingFolder,
        Mock<IStorageFolder> BackupFolder,
        Mock<IStorageFile> StagedFile,
        Mock<IStorageFile> BackupFile);

    private sealed class NonSeekableCommitStream(
        Action<byte[]> commit,
        Exception? writeFailure = null) : Stream
    {
        private readonly MemoryStream _buffer = new();
        private bool _completed;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (writeFailure is not null)
                throw writeFailure;

            _buffer.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (writeFailure is not null)
                return ValueTask.FromException(writeFailure);

            _buffer.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_completed)
            {
                _completed = true;
                if (writeFailure is null)
                    commit(_buffer.ToArray());
                _buffer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
